using System.Text.Json;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

internal static class CapacityAuthority
{
    // A current stored grant is still mandatory. A carrier exception is a local
    // predicate for its exact exposure; neither the grant nor binder is rewritten.
    internal static async Task<bool> Allows(BackOfficeDbContext db, UnderwritingDecisionContext held, JsonElement authority,
        IReadOnlyList<ReferralCondition> conditions, DateTimeOffset now, CancellationToken token)
    {
        if (held.Input.IsCommercial)
        {
            using var commercialProposal = JsonDocument.Parse(held.Revision.ProposalJson);
            var premium = held.Rating?.AnnualPremium ?? throw new BackOffice.Infrastructure.Quotes.QuoteOperationException(409, "quote-rating-required");
            // Internal authority cannot grant a carrier exception. CC carrier
            // response applicability is implemented separately in 08-07.
            return CommercialReferralRules.AssessAuthority(authority, commercialProposal.RootElement, premium).Count == 0 &&
                CommercialReferralRules.AssessAuthority(held.Eligible.Binder, commercialProposal.RootElement, premium).Count == 0;
        }
        var blockers = ReferralRules.AuthorityBlockers(authority, held.Eligible.Binder, held.Risk, conditions);
        if (blockers.Count == 0) return true;
        var responses = await (from e in db.Set<CapacityEscalation>().AsNoTracking()
                               join s in db.Set<CapacitySubmission>().AsNoTracking() on e.CurrentSubmissionId equals s.Id
                               join m in db.Set<CapacityMessage>().AsNoTracking() on e.CurrentResponseId equals m.Id
                               join r in db.Set<QuoteReferral>().AsNoTracking() on e.ReferralId equals r.Id
                               join p in db.Set<CapacityProvider>().AsNoTracking() on e.ProviderId equals p.Id
                               where e.CycleId == held.Cycle.Id && e.QuoteId == held.Cycle.QuoteId && e.BinderVersionId == held.Cycle.BinderVersionId &&
                                   p.State == "active" && (e.State == "approved" || e.State == "conditional") && m.SubmissionId == s.Id && m.ApplicationState == "applied" &&
                                   (m.Outcome == "approve" || m.Outcome == "approve-with-conditions")
                               select new { Submission = s, Message = m, Referral = r }).ToArrayAsync(token);
        var eligible = new List<(CapacitySubmission Submission, QuoteReferral Referral, CapacityDecision Decision)>();
        using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
        foreach (var item in responses)
        {
            if (!await SubmissionEvidenceCurrent(db, item.Submission.Id, token)) continue;
            if (item.Message.Provenance == "supplied-response" && !await ProofCurrent(db, item.Message, token)) continue;
            using var json = JsonDocument.Parse(item.Message.DefinitionJson); var value = json.RootElement;
            var decision = new CapacityDecision(value.GetProperty("quoteId").GetGuid(), value.GetProperty("cycleId").GetGuid(), value.GetProperty("submissionId").GetGuid(),
                value.GetProperty("submissionHash").GetString()!, value.GetProperty("outcome").GetString()!, value.GetProperty("validFrom").GetDateTimeOffset(), value.GetProperty("validTo").GetDateTimeOffset(),
                value.GetProperty("authorisedLimits").EnumerateArray().Select(CapacityRules.Extension).ToArray(),
                value.GetProperty("conditions").EnumerateArray().Select(x => ReferralRules.Condition(x, proposal.RootElement)).ToArray());
            eligible.Add((item.Submission, item.Referral, decision));
        }
        foreach (var blocker in blockers)
        {
            var dimension = CapacityRules.Dimension(blocker.RuleCode, blocker.Dimension);
            var minimumAge = dimension == "driver-age" && blocker.TargetId is Guid driverId ? held.Risk.Drivers.Single(x => x.Id == driverId).Age : held.Risk.AnyDriverMinimumAge;
            var maximumAge = blocker.TargetId is not null ? minimumAge : held.Risk.AnyDriverMaximumAge;
            if (!eligible.Any(x => (dimension != "driver-age" || x.Referral.RiskItemId == blocker.TargetId) &&
                CapacityRules.Applies(x.Decision, new(held.Cycle.QuoteId, held.Cycle.Id, x.Submission.Id, x.Submission.ContextHash,
                    held.Cycle.StartsAt, held.Cycle.EndsAt, dimension, blocker.RequestedAmount, minimumAge, maximumAge,
                    dimension == "trade-restriction" ? blocker.RuleCode : null), now))) return false;
        }
        return true;
    }
    internal static async Task<bool> HasBlockingRequest(BackOfficeDbContext db, UnderwritingDecisionContext held, Guid referralId, DateTimeOffset now, CancellationToken token)
    {
        var escalation = await db.Set<CapacityEscalation>().AsNoTracking().SingleOrDefaultAsync(x => x.ReferralId == referralId && x.CycleId == held.Cycle.Id, token);
        if (escalation is null) return false;
        if (escalation.CurrentSubmissionId is null || escalation.CurrentResponseId is null || escalation.State is not ("approved" or "conditional")) return true;
        var message = await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.Id == escalation.CurrentResponseId, token);
        if (!await SubmissionEvidenceCurrent(db, escalation.CurrentSubmissionId.Value, token)) return true;
        if (message.SubmissionId != escalation.CurrentSubmissionId || message.ApplicationState != "applied" || message.Outcome is not ("approve" or "approve-with-conditions")) return true;
        if (message.Provenance == "supplied-response" && !await ProofCurrent(db, message, token)) return true;
        if (!await db.Set<CapacityProvider>().AnyAsync(x => x.Id == escalation.ProviderId && x.State == "active", token)) return true;
        using var definition = JsonDocument.Parse(message.DefinitionJson); var value = definition.RootElement;
        return value.GetProperty("validFrom").GetDateTimeOffset() > now || value.GetProperty("validTo").GetDateTimeOffset() <= now ||
            value.GetProperty("validFrom").GetDateTimeOffset() > held.Cycle.StartsAt || value.GetProperty("validTo").GetDateTimeOffset() < held.Cycle.EndsAt;
    }
    internal static Task<bool> ProofCurrent(BackOfficeDbContext db, CapacityMessage message, CancellationToken token) =>
        (from a in db.Set<UnderwritingEvidenceAssociation>().AsNoTracking()
         join e in db.Set<UnderwritingEvidenceEvent>().AsNoTracking() on a.LatestReviewId equals e.Id
         join f in db.Set<QuoteEvidenceFile>().AsNoTracking() on a.FileId equals f.Id
         where a.Id == message.EvidenceAssociationId && a.CapacitySubmissionId == message.SubmissionId && a.CycleId == message.CycleId &&
             a.WithdrawnEventId == null && e.Id == message.EvidenceReviewId && e.Outcome == "accepted" && f.ScreeningState == "accepted"
         select a.Id).AnyAsync(token);

    internal static async Task<bool> SubmissionEvidenceCurrent(BackOfficeDbContext db, Guid submissionId, CancellationToken token) =>
        !await (from selected in db.Set<CapacitySubmissionEvidence>().AsNoTracking()
                join proof in db.Set<UnderwritingEvidenceAssociation>().AsNoTracking() on selected.EvidenceAssociationId equals proof.Id
                join file in db.Set<QuoteEvidenceFile>().AsNoTracking() on proof.FileId equals file.Id
                where selected.SubmissionId == submissionId && (proof.WithdrawnEventId != null || file.ScreeningState != "accepted")
                select selected.Id).AnyAsync(token);

    internal static Task<Guid[]> CarrierDecisionIds(BackOfficeDbContext db, Guid cycleId, CancellationToken token) =>
        (from e in db.Set<CapacityEscalation>().AsNoTracking() join m in db.Set<CapacityMessage>().AsNoTracking() on e.CurrentResponseId equals m.Id
         where e.CycleId == cycleId && (e.State == "approved" || e.State == "conditional") && m.SubmissionId == e.CurrentSubmissionId && m.DecisionId != null
         select m.DecisionId!.Value).ToArrayAsync(token);
}
