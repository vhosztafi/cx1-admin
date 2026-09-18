using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingRatingOutcome(string Format, Guid OperationId, string Outcome,
    DateTimeOffset CompletedAt, DateTimeOffset ExpiresAt, CalculatedServicingRating? Rating);

public sealed class ServicingRatingWorker(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = ServicingRatingService.Json;

    public async Task<ServicingRatingOutcome> ExecuteProviderAsync(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind != ServicingRatingService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var cycle = await db.Set<ServicingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token)
            ?? throw Failure(JobFailure.InvalidPayload);
        var input = ReadInput(cycle);
        if (input.ScenarioVersionId != lease.ScenarioVersionId || lease.OperationKey != $"servicing-rating/{cycle.Id:N}") throw Failure(JobFailure.ProviderConflict);
        var setting = await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == lease.ScenarioVersionId, token);
        var scenario = UnderwritingRuntimeConfiguration.Scenario(setting.Values) ?? throw Failure(JobFailure.InvalidPayload);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var operation = await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed = operation is not null;
        if (operation is not null && (operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, cycle.InputHash))) throw Failure(JobFailure.ProviderConflict);
        if (operation?.Result is not null)
        {
            var previous = JsonSerializer.Deserialize<ServicingRatingOutcome>(operation.Result, Json) ?? throw Failure(JobFailure.ProviderConflict);
            await tx.CommitAsync(token); return previous;
        }
        if (operation is null)
        {
            operation = new DemoProviderOperation { Kind = lease.Kind, OperationKey = lease.OperationKey, RequestHash = cycle.InputHash,
                ScenarioVersionId = lease.ScenarioVersionId, CreatedAt = time.GetUtcNow() };
            db.Add(operation);
        }
        if (!existed && scenario == "fail-once")
        {
            operation.State = "transient-failed"; await db.SaveChangesAsync(token); await tx.CommitAsync(token); throw Failure(JobFailure.ProviderUnavailable);
        }
        var calculated = scenario == "reject" ? null : ServicingRatingInput.Calculate(input);
        var now = time.GetUtcNow();
        var outcome = new ServicingRatingOutcome("servicing-rating-result-1", operation.Id, scenario == "reject" ? "rejected" : "rated", now,
            now.AddDays(input.RatingDefinition.GetProperty("quoteValidityDays").GetInt32()), calculated);
        operation.State = calculated is null ? "rejected" : "succeeded"; operation.CompletedAt = now; operation.Result = JsonSerializer.Serialize(outcome, Json);
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        if (!existed && scenario == "timeout-after-success") throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<bool> ApplyAsync(JobLease lease, ServicingRatingOutcome outcome, CancellationToken token = default)
    {
        if (lease.Kind != ServicingRatingService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db = await factory.CreateDbContextAsync(token);
        var hint = await db.Set<ServicingCycle>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == lease.WorkId, token) ?? throw Failure(JobFailure.InvalidPayload);
        var sourceId = await db.Set<Policy>().Where(x => x.Id == hint.PolicyId).Select(x => x.SourceQuoteId).SingleAsync(token);
        var agencyId = await db.Set<Quote>().Where(x => x.Id == sourceId).Select(x => x.AgencyId).SingleAsync(token);
        var reference = await IdentitySnapshot.Reference(db, hint.RequestedBy, token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        // Keep the same agency/identity/source/policy/term/draft -> work -> cycle
        // order as requests. Revoked actors still retain immutable provider history.
        await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK) WHERE Id={agencyId}").AsNoTracking().SingleAsync(token);
        var identity = reference is null ? null : await IdentitySnapshot.Lock(db, reference, token);
        await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK) WHERE Id={sourceId}").AsNoTracking().SingleAsync(token);
        await db.Set<Policy>().FromSqlInterpolated($"SELECT * FROM Policy WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.PolicyId}").AsNoTracking().SingleAsync(token);
        await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.BaseTermId}").AsNoTracking().SingleAsync(token);
        var draft = await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.DraftId}").SingleAsync(token);
        var input = ReadInput(hint); var now = time.GetUtcNow(); HeldServicingRating? eligible = null;
        if (identity is not null && draft.State == "draft" && draft.CurrentCycleId == hint.Id && draft.CurrentRevisionId == hint.RevisionId &&
            hint.State == "rating-pending" && outcome.ExpiresAt > now)
        {
            try
            {
                var actor = new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal));
                eligible = await ServicingRatingScope.HoldAsync(db, actor, draft.Id, now, token);
                var latest = await db.Set<PolicyVersion>().Where(x => x.PolicyId == hint.PolicyId && x.TermId == hint.BaseTermId)
                    .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Sequence).Select(x => x.Id).FirstAsync(token);
                if (eligible.Renewal is null && latest != hint.BaseVersionId || !ServicingRatingScope.Matches(eligible, hint, input)) eligible = null;
            }
            catch (QuoteOperationException) { eligible = null; }
        }
        var work = await SqlJobLeases.OwnedAsync(db, lease, now, token); if (work is null) return false;
        var cycle = await db.Set<ServicingCycle>().FromSqlInterpolated($"SELECT * FROM ServicingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={hint.Id}").SingleAsync(token);
        if (work.Kind != lease.Kind || work.OperationKey != lease.OperationKey || work.ScenarioVersionId != lease.ScenarioVersionId || work.SubjectRecordId != cycle.Id)
            throw Failure(JobFailure.ProviderConflict);
        var serialized = JsonSerializer.Serialize(outcome, Json);
        var operation = await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == outcome.OperationId && x.Kind == work.Kind && x.OperationKey == work.OperationKey, token);
        if (operation?.Result != serialized || operation.ScenarioVersionId != lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash, cycle.InputHash))
            throw Failure(JobFailure.ProviderConflict);
        // A terminal queue transition can race while ownership locks are held.
        if (cycle.State != "rating-pending") eligible = null;
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        var rating = outcome.Rating;
        var result = new ServicingRatingResult { CycleId = cycle.Id, DraftId = draft.Id, RevisionId = cycle.RevisionId, WorkId = work.Id,
            AttemptId = attempt.Id, ProviderOperationId = outcome.OperationId, RuleVersionId = cycle.RatingRuleVersionId, InputHash = cycle.InputHash,
            ResultHash = SHA256.HashData(Encoding.UTF8.GetBytes(serialized)), ResultJson = serialized, Outcome = outcome.Outcome,
            CreatedAt = outcome.CompletedAt, CompletedAt = outcome.CompletedAt, ExpiresAt = outcome.ExpiresAt,
            BaseAnnualPremium = rating?.BaseAnnualPremium ?? 0, Premium = rating?.Premium ?? 0, Tax = rating?.Tax ?? 0,
            Fee = rating?.Fee ?? 0, BrokerCommission = rating?.BrokerCommission ?? 0, GrossPayable = rating?.GrossPayable ?? 0, NetDue = rating?.NetDue ?? 0 };
        db.Add(result); await db.SaveChangesAsync(token);
        if (eligible is not null)
        { cycle.CurrentRatingId = rating is null ? null : result.Id; cycle.State = rating is null ? "failed" : "rated"; cycle.UpdatedAt = now; }
        else if (cycle.State == "rating-pending") { cycle.State = "failed"; cycle.UpdatedAt = now; }
        if (eligible is not null && rating is not null)
        {
            // Assess every cumulative risk using its annual price, including
            // temporary cover that a later change removes again.
            if (rating.Slices.Count != input.Slices.Count) throw Failure(JobFailure.ProviderConflict);
            var slices = new List<ServicingAuthoritySlice>();
            for (var index = 0; index < input.Slices.Count; index++)
            {
                var source = input.Slices[index]; var priced = rating.Slices[index];
                // Each price movement earns through the term end.
                var endsAt = input.Term.EndsAt;
                if (source.EffectiveAt != priced.EffectiveAt || priced.CoverageEndsAt != endsAt ||
                    !source.ChangeIds.Order().SequenceEqual(priced.ChangeIds.Order())) throw Failure(JobFailure.ProviderConflict);
                slices.Add(new(source.EffectiveAt, source.Input.RiskForPremium(priced.AnnualPremium)));
            }
            var needs = ServicingReferralRules.Assess(input.Term, slices, eligible.Eligible.Binder, eligible.Eligible.Authority,
                input.RatingDefinition.GetProperty("minimumTradingYears").GetInt32()).ToList();
            if (input.Renewal is { } renewal)
            {
                var experience = RenewalPreparationRules.Experience(renewal.Experience, renewal.EvidenceAccepted, input.RequestedAt,
                    renewal.ThresholdBasisPoints, renewal.LoadingBasisPoints);
                var code = !experience.InformationComplete ? "UW-31-information" : experience.RequiresSeniorDecision ? "UW-31" : null;
                if (code is not null)
                {
                    var requirement = new UnderwritingRequirement(code, "renewal-experience", RequestedAmount: experience.LossRatio,
                        AuthorisedAmount: renewal.ThresholdBasisPoints / 10000m);
                    needs.Add(new(code, "renewal-experience", null, [new(input.Term.StartsAt, "source", requirement)]));
                }
            }
            // Referral provenance guards require the current rated pointer to
            // exist; both saves remain within this worker transaction.
            await db.SaveChangesAsync(token);
            var sequence = 0;
            foreach (var need in needs)
                db.Add(new ServicingReferral { DraftId = draft.Id, CycleId = cycle.Id, RevisionId = cycle.RevisionId,
                    RatingId = result.Id, Sequence = ++sequence, RuleCode = need.RuleCode, Dimension = need.Dimension,
                    RiskItemId = need.RiskItemId, TargetKey = need.RiskItemId ?? Guid.Empty,
                    RequiredAuthorityJson = JsonSerializer.Serialize(new { triggers = need.Triggers }, Json),
                    Reason = "Review required: " + need.Dimension, CreatedBy = cycle.RequestedBy, CreatedAt = now, UpdatedAt = now });
        }
        attempt.EndedAt = now; attempt.Outcome = eligible is null ? "superseded" : rating is null ? "rejected" : "succeeded";
        attempt.Response = JsonSerializer.Serialize(new { cycleId = cycle.Id, ratingId = result.Id, applicable = eligible is not null }, Json);
        work.State = "succeeded"; work.CompletedAt = now; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null; work.Result = attempt.Response;
        if (rating is null) await SqlJobLeases.MarkTerminalAsync(db, work, "provider-rejected", now, token);
        db.Add(new AuditEvent { ActorId = cycle.RequestedBy, EventType = "servicing.rating-completed", OccurredAt = now,
            CorrelationId = work.CorrelationId, SubjectRecordId = draft.Id, After = attempt.Response });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return true;
    }

    private static ServicingRatingRequestInput ReadInput(ServicingCycle cycle)
    {
        try { return ServicingRatingInput.Read(cycle.InputJson, cycle.InputHash); }
        catch (ArgumentException) { throw Failure(JobFailure.InvalidPayload); }
    }
    private static QuoteRatingProviderException Failure(JobFailure failure) => new(failure);
}
