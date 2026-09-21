using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal sealed record WorkflowTaskSource(OperationalParent Parent, Guid ActorId, DateOnly DueOn, bool Eligible, bool Resolved, string SnapshotJson);

// Resolve the typed authoritative record, never an arbitrary payload parent ID.
// The materializer rereads this graph inside its held serializable transaction.
internal static partial class WorkflowTaskSources
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static DateOnly LocalDate(DateTimeOffset value) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
    internal static async Task<WorkflowTaskSource> Read(BackOfficeDbContext db, WorkflowTaskDefinition rule, string kind, Guid id, DateTimeOffset now, CancellationToken token)
    {
        WorkflowTaskRules.DemandSource(rule, kind);
        WorkflowTaskSource Source(OperationalParent parent, Guid? actor, DateOnly due, bool active, object details, bool dueWindow = true)
        {
            if (actor is null || actor == Guid.Empty) throw new OperationalAccessException(409, "workflow-source-actor-unavailable");
            return new(parent, actor.Value, due, active && dueWindow, !active,
                JsonSerializer.Serialize(new { sourceKind = kind, sourceEventId = id, parent, actorId = actor, details }, Json));
        }
        switch (kind)
        {
            case "quote-referral":
            case "quote-query":
            {
                var decision = kind == "quote-query" ? await db.Set<QuoteReferralDecision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Outcome == "query", token) ?? throw Missing() : null;
                var referralId = decision?.ReferralId ?? id;
                var row = await db.Set<QuoteReferral>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == referralId, token) ?? throw Missing();
                var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == row.CycleId && x.QuoteId == row.QuoteId, token);
                var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == row.QuoteId, token);
                var current = quote.CurrentUnderwritingCycleId == cycle.Id && cycle.SupersededAt == null && cycle.CurrentRatingId == row.RatingId;
                var active = current && (decision is null ? row.State is "open" or "queried" : row.State == "queried" && row.LatestDecisionId == decision.Id);
                return Source(new("quote", quote.Id), decision?.ActorId ?? cycle.RequestedBy, LocalDate(decision?.DecidedAt ?? row.CreatedAt).AddDays(rule.DueDays), active,
                    new { referralId = row.Id, row.CycleId, row.RatingId, row.RuleCode, row.Reason, row.State, row.LatestDecisionId, question = decision?.Question, quote.Reference, current });
            }
            case "servicing-referral":
            case "servicing-query":
            {
                var decision = kind == "servicing-query" ? await db.Set<ServicingReferralDecision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Outcome == "query", token) ?? throw Missing() : null;
                var referralId = decision?.ReferralId ?? id;
                var row = await db.Set<ServicingReferral>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == referralId, token) ?? throw Missing();
                var cycle = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x => x.Id == row.CycleId && x.DraftId == row.DraftId, token);
                var draft = await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x => x.Id == row.DraftId, token);
                var current = draft.CurrentCycleId == cycle.Id && draft.CurrentRevisionId == row.RevisionId && cycle.SupersededAt == null && cycle.CurrentRatingId == row.RatingId && draft.State != "issued";
                var active = current && (decision is null ? row.State is "open" or "queried" : row.State == "queried" && row.LatestDecisionId == decision.Id);
                return Source(new("servicing-draft", draft.Id), decision?.ActorId ?? cycle.RequestedBy, LocalDate(decision?.DecidedAt ?? row.CreatedAt).AddDays(rule.DueDays), active,
                    new { referralId = row.Id, row.CycleId, row.RatingId, row.RevisionId, row.RuleCode, row.Reason, row.State, row.LatestDecisionId, question = decision?.Question, current });
            }
            case "match-information-request":
            {
                var request = await db.Set<MatchInformationRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing();
                var review = await db.Set<MatchReview>().AsNoTracking().SingleAsync(x => x.Id == request.MatchId, token);
                var submission = await db.Set<MatchSubmission>().AsNoTracking().SingleAsync(x => x.Id == review.SubmissionId, token);
                OperationalParent parent;
                if (submission.QuoteId is Guid quoteId)
                {
                    if (!await db.Set<Quote>().AnyAsync(x => x.Id == quoteId && x.AgencyId == submission.AgencyId, token)) throw Missing();
                    parent = new("quote", quoteId);
                }
                else if (submission.LinkedRelationshipId is Guid relationshipId)
                {
                    if (!await db.Set<ClientAgencyRelationship>().AnyAsync(x => x.Id == relationshipId && x.ClientId == submission.LinkedClientId && x.AgencyId == submission.AgencyId, token)) throw Missing();
                    parent = new("relationship", relationshipId);
                }
                else throw new OperationalAccessException(409, "workflow-source-parent-unavailable");
                var decisions = await db.Set<MatchDecision>().AsNoTracking().Where(x => x.MatchId == review.Id)
                    .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.CreatedAt).Take(2).ToArrayAsync(token);
                // Legacy match history has no sequence/current-decision pointer.
                // Equal timestamps must not turn random SQL ordering into a new
                // active obligation or a false resolution of an earlier one.
                if (decisions.Length == 2 && decisions[0].OccurredAt == decisions[1].OccurredAt && decisions[0].CreatedAt == decisions[1].CreatedAt)
                    throw new OperationalAccessException(409, "workflow-match-history-ambiguous");
                var latest = decisions.FirstOrDefault()?.InformationRequestId;
                return Source(parent, request.ActorId, LocalDate(request.RecordedAt).AddDays(rule.DueDays), review.State == "queried" && latest == request.Id,
                    new { request.MatchId, request.Description, request.RecordedAt, review.State, latestInformationRequestId = latest });
            }
            case "policy-term":
            {
                var term = await db.Set<PolicyTerm>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing();
                var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == term.PolicyId, token);
                var cancelled = await db.Set<PolicyTransaction>().AnyAsync(x => x.TermId == id && x.Kind == "cancellation" && x.EffectiveAt <= now, token);
                var lapsed = await db.Set<RenewalLapseEvent>().AnyAsync(x => x.TermId == id, token);
                var renewed = await db.Set<PolicyTerm>().AnyAsync(x => x.PolicyId == policy.Id && x.Number > term.Number, token);
                var due = LocalDate(term.EndsAt).AddDays(-rule.LeadDays);
                return Source(new("policy", policy.Id), term.CreatedBy, due.AddDays(rule.DueDays), !cancelled && !lapsed && !renewed && policy.CurrentTermId == term.Id,
                    new { policy.Reference, term.Number, term.EndsAt, cancelled, lapsed, renewed, policy.CurrentTermId }, LocalDate(now) >= due);
            }
            case "agency-follow-up":
            {
                var row = await db.Set<AgencyFollowUp>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing();
                var agency = await db.Set<Agency>().AsNoTracking().SingleAsync(x => x.Id == row.AgencyId, token);
                var superseded = false;
                if (row.EvidenceId is Guid evidenceId)
                {
                    var evidence = await db.Set<AgencyEvidence>().AsNoTracking().SingleAsync(x => x.Id == evidenceId && x.AgencyId == row.AgencyId, token);
                    superseded = await db.Set<AgencyEvidence>().AnyAsync(x => x.AgencyId == row.AgencyId && x.Kind == evidence.Kind && x.Ordinal > evidence.Ordinal && x.State == "verified", token);
                }
                return Source(new("agency", agency.Id), row.CreatedBy, row.DueOn.AddDays(rule.DueDays), agency.State == "active" && !superseded,
                    new { row.Purpose, row.DueOn, row.EvidenceId, row.ActivationRequestId, agency.Reference, agency.State, superseded }, row.DueOn <= LocalDate(now).AddDays(rule.LeadDays));
            }
            case "job-exception": return await Job(db, rule, id, token);
            default: throw Missing();
        }
    }
    private static OperationalAccessException Missing() => new(404, "workflow-source-not-found");
}
