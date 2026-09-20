using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record QuoteRatingOutcome(Guid OperationId, string Outcome, DateTimeOffset CompletedAt, DateTimeOffset ExpiresAt, CalculatedQuoteRating? Rating);
public sealed class QuoteRatingProviderException(JobFailure failure) : Exception("The demo rating did not complete.")
{ public JobFailure Failure { get; } = failure; }

// Fictional durable provider. Completion commits separately from application so
// a crash or timeout cannot result in a second financial calculation/operation.
public sealed class QuoteRatingWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = QuoteRatingService.Json;

    public async Task<QuoteRatingOutcome> ExecuteProviderAsync(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind != QuoteRatingService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        var input = ReadInput(cycle);
        if (input.ScenarioVersionId != lease.ScenarioVersionId || lease.OperationKey != $"quote-rating/{cycle.Id:N}") throw Failure(JobFailure.ProviderConflict);
        var setting = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == lease.ScenarioVersionId, token);
        var scenario = UnderwritingRuntimeConfiguration.Scenario(setting.Values) ?? throw Failure(JobFailure.InvalidPayload);
        var rules = await db.Set<RatingRuleVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.RatingRuleVersionId, token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var operation = await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, cycle.PricingInputHash))) throw Failure(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        {
            var previous = JsonSerializer.Deserialize<QuoteRatingOutcome>(operation.Result, Json) ?? throw Failure(JobFailure.ProviderConflict);
            await tx.CommitAsync(token); return previous;
        }
        if (operation is null)
        {
            operation = new DemoProviderOperation { Kind = lease.Kind, OperationKey = lease.OperationKey, RequestHash = cycle.PricingInputHash, ScenarioVersionId = lease.ScenarioVersionId, CreatedAt = time.GetUtcNow() };
            db.Add(operation);
        }
        if (!existed && scenario == "fail-once")
        {
            operation.State = "transient-failed"; await db.SaveChangesAsync(token); await tx.CommitAsync(token); throw Failure(JobFailure.ProviderUnavailable);
        }
        using var definition = JsonDocument.Parse(rules.DefinitionJson);
        var calculated = scenario == "reject" ? null : input.IsCommercial
            ? CommercialRatingRules.Calculate(definition.RootElement, input.Commercial!.Rating, input.Term, input.CommissionBasisPoints, input.MinimumPremium)
            : QuoteRatingRules.Calculate(definition.RootElement, input.Input.Rating, input.Term, input.CommissionBasisPoints, input.MinimumPremium);
        var now = time.GetUtcNow();
        var outcome = new QuoteRatingOutcome(operation.Id, scenario == "reject" ? "rejected" : "rated", now, now.AddDays(definition.RootElement.GetProperty("quoteValidityDays").GetInt32()), calculated);
        operation.State = outcome.Outcome == "rated" ? "succeeded" : "rejected"; operation.CompletedAt = now; operation.Result = JsonSerializer.Serialize(outcome, Json);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        if (!existed && scenario == "timeout-after-success") throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<bool> ApplyAsync(JobLease lease, QuoteRatingOutcome outcome, CancellationToken token = default)
    {
        if (lease.Kind != QuoteRatingService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var hint = await db.Set<UnderwritingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        // Agency -> identity -> quote precedes queue locking; work precedes its
        // cycle as in terminal lease handling, avoiding a work/cycle lock inversion.
        // A revoked requester still gets retained provider history, never applied authority.
        var agency = await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={hint.AgencyId}").AsNoTracking().SingleAsync(token);
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(hint.RequestedBy, null), token);
        var quote = await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={hint.QuoteId}").SingleAsync(token);
        var now = time.GetUtcNow(); var work = await SqlJobLeases.OwnedAsync(db, lease, now, token);
        if (work is null) return false;
        var cycle = await db.Set<UnderwritingCycle>().FromSqlInterpolated($"SELECT * FROM UnderwritingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.Id}").SingleAsync(token);
        if (work.Kind != lease.Kind || work.OperationKey != lease.OperationKey || work.ScenarioVersionId != lease.ScenarioVersionId || work.SubjectRecordId != cycle.Id) throw Failure(JobFailure.ProviderConflict);
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == outcome.OperationId && x.Kind == work.Kind && x.OperationKey == work.OperationKey, token);
        var serialized = JsonSerializer.Serialize(outcome, Json);
        if (operation?.Result != serialized || operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, cycle.PricingInputHash)) throw Failure(JobFailure.ProviderConflict);
        var input = ReadInput(cycle); EligibleQuoteRating? eligible = null;
        if (identity is not null && quote.State == "rating-pending" && quote.CurrentRevisionId == cycle.QuoteRevisionId && quote.CurrentUnderwritingCycleId == cycle.Id &&
            quote.AgencyId == cycle.AgencyId && quote.ClientId == cycle.ClientId && quote.RelationshipId == cycle.RelationshipId && quote.ProductId == cycle.ProductId &&
            cycle.State == "rating-pending" && outcome.ExpiresAt > now)
        {
            try
            {
                var actor = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
                if (actor.HasCapability("quote-rate"))
                {
                    var owned = await QuoteUnderwritingScope.HoldAsync(db, actor, quote.Id, "quote-rate", token);
                    eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Term, now, token);
                    if (eligible.RatingVersion.Id != cycle.RatingRuleVersionId || eligible.BinderVersion.Id != cycle.BinderVersionId || eligible.AuthorityVersion.Id != cycle.AuthorityVersionId ||
                        eligible.RuntimeVersion.Id != input.RuntimeVersionId || eligible.ScenarioVersion.Id != input.ScenarioVersionId ||
                        eligible.CommissionBasisPoints != input.CommissionBasisPoints || eligible.MinimumPremium != input.MinimumPremium ||
                        (await QuoteMatching.AssessAsync(db, quote, now, token)).Code is not null) eligible = null;
                }
            }
            catch (QuoteOperationException) { eligible = null; }
        }
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        var rating = outcome.Rating;
        var result = new QuoteRatingResult { CycleId = cycle.Id, QuoteId = quote.Id, WorkId = work.Id, AttemptId = attempt.Id, RuleVersionId = cycle.RatingRuleVersionId,
            InputHash = cycle.PricingInputHash, ResultJson = serialized, Outcome = outcome.Outcome, CreatedAt = outcome.CompletedAt, CompletedAt = outcome.CompletedAt, ExpiresAt = outcome.ExpiresAt,
            AnnualPremium = rating?.AnnualPremium ?? 0, TermPremium = rating?.TermPremium ?? 0, Tax = rating?.Tax ?? 0, Fee = rating?.Fee ?? 0, GrossPayable = rating?.GrossPayable ?? 0, BrokerCommission = rating?.BrokerCommission ?? 0 };
        db.Add(result); await db.SaveChangesAsync(token);
        if (eligible is not null)
        {
            cycle.CurrentRatingId = rating is null ? null : result.Id; cycle.State = rating is null ? "failed" : "rated"; cycle.UpdatedAt = now;
            if (rating is not null && !input.IsCommercial)
            {
                var risk = input.Input.RiskForPremium(rating.AnnualPremium);
                var requirements = UnderwritingRules.AssessAuthority(eligible.Binder, risk).Concat(UnderwritingRules.AssessAuthority(eligible.Authority, risk))
                    .Concat(UnderwritingRules.SourceReferrals(risk, 5)).GroupBy(x => new { x.RuleCode, x.TargetId }).OrderBy(x => x.Key.RuleCode, StringComparer.Ordinal).ThenBy(x => x.Key.TargetId).ToArray();
                var sequence = 0;
                foreach (var group in requirements)
                {
                    var requirement = group.First();
                    db.Add(new QuoteReferral { CycleId = cycle.Id, QuoteId = quote.Id, RatingId = result.Id, Sequence = ++sequence, RuleCode = requirement.RuleCode,
                        Dimension = requirement.Dimension, RiskItemId = requirement.TargetId, TargetKey = requirement.TargetId ?? Guid.Empty,
                        RequiredAuthorityJson = JsonSerializer.Serialize(new { requirements = group.ToArray(), binderVersionId = cycle.BinderVersionId, authorityVersionId = cycle.AuthorityVersionId }, Json),
                        Reason = "Review required: " + requirement.Dimension, CreatedAt = now, UpdatedAt = now });
                }
                quote.State = requirements.Length == 0 ? "rated" : "referred";
            }
            else if (rating is not null)
            {
                var proposal = input.Commercial!.Pricing.GetProperty("proposal");
                var source = CommercialReferralRules.SourceReferrals(proposal);
                var requirements = source.Select(x => x.Requirement).Concat(CommercialReferralRules.AssessAuthority(eligible.Binder, proposal, rating.AnnualPremium))
                    .Concat(CommercialReferralRules.AssessAuthority(eligible.Authority, proposal, rating.AnnualPremium))
                    .GroupBy(x => new { x.RuleCode, x.TargetId }).OrderBy(x => x.Key.RuleCode, StringComparer.Ordinal).ThenBy(x => x.Key.TargetId).ToArray();
                var sequence = 0;
                foreach (var group in requirements)
                {
                    var requirement = group.First(); var disposition = source.FirstOrDefault(x => x.RuleCode == requirement.RuleCode && x.TargetId == requirement.TargetId)?.Disposition ?? "carrier-required";
                    db.Add(new QuoteReferral { CycleId = cycle.Id, QuoteId = quote.Id, RatingId = result.Id, Sequence = ++sequence, RuleCode = requirement.RuleCode,
                        Dimension = requirement.Dimension, RiskItemId = requirement.TargetId, TargetKey = requirement.TargetId ?? Guid.Empty,
                        RequiredAuthorityJson = JsonSerializer.Serialize(new { requirements = group.ToArray(), disposition, binderVersionId = cycle.BinderVersionId, authorityVersionId = cycle.AuthorityVersionId }, Json),
                        Reason = disposition + ": " + requirement.Dimension, CreatedAt = now, UpdatedAt = now });
                }
                quote.State = requirements.Length == 0 ? "rated" : "referred";
            }
            // Provider rejection is a rating failure, never an underwriting
            // decline decision made on behalf of a human underwriter.
            quote.UpdatedAt = now;
        }
        else if (cycle.State == "rating-pending") { cycle.State = "failed"; cycle.UpdatedAt = now; }
        attempt.EndedAt = now; attempt.Outcome = eligible is null ? "superseded" : rating is null ? "rejected" : "succeeded";
        attempt.Response = JsonSerializer.Serialize(new { cycleId = cycle.Id, ratingId = result.Id, applicable = eligible is not null });
        work.State = "succeeded"; work.CompletedAt = now; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null; work.Result = attempt.Response;
        if (outcome.Outcome == "rejected") await SqlJobLeases.MarkTerminalAsync(db, work, "provider-rejected", now, token);
        db.Add(new AuditEvent { ActorId = cycle.RequestedBy, EventType = "quote.rating-completed", OccurredAt = now, CorrelationId = work.CorrelationId, SubjectRecordId = quote.Id, After = attempt.Response });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return true;
    }

    private static StoredRatingInput ReadInput(UnderwritingCycle cycle)
    {
        try
        {
            var input = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, Json);
            if (input is null || (!input.IsCommercial && (input.Format != "underwriting-input-1" || input.Input is null || input.Commercial is not null)) || input.ScenarioVersionId == Guid.Empty || input.RuntimeVersionId == Guid.Empty) throw Failure(JobFailure.InvalidPayload);
            return input;
        }
        catch (JsonException) { throw Failure(JobFailure.InvalidPayload); }
    }
    private static QuoteRatingProviderException Failure(JobFailure failure) => new(failure);
}
