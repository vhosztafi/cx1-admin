using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class QuoteReferralService
{
    public Task<CommandOutcome> ResolveAsync(ActorContext actor, Guid quoteId, Guid referralId, Guid cycleId, Guid conditionId,
        byte[] quoteVersion, byte[] conditionVersion, Guid evidenceAssociationId, string outcome, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        reason = QuoteRatingService.Reason(reason);
        if (outcome is not ("satisfied" or "rejected") || evidenceAssociationId == Guid.Empty) throw new QuoteOperationException(422, "condition-resolution-invalid");
        UnderwritingDecisionContext? held = null; QuoteCondition? condition = null; QuoteReferral? referral = null; EffectiveUnderwritingGrant? grant = null;
        ReferralCondition? definition = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/referrals/{referralId:D}/conditions/{conditionId:D}/resolutions", key, correlationId),
            new { quoteId, referralId, cycleId, conditionId, quoteVersion = Convert.ToBase64String(quoteVersion), conditionVersion = Convert.ToBase64String(conditionVersion), evidenceAssociationId, outcome, reason }, "underwriting.condition-resolved",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-decide-within-authority", time.GetUtcNow(), true, ct);
                condition = await db.Set<QuoteCondition>().FromSqlInterpolated($"SELECT * FROM QuoteCondition WITH(UPDLOCK,HOLDLOCK) WHERE Id={conditionId} AND ReferralId={referralId} AND CycleId={cycleId} AND QuoteId={quoteId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "condition-not-found");
                referral = await db.Set<QuoteReferral>().FromSqlInterpolated($"SELECT * FROM QuoteReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={referralId} AND CycleId={cycleId} AND QuoteId={quoteId}").SingleAsync(ct);
                using var proposal = JsonDocument.Parse(held.Revision.ProposalJson); definition = Parse(condition.DefinitionJson, proposal.RootElement);
                var decision = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(x => x.Id == condition.DecisionId, ct);
                var active = (await UnderwritingEvidenceService.ActiveConditions(db, cycleId, ct)).Select(x => Parse(x.DefinitionJson, proposal.RootElement)).ToArray();
                foreach (var candidate in held.Grants)
                    if ((definition.RequirementCode != "trading-history" || candidate.Definition.GetProperty("limits").GetProperty("reviewTradingHistory").GetBoolean()) &&
                        (decision.Outcome == "query" || await CapacityAuthority.Allows(db, held, candidate.Definition, active, time.GetUtcNow(), ct))) { grant = candidate; break; }
                if (grant is null) throw new QuoteOperationException(403, "underwriting-dimension-authority-required");
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(quoteVersion, now, currentPrice: true);
                UnderwritingDecisionContext.CheckVersion(condition!.RowVersion, conditionVersion, "stale-condition");
                if (!(await UnderwritingEvidenceService.ActiveConditions(db, cycleId, ct)).Any(x => x.Id == condition.Id) || referral!.State is "superseded" or "declined") throw new QuoteOperationException(409, "condition-superseded");
                if (!ReferralRules.CanResolveWithEvidence(definition!)) throw new QuoteOperationException(409, "condition-requires-risk-revision");
                var evidence = await db.Set<UnderwritingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM UnderwritingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={evidenceAssociationId} AND QuoteId={quoteId} AND CycleId={cycleId}").AsNoTracking().SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "underwriting-evidence-not-found");
                var requirement = (await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, ct)).SingleOrDefault(x => x.ConditionId == conditionId);
                if (requirement is null || evidence.ConditionId != conditionId || evidence.RequirementCode != requirement.Code || evidence.RiskItemId != requirement.RiskItemId || evidence.TermsVersionId != requirement.TermsVersionId || evidence.InputFingerprint != requirement.InputFingerprint || evidence.WithdrawnEventId is not null || evidence.LatestReviewId is null)
                    throw new QuoteOperationException(409, "condition-proof-required");
                var review = await db.Set<UnderwritingEvidenceEvent>().AsNoTracking().SingleAsync(x => x.Id == evidence.LatestReviewId && x.AssociationId == evidence.Id && x.CycleId == cycleId, ct);
                if (review.Kind != "review" || outcome == "satisfied" && review.Outcome != "accepted") throw new QuoteOperationException(409, "condition-proof-review-required");
                if (!await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == evidence.FileId && x.QuoteId == quoteId && x.ScreeningState == "accepted", ct))
                    throw new QuoteOperationException(409, "evidence-screening-required");
                var row = new QuoteConditionResolution { ConditionId = conditionId, CycleId = cycleId, QuoteId = quoteId,
                    Sequence = checked((await db.Set<QuoteConditionResolution>().Where(x => x.ConditionId == conditionId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    EvidenceAssociationId = evidence.Id, EvidenceReviewId = review.Id, Outcome = outcome, ActorId = actor.UserId, AuthorityVersionId = grant!.Version.Id,
                    Reason = reason, RecordedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(row); await db.SaveChangesAsync(ct); condition.LatestResolutionId = row.Id; await db.SaveChangesAsync(ct);
                await RefreshState(db, held, now, ct); return await held.Receipt(db, row.Id, 200, "underwriting.condition-resolved", now, ct);
            }, token);
    }
}
