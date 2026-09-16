using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed class QuoteReferralReadModel(IDbContextFactory<BackOfficeDbContext> factory)
{
    public async Task<Guid> QuoteForReferralAsync(ActorContext actor, Guid referralId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var quote = await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.Id == referralId).Select(x => (Guid?)x.QuoteId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "referral-not-found");
        await using var tx = await db.Database.BeginTransactionAsync(token); await Authorize(db, actor, quote, token); await tx.CommitAsync(token); return quote;
    }
    public async Task<string> VersionAsync(ActorContext actor, Guid quoteId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        await Authorize(db, actor, quoteId, token); var version = await QuoteDiscovery.VersionAsync(db, token); await tx.CommitAsync(token); return version;
    }
    public async Task<Dictionary<string, object>> ReferralAsync(ActorContext actor, Guid referralId, CancellationToken token = default)
    {
        var quoteId = await QuoteForReferralAsync(actor, referralId, token);
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        await Authorize(db, actor, quoteId, token);
        var row = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referralId && x.QuoteId == quoteId, token);
        var result = await Referral(db, row, false, token); await tx.CommitAsync(token); return result;
    }

    public async Task<(object[] Items, bool More)> PageAsync(ActorContext actor, Guid quoteId, string kind, Guid? childId,
        string expectedVersion, int offset, int size, CancellationToken token = default)
    {
        if (size is < 1 or > 100 || offset < 0 || kind is not ("referrals" or "decisions" or "evidence" or "events")) throw new QuoteOperationException(400, "invalid-query");
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        await Authorize(db, actor, quoteId, token);
        if (expectedVersion != await QuoteDiscovery.VersionAsync(db, token)) throw new QuoteOperationException(409, "underwriting-history-changed");
        var items = new List<object>(); bool more;
        switch (kind)
        {
            case "referrals":
                var referrals = await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.QuoteId == quoteId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(size + 1).ToArrayAsync(token);
                more = referrals.Length > size;
                foreach (var referral in referrals.Take(size)) items.Add(await Referral(db, referral, true, token));
                break;
            case "decisions":
                if (!await db.Set<QuoteReferral>().AnyAsync(x => x.Id == childId && x.QuoteId == quoteId, token)) throw new QuoteOperationException(404, "referral-not-found");
                var decisions = await db.Set<QuoteReferralDecision>().AsNoTracking().Where(x => x.ReferralId == childId && x.QuoteId == quoteId).OrderByDescending(x => x.Sequence).Skip(offset).Take(size + 1).ToArrayAsync(token);
                more = decisions.Length > size;
                foreach (var decision in decisions.Take(size)) items.Add(await Decision(db, decision, token));
                break;
            case "evidence":
                var evidence = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().Where(x => x.QuoteId == quoteId).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(offset).Take(size + 1).ToArrayAsync(token);
                more = evidence.Length > size;
                foreach (var association in evidence.Take(size)) items.Add(await Evidence(db, association, token));
                break;
            default:
                if (!await db.Set<UnderwritingEvidenceAssociation>().AnyAsync(x => x.Id == childId && x.QuoteId == quoteId, token)) throw new QuoteOperationException(404, "underwriting-evidence-not-found");
                var events = await db.Set<UnderwritingEvidenceEvent>().AsNoTracking().Where(x => x.AssociationId == childId && x.QuoteId == quoteId).OrderByDescending(x => x.Sequence).Skip(offset).Take(size + 1).ToArrayAsync(token);
                more = events.Length > size;
                foreach (var entry in events.Take(size))
                {
                    var result = new Dictionary<string, object> { ["id"] = entry.Id, ["associationId"] = entry.AssociationId, ["cycleId"] = entry.CycleId,
                        ["sequence"] = entry.Sequence, ["kind"] = entry.Kind, ["reason"] = entry.Reason, ["actorId"] = entry.ActorId,
                        ["actorLabel"] = await ActorLabel(db, entry.ActorId, token), ["recordedAt"] = entry.RecordedAt, ["inputFingerprint"] = entry.InputFingerprint };
                    if (entry.Outcome is not null) result["outcome"] = entry.Outcome; items.Add(result);
                }
                break;
        }
        await tx.CommitAsync(token); return (items.ToArray(), more);
    }

    private static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid quoteId, CancellationToken token)
    { var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token); if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied"); }
    private static Task<string> ActorLabel(BackOfficeDbContext db, Guid id, CancellationToken token) => db.Set<StaffUser>().AsNoTracking().Where(x => x.Id == id).Select(x => x.DisplayName).SingleAsync(token);
    private static async Task<Dictionary<string, object>> Context(BackOfficeDbContext db, Guid id, Guid quoteId, Guid cycleId, CancellationToken token)
    {
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == cycleId && x.QuoteId == quoteId, token);
        return new() { ["id"] = id, ["quoteId"] = quoteId, ["cycleId"] = cycleId, ["revisionId"] = cycle.QuoteRevisionId, ["pricingInputHash"] = Convert.ToHexStringLower(cycle.PricingInputHash) };
    }
    private static async Task<object> Decision(BackOfficeDbContext db, QuoteReferralDecision row, CancellationToken token)
    {
        var result = await Context(db, row.Id, row.QuoteId, row.CycleId, token); using var conditions = JsonDocument.Parse(row.ConditionsJson);
        result["referralId"] = row.ReferralId; result["outcome"] = row.Outcome; result["reason"] = row.Reason;
        result["actorId"] = row.ActorId; result["actorLabel"] = await ActorLabel(db, row.ActorId, token); result["authorityVersionId"] = row.AuthorityVersionId;
        result["recordedAt"] = row.DecidedAt; result["conditions"] = conditions.RootElement.Clone(); if (row.Question is not null) result["question"] = row.Question; return result;
    }
    private static async Task<Dictionary<string, object>> Referral(BackOfficeDbContext db, QuoteReferral row, bool latestOnly, CancellationToken token)
    {
        var result = await Context(db, row.Id, row.QuoteId, row.CycleId, token);
        result["etag"] = UnderwritingDecisionContext.Etag(row.RowVersion); result["ruleCode"] = row.RuleCode; result["dimension"] = row.Dimension; result["reason"] = row.Reason; result["state"] = row.State;
        if (row.RiskItemId is Guid target) result["targetId"] = target; if (row.AssignedUserId is Guid assigned) result["assignedUserId"] = assigned;
        var decisions = await db.Set<QuoteReferralDecision>().AsNoTracking().Where(x => x.ReferralId == row.Id).OrderByDescending(x => x.Sequence).Take(latestOnly ? 1 : 100).ToArrayAsync(token);
        var history = new List<object>(); foreach (var decision in decisions) history.Add(await Decision(db, decision, token)); result["decisions"] = history;
        var conditions = new List<object>();
        foreach (var condition in await db.Set<QuoteCondition>().AsNoTracking().Where(x => x.ReferralId == row.Id && x.DecisionId == row.LatestDecisionId).OrderBy(x => x.Sequence).Take(100).ToArrayAsync(token))
        {
            using var definition = JsonDocument.Parse(condition.DefinitionJson);
            var item = new Dictionary<string, object> { ["id"] = condition.Id, ["decisionId"] = condition.DecisionId, ["cycleId"] = condition.CycleId,
                ["etag"] = UnderwritingDecisionContext.Etag(condition.RowVersion), ["definition"] = definition.RootElement.Clone(),
                ["state"] = row.State == "superseded" ? "superseded" : await UnderwritingEvidenceService.Resolved(db, condition, token) ? "resolved" : "outstanding" };
            if (condition.LatestResolutionId is Guid resolution)
            {
                item["latestResolutionId"] = resolution;
                item["evidenceAssociationId"] = await db.Set<QuoteConditionResolution>().Where(x => x.Id == resolution && x.ConditionId == condition.Id).Select(x => x.EvidenceAssociationId).SingleAsync(token);
            }
            conditions.Add(item);
        }
        result["conditions"] = conditions; return result;
    }
    private static async Task<object> Evidence(BackOfficeDbContext db, UnderwritingEvidenceAssociation row, CancellationToken token)
    {
        var result = await Context(db, row.Id, row.QuoteId, row.CycleId, token);
        var file = await db.Set<QuoteEvidenceFile>().AsNoTracking().SingleAsync(x => x.Id == row.FileId && x.QuoteId == row.QuoteId, token);
        result["fileId"] = file.Id; result["fileName"] = file.FileName; result["requirementCode"] = row.RequirementCode; result["inputFingerprint"] = row.InputFingerprint;
        result["etag"] = UnderwritingDecisionContext.Etag(row.RowVersion); result["screeningState"] = file.ScreeningState; result["withdrawn"] = row.WithdrawnEventId is not null;
        result["reviewState"] = row.LatestReviewId is Guid review ? (await db.Set<UnderwritingEvidenceEvent>().Where(x => x.Id == review).Select(x => x.Outcome).SingleAsync(token))! : "unreviewed";
        if (row.LatestReviewId is Guid latest) result["latestReviewId"] = latest;
        if (row.RiskItemId is Guid target) result["riskItemId"] = target; if (row.ConditionId is Guid condition) result["conditionId"] = condition; if (row.TermsVersionId is Guid terms) result["termsVersionId"] = terms;
        return result;
    }
}
