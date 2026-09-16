using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record UnderwritingProofRequirement(string Code, string Label, string Path, Guid? RiskItemId,
    Guid? ConditionId, Guid? TermsVersionId, string InputFingerprint, bool Satisfied);

public sealed partial class UnderwritingEvidenceService
{
    public async Task<IReadOnlyList<UnderwritingProofRequirement>> RequirementsAsync(ActorContext actor, Guid quoteId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied");
        if (owned.Quote.CurrentUnderwritingCycleId is not Guid id) { await tx.CommitAsync(token); return []; }
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == id && x.QuoteId == quoteId, token);
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId && x.QuoteId == quoteId, token);
        var input = JsonSerializer.Deserialize<StoredRatingInput>(cycle.InputJson, QuoteRatingService.Json)!;
        var result = await Requirements(db, cycle, revision, input, token); await tx.CommitAsync(token); return result;
    }

    internal static async Task<IReadOnlyList<UnderwritingProofRequirement>> Requirements(BackOfficeDbContext db,
        UnderwritingCycle cycle, QuoteRevision revision, StoredRatingInput input, CancellationToken token)
    {
        using var proposal = JsonDocument.Parse(revision.ProposalJson); var pins = QuoteService.Pins(revision);
        var result = new List<UnderwritingProofRequirement>();
        void Add(string code, string label, string path, Guid? riskId = null, QuoteCondition? condition = null, Guid? termsId = null)
        {
            var hash = QuoteCanonicalJson.Create(JsonSerializer.Serialize(new { format = "underwriting-proof-1", cycleId = cycle.Id,
                revisionId = revision.Id, revisionHash = Convert.ToHexStringLower(revision.ContentHash), code, riskId, conditionId = condition?.Id,
                definition = condition?.DefinitionJson, termsId }), pins).ContentHash;
            result.Add(new(code, label, path, riskId, condition?.Id, termsId, hash, false));
        }
        foreach (var requirement in QuoteEvidenceRequirements.ForProposal(proposal.RootElement)) Add(requirement.Code, requirement.Label, requirement.Path, requirement.RiskItemId);
        if (input.Input.TradingYears < 5) Add("trading-history", "Business trading history and experience", "/risk/business");
        if (proposal.RootElement.TryGetProperty("cover", out var cover) && cover.TryGetProperty("requestedSections", out var sections))
            foreach (var section in sections.EnumerateArray().Where(x => x.GetProperty("code").GetString() == "premises" && x.GetProperty("selected").GetBoolean()))
                foreach (var premises in section.GetProperty("premisesIds").EnumerateArray()) Add("premises-security", "Security evidence for the insured premises", "/risk/premises", premises.GetGuid());
        foreach (var condition in await ActiveConditions(db, cycle.Id, token))
        {
            using var json = JsonDocument.Parse(condition.DefinitionJson); var definition = ReferralRules.Condition(json.RootElement, proposal.RootElement);
            if (ReferralRules.CanResolveWithEvidence(definition)) Add(definition.RequirementCode!, definition.Kind == "warranty" ? "Acknowledgement: " + definition.Code.Replace('-', ' ') : definition.Code.Replace('-', ' '),
                "/underwriting/conditions/" + condition.Id, definition.TargetIds.Count == 1 ? definition.TargetIds[0] : null, condition, definition.TermsVersionId);
        }
        var proofs = await (from a in db.Set<UnderwritingEvidenceAssociation>().AsNoTracking()
                            join f in db.Set<QuoteEvidenceFile>().AsNoTracking() on a.FileId equals f.Id
                            join r in db.Set<UnderwritingEvidenceEvent>().AsNoTracking() on a.LatestReviewId equals r.Id into reviews
                            from review in reviews.DefaultIfEmpty()
                            where a.CycleId == cycle.Id && a.QuoteId == cycle.QuoteId
                            select new { a.RequirementCode, a.RiskItemId, a.ConditionId, a.TermsVersionId, a.InputFingerprint, a.WithdrawnEventId,
                                f.ScreeningState, ReviewState = review == null ? "unreviewed" : review.Outcome }).ToArrayAsync(token);
        return result.Select(x => x with { Satisfied = proofs.Any(a => a.RequirementCode == x.Code && a.RiskItemId == x.RiskItemId && a.TermsVersionId == x.TermsVersionId &&
            (a.ConditionId == x.ConditionId || x.ConditionId is null && result.Any(p => p.ConditionId == a.ConditionId && p.Code == x.Code && p.RiskItemId == x.RiskItemId && p.InputFingerprint == a.InputFingerprint)) &&
            ReferralRules.EvidenceSatisfied(a.ScreeningState, a.ReviewState ?? "unreviewed", a.WithdrawnEventId is not null,
                a.ConditionId == x.ConditionId ? x.InputFingerprint : result.Single(p => p.ConditionId == a.ConditionId && p.Code == x.Code && p.RiskItemId == x.RiskItemId).InputFingerprint, a.InputFingerprint)) }).ToArray();
    }

    internal static Task<QuoteCondition[]> ActiveConditions(BackOfficeDbContext db, Guid cycleId, CancellationToken token) =>
        (from c in db.Set<QuoteCondition>().AsNoTracking() join r in db.Set<QuoteReferral>().AsNoTracking() on c.ReferralId equals r.Id
         where c.CycleId == cycleId && r.CycleId == cycleId && r.LatestDecisionId == c.DecisionId && r.State != "superseded"
         orderby c.CreatedAt, c.Sequence select c).ToArrayAsync(token);

    internal static async Task<bool> Resolved(BackOfficeDbContext db, QuoteCondition condition, CancellationToken token)
    {
        if (condition.Kind == "risk-change" || condition.LatestResolutionId is null) return false;
        return await (from r in db.Set<QuoteConditionResolution>().AsNoTracking()
                      join a in db.Set<UnderwritingEvidenceAssociation>().AsNoTracking() on r.EvidenceAssociationId equals a.Id
                      join e in db.Set<UnderwritingEvidenceEvent>().AsNoTracking() on r.EvidenceReviewId equals e.Id
                      where r.Id == condition.LatestResolutionId && r.ConditionId == condition.Id && r.CycleId == condition.CycleId && r.Outcome == "satisfied" &&
                          a.CycleId == condition.CycleId && a.WithdrawnEventId == null && a.LatestReviewId == e.Id && e.Outcome == "accepted"
                      select r.Id).AnyAsync(token);
    }

    internal static async Task<string> Assurance(BackOfficeDbContext db, UnderwritingCycle cycle, QuoteRevision revision, CancellationToken token)
    {
        var items = new List<UnderwritingAssuranceItem>(); var pins = QuoteService.Pins(revision);
        var associations = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().Where(x => x.CycleId == cycle.Id).OrderBy(x => x.Id).ToArrayAsync(token);
        foreach (var a in associations) items.Add(new(a.Id, "evidence", a.WithdrawnEventId ?? a.LatestReviewId, a.WithdrawnEventId is not null ? "withdrawn" : a.LatestReviewId is null ? "unreviewed" : "reviewed", a.InputFingerprint));
        foreach (var r in await db.Set<QuoteReferral>().AsNoTracking().Where(x => x.CycleId == cycle.Id).ToArrayAsync(token))
            items.Add(new(r.Id, "referral", r.LatestDecisionId, r.State, QuoteCanonicalJson.Create(r.RequiredAuthorityJson, pins).ContentHash));
        foreach (var c in await ActiveConditions(db, cycle.Id, token))
            items.Add(new(c.Id, "condition", c.LatestResolutionId, await Resolved(db, c, token) ? "resolved" : "outstanding", QuoteCanonicalJson.Create(c.DefinitionJson, pins).ContentHash));
        return UnderwritingHashes.Assurance(cycle.Id, pins, items);
    }
}
