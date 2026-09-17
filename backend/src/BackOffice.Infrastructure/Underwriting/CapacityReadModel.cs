using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed class CapacityReadModel(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<Guid> QuoteForEscalationAsync(ActorContext actor, Guid id, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var quoteId = await db.Set<CapacityEscalation>().AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.QuoteId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "capacity-escalation-not-found");
        await Authorize(db, actor, quoteId, token); await tx.CommitAsync(token); return quoteId;
    }
    public async Task<Dictionary<string, object>> GetAsync(ActorContext actor, Guid id, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var row = await db.Set<CapacityEscalation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new QuoteOperationException(404, "capacity-escalation-not-found");
        var owned = await Authorize(db, actor, row.QuoteId, token);
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == row.CycleId, token);
        var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == row.ReferralId, token);
        var rating = await db.Set<QuoteRatingResult>().AsNoTracking().SingleAsync(x => x.Id == cycle.CurrentRatingId, token);
        var input = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json)!;
        var binder = await db.Set<BinderVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.BinderVersionId, token);
        using var binderDefinition = JsonDocument.Parse(binder.DefinitionJson);
        var binderContext = BackOffice.Application.Underwriting.UnderwritingAuthorityView.Rows(input.Input.RiskForPremium(rating.AnnualPremium), binderDefinition.RootElement, null)
            .Select(x => new { x.Code, x.Label, x.Requested, x.BinderLimit }).ToArray();
        var current = owned.Quote.CurrentUnderwritingCycleId == cycle.Id && owned.Quote.CurrentRevisionId == cycle.QuoteRevisionId && cycle.State == "rated" && owned.Quote.State is not ("draft" or "bound" or "withdrawn");
        var canWrite = false;
        if (current && owned.Scope.Actor.HasCapability("underwriting-escalate"))
        {
            try
            {
                // A read must not upgrade the held agency/quote locks after a
                // worker has acquired its update lock in the same scope.
                var now = time.GetUtcNow();
                var eligible = await QuoteRatingEligibility.ResolveAsync(db, owned, cycle.ProductVersionId, cycle.AgencyTermsVersionId, input.Input.Term, now, token);
                var grants = await QuoteUnderwritingScope.GrantsAsync(db, owned, cycle.ProductVersionId, eligible.BinderVersion, eligible.Capture.Product.Code, input.Input.Term, now, token);
                canWrite = owned.Scope.Agency.State == "active" && grants.Count > 0 && rating.Outcome == "rated" && rating.ExpiresAt > now &&
                    eligible.RatingVersion.Id == cycle.RatingRuleVersionId && eligible.BinderVersion.Id == cycle.BinderVersionId && eligible.AuthorityVersion.Id == cycle.AuthorityVersionId &&
                    await db.Set<CapacityProvider>().AnyAsync(x => x.Id == row.ProviderId && x.State == "active", token);
            }
            catch (QuoteOperationException) { canWrite = false; }
        }
        var result = new Dictionary<string, object> {
            ["id"] = row.Id, ["quoteId"] = row.QuoteId, ["cycleId"] = row.CycleId, ["revisionId"] = cycle.QuoteRevisionId,
            ["pricingInputHash"] = Convert.ToHexStringLower(cycle.PricingInputHash), ["referralId"] = row.ReferralId, ["providerId"] = row.ProviderId, ["binderVersionId"] = row.BinderVersionId,
            ["etag"] = UnderwritingDecisionContext.Etag(row.RowVersion), ["quoteEtag"] = UnderwritingDecisionContext.Etag(owned.Quote.RowVersion),
            ["state"] = row.State, ["current"] = current, ["reason"] = row.Reason, ["raisedAt"] = row.CreatedAt,
            ["raisedByLabel"] = await db.Set<StaffUser>().Where(x => x.Id == row.RaisedBy).Select(x => x.DisplayName).SingleAsync(token),
            ["providerLabel"] = await db.Set<CapacityProvider>().Where(x => x.Id == row.ProviderId).Select(x => x.Name).SingleAsync(token),
            ["ruleCode"] = referral.RuleCode, ["dimension"] = referral.Dimension,
            ["binderContext"] = binderContext,
            ["capabilities"] = new { canSend = canWrite && row.State is not ("queued" or "superseded") && referral.State is not ("superseded" or "declined"),
                canRecordResponse = canWrite && row.CurrentSubmissionId is not null && owned.Scope.Actor.HasCapability("underwriting-record-capacity"),
                canRevise = current && owned.Scope.Actor.HasCapability("quote-revise") },
            ["blockers"] = current ? Array.Empty<object>() : new object[] { new { code = "underwriting-cycle-stale", message = "This request belongs to an earlier quote cycle." } }
        };
        if (referral.AssignedUserId is Guid assignee) result["assignedUserLabel"] = await db.Set<StaffUser>().Where(x => x.Id == assignee).Select(x => x.DisplayName).SingleAsync(token);
        if (row.CurrentSubmissionId is Guid submissionId)
        {
            var submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.Id == submissionId, token);
            result["currentSubmissionId"] = submission.Id; result["submissionHash"] = submission.ContextHash; result["submittedAt"] = submission.SubmittedAt;
            result["responseDueAt"] = submission.ResponseDueAt; result["jobId"] = submission.WorkId;
            var scenario = CapacitySeed.Parse(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x => x.Id == submission.ScenarioVersionId, token));
            result["serviceStandard"] = scenario?.ResponseDueWorkingDays is int days ? $"{days} working days (Monday–Friday)" : $"{scenario?.ResponseDueHours} elapsed hours (retained setting)";
            result["attemptHistory"] = await db.Set<AdapterAttempt>().AsNoTracking().Where(x => x.WorkId == submission.WorkId).OrderBy(x => x.AttemptNumber).Take(18)
                .Select(x => new { number = x.AttemptNumber, x.StartedAt, x.EndedAt, x.Outcome, x.ErrorCode }).ToArrayAsync(token);
        }
        else result["attemptHistory"] = Array.Empty<object>();
        if (row.CurrentResponseId is Guid responseId) result["currentResponseId"] = responseId;
        var scenarios = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith("capacity-escalation/") && x.EffectiveFrom <= time.GetUtcNow()).ToArrayAsync(token);
        result["scenarios"] = scenarios.GroupBy(x => x.Scope).Select(x => x.OrderByDescending(s => s.Version).First()).Where(x => CapacitySeed.Parse(x) is not null)
            .OrderBy(x => x.Scope, StringComparer.Ordinal).Select(x => new { id = x.Id, label = "Demo: " + CapacitySeed.Parse(x)!.Value.Scenario.Replace('-', ' '), version = x.Version }).ToArray();
        var messages = await db.Set<CapacityMessage>().AsNoTracking().Where(x => x.EscalationId == id).OrderByDescending(x => x.Sequence).Take(100).ToArrayAsync(token);
        var history = new List<object>(); foreach (var message in messages) history.Add(await Message(db, message, token)); result["messages"] = history;
        result["conflictCount"] = await (from quarantine in db.Set<AdapterQuarantine>().AsNoTracking()
                                        join message in db.Set<CapacityMessage>().AsNoTracking() on quarantine.InboxId equals message.InboxId
                                        where message.EscalationId == id select quarantine.Id).CountAsync(token);
        await tx.CommitAsync(token); return result;
    }
    public async Task<(object[] Items, bool More)> MessagesAsync(ActorContext actor, Guid id, string expectedVersion, int offset, int size, CancellationToken token = default)
    {
        if (size is < 1 or > 100 || offset < 0) throw new QuoteOperationException(400, "invalid-query");
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var row = await db.Set<CapacityEscalation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new QuoteOperationException(404, "capacity-escalation-not-found");
        await Authorize(db, actor, row.QuoteId, token);
        if (expectedVersion != await QuoteDiscovery.VersionAsync(db, token)) throw new QuoteOperationException(409, "underwriting-history-changed");
        var rows = await db.Set<CapacityMessage>().AsNoTracking().Where(x => x.EscalationId == id).OrderByDescending(x => x.Sequence).Skip(offset).Take(size + 1).ToArrayAsync(token);
        var items = new List<object>(); foreach (var message in rows.Take(size)) items.Add(await Message(db, message, token));
        await tx.CommitAsync(token); return (items.ToArray(), rows.Length > size);
    }
    private static async Task<OwnedQuoteScope> Authorize(BackOfficeDbContext db, ActorContext actor, Guid quoteId, CancellationToken token)
    {
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied"); return owned;
    }
    private static async Task<object> Message(BackOfficeDbContext db, CapacityMessage row, CancellationToken token)
    {
        var hash = await db.Set<CapacitySubmission>().Where(x => x.Id == row.SubmissionId).Select(x => x.ContextHash).SingleAsync(token);
        var result = new Dictionary<string, object> { ["id"] = row.Id, ["escalationId"] = row.EscalationId, ["submissionId"] = row.SubmissionId, ["submissionHash"] = hash,
            ["direction"] = row.Direction, ["provenance"] = row.Provenance, ["body"] = row.Body, ["recordedAt"] = row.RecordedAt,
            ["applicationState"] = row.ApplicationState, ["recordedByLabel"] = await db.Set<StaffUser>().Where(x => x.Id == row.RecordedBy).Select(x => x.DisplayName).SingleAsync(token) };
        if (row.Outcome is not null) result["outcome"] = row.Outcome;
        if (row.ProviderUnderwriter is not null) result["providerUnderwriter"] = row.ProviderUnderwriter;
        if (row.ProviderReference is not null) result["providerReference"] = row.ProviderReference;
        if (row.ReceivedAt is not null) result["receivedAt"] = row.ReceivedAt;
        if (row.EvidenceAssociationId is not null) result["evidenceAssociationId"] = row.EvidenceAssociationId;
        if (row.ProviderEventId is not null) result["providerEventId"] = row.ProviderEventId;
        if (row.DecisionId is not null) result["decisionId"] = row.DecisionId;
        if (row.Direction == "inbound")
        {
            using var definition = JsonDocument.Parse(row.DefinitionJson);
            foreach (var field in new[] { "validFrom", "validTo", "authorisedLimits", "conditions" })
                if (definition.RootElement.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null) result[field] = value.Clone();
        }
        return result;
    }
}
