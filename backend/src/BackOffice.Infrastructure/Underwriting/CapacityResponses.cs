using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record CapacityResponseInput(Guid SubmissionId, string SubmissionHash, string Outcome, string ProviderUnderwriter,
    string ProviderReference, string Body, DateTimeOffset ReceivedAt, Guid EvidenceAssociationId, DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo, IReadOnlyList<JsonElement> AuthorisedLimits, IReadOnlyList<JsonElement> Conditions);

public sealed partial class CapacityService
{
    public Task<CommandOutcome> RecordResponseAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid escalationId, byte[] version,
        byte[] escalationVersion, CapacityResponseInput response, string key, Guid correlationId, CancellationToken token = default)
    {
        response = response with { ProviderUnderwriter = QuoteRatingService.Reason(response.ProviderUnderwriter, 200),
            ProviderReference = QuoteRatingService.Reason(response.ProviderReference, 100), Body = Correspondence(response.Body) };
        if (response.Outcome is not ("approve" or "approve-with-conditions" or "query" or "decline") || !ReferralRules.Hash(response.SubmissionHash) ||
            response.SubmissionId == Guid.Empty || response.EvidenceAssociationId == Guid.Empty) throw new QuoteOperationException(422, "capacity-response-invalid");
        var approving = response.Outcome is "approve" or "approve-with-conditions";
        if ((approving ? response.AuthorisedLimits.Count is < 1 or > 20 || response.ValidFrom is null || response.ValidTo is null || response.ValidFrom >= response.ValidTo
            : response.AuthorisedLimits.Count != 0 || response.ValidFrom is not null || response.ValidTo is not null) ||
            (response.Outcome == "approve-with-conditions" ? response.Conditions.Count is < 1 or > 20 : response.Conditions.Count != 0))
            throw new QuoteOperationException(422, "capacity-response-shape");
        UnderwritingDecisionContext? held = null; CapacityEscalation? escalation = null; CapacitySubmission? submission = null;
        UnderwritingEvidenceAssociation? proof = null; QuoteReferral? referral = null; ReferralCondition[] conditions = [];
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/escalations/{escalationId:D}/responses", key, correlationId),
            new { quoteId, cycleId, escalationId, version = Convert.ToBase64String(version), escalationVersion = Convert.ToBase64String(escalationVersion), response }, "capacity.response-recorded",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-record-capacity", time.GetUtcNow(), true, ct);
                escalation = await HoldEscalation(db, held, escalationId, ct);
                submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == response.SubmissionId && x.EscalationId == escalationId && x.QuoteId == quoteId && x.CycleId == cycleId, ct)
                    ?? throw new QuoteOperationException(404, "capacity-submission-not-found");
                proof = await db.Set<UnderwritingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM UnderwritingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={response.EvidenceAssociationId} AND QuoteId={quoteId} AND CycleId={cycleId}").AsNoTracking().SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "underwriting-evidence-not-found");
                referral = await db.Set<QuoteReferral>().FromSqlInterpolated($"SELECT * FROM QuoteReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={escalation.ReferralId} AND QuoteId={quoteId} AND CycleId={cycleId}").SingleAsync(ct);
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                UnderwritingDecisionContext.CheckVersion(escalation!.RowVersion, escalationVersion, "stale-capacity-escalation");
                if (escalation.CurrentSubmissionId != submission!.Id || submission.ContextHash != response.SubmissionHash || escalation.State == "superseded")
                    throw new QuoteOperationException(412, "stale-capacity-submission");
                if (response.ReceivedAt.Offset != TimeSpan.Zero || response.ReceivedAt > now || response.ReceivedAt < submission.SubmittedAt)
                    throw new QuoteOperationException(422, "capacity-response-time");
                if (response.ValidFrom is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero || response.ValidTo is { Offset: var toOffset } && toOffset != TimeSpan.Zero)
                    throw new QuoteOperationException(422, "capacity-response-time");
                var purpose = (await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, ct)).SingleOrDefault(x => x.CapacitySubmissionId == submission.Id);
                if (purpose is null || proof!.CapacitySubmissionId != submission.Id || proof.RequirementCode != "capacity-response" ||
                    proof.InputFingerprint != purpose.InputFingerprint || proof.WithdrawnEventId is not null || proof.LatestReviewId is null ||
                    !await db.Set<UnderwritingEvidenceEvent>().AnyAsync(x => x.Id == proof.LatestReviewId && x.AssociationId == proof.Id && x.Kind == "review" && x.Outcome == "accepted", ct))
                    throw new QuoteOperationException(409, "capacity-response-proof-required");
                using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
                CapacityExtension[] extensions;
                try { extensions = response.AuthorisedLimits.Select(CapacityRules.Extension).ToArray(); conditions = response.Conditions.Select(x => ReferralRules.Condition(x, proposal.RootElement)).ToArray(); }
                catch (ArgumentException) { throw new QuoteOperationException(422, "capacity-response-definition"); }
                var dimension = CapacityRules.Dimension(referral!.RuleCode, referral.Dimension);
                if (extensions.Any(x => x.Dimension != dimension || x.Dimension == "trade-restriction" && x.QuestionId != referral.RuleCode) ||
                    extensions.Select(x => x.Dimension).Distinct().Count() != extensions.Length || conditions.Select(x => x.DefinitionJson).Distinct().Count() != conditions.Length)
                    throw new QuoteOperationException(422, "capacity-response-extent");
                foreach (var signed in conditions.Where(x => x.TermsVersionId is not null))
                {
                    var terms = await QuoteTermsService.CurrentTerms(db, held, signed.TermsVersionId!.Value, now, ct);
                    if (terms.TermsHash != signed.TermsHash) throw new QuoteOperationException(412, "quote-terms-stale");
                }
                var definition = JsonSerializer.Serialize(new { quoteId, cycleId, submissionId = submission.Id, submissionHash = submission.ContextHash,
                    outcome = response.Outcome, validFrom = response.ValidFrom, validTo = response.ValidTo, authorisedLimits = response.AuthorisedLimits, conditions = response.Conditions }, QuoteRatingService.Json);
                var message = new CapacityMessage { QuoteId = quoteId, CycleId = cycleId, EscalationId = escalationId, SubmissionId = submission.Id,
                    ReferralId = referral.Id, ProviderId = escalation.ProviderId, Sequence = await NextMessage(db, escalationId, ct), Direction = "inbound", Provenance = "supplied-response",
                    Outcome = response.Outcome, ProviderUnderwriter = response.ProviderUnderwriter, ProviderReference = response.ProviderReference, Body = response.Body,
                    DefinitionJson = definition, EvidenceAssociationId = proof.Id, EvidenceReviewId = proof.LatestReviewId, ReceivedAt = response.ReceivedAt,
                    RecordedAt = now, RecordedBy = actor.UserId, CreatedAt = now, CreatedBy = actor.UserId };
                await AddCarrierConditions(db, held, referral, message, conditions, now, ct);
                message.ContentHash = Convert.FromHexString(QuoteCanonicalJson.Create(JsonSerializer.Serialize(new { definition, response.ProviderUnderwriter, response.ProviderReference,
                    response.Body, response.ReceivedAt, response.EvidenceAssociationId, evidenceReviewId = proof.LatestReviewId }), QuoteService.Pins(held.Revision)).ContentHash);
                db.Add(message); await db.SaveChangesAsync(ct); escalation.CurrentResponseId = message.Id;
                escalation.State = ResponseState(response.Outcome); escalation.UpdatedAt = now;
                await db.SaveChangesAsync(ct); await QuoteReferralService.RefreshState(db, held, now, ct);
                return await held.Receipt(db, message.Id, 201, "capacity.response-recorded", now, ct);
            }, token);
    }

    internal static string ResponseState(string outcome) => outcome switch { "approve" => "approved", "approve-with-conditions" => "conditional", "query" => "queried", "decline" => "declined", _ => throw new ArgumentException("Unknown capacity outcome.") };
    internal static async Task AddCarrierConditions(BackOfficeDbContext db, UnderwritingDecisionContext held, QuoteReferral referral,
        CapacityMessage message, IReadOnlyList<ReferralCondition> conditions, DateTimeOffset now, CancellationToken token)
    {
        if (conditions.Count == 0) return;
        var previousDecision = await (from escalation in db.Set<CapacityEscalation>()
                                      join previous in db.Set<CapacityMessage>() on escalation.CurrentResponseId equals previous.Id
                                      where escalation.Id == message.EscalationId select previous.DecisionId).SingleOrDefaultAsync(token);
        var retained = await UnderwritingEvidenceService.ActiveConditions(db, held.Cycle.Id, token);
        if (retained.Count(x => x.DecisionId != previousDecision) + conditions.Count > 100)
            throw new QuoteOperationException(422, "underwriting-condition-limit");
        // This record attributes the recording action to staff; the linked immutable
        // message identifies the carrier as the origin of these conditions.
        var decision = new QuoteReferralDecision { QuoteId = held.Cycle.QuoteId, CycleId = held.Cycle.Id, ReferralId = referral.Id,
            Sequence = checked((await db.Set<QuoteReferralDecision>().Where(x => x.ReferralId == referral.Id).MaxAsync(x => (int?)x.Sequence, token) ?? 0) + 1),
            Outcome = "approve-with-conditions", Reason = "Capacity provider conditions recorded: " + message.ProviderReference,
            ActorId = message.RecordedBy, AuthorityVersionId = held.Grants[0].Version.Id,
            ConditionsJson = "[" + string.Join(",", conditions.Select(x => x.DefinitionJson)) + "]", DecidedAt = now, CreatedAt = now, CreatedBy = message.RecordedBy };
        db.Add(decision); await db.SaveChangesAsync(token); message.DecisionId = decision.Id;
        var sequence = 0;
        foreach (var condition in conditions) db.Add(new QuoteCondition { QuoteId = decision.QuoteId, CycleId = decision.CycleId, ReferralId = referral.Id,
            DecisionId = decision.Id, Sequence = ++sequence, Code = condition.Code, Kind = condition.Kind, DefinitionJson = condition.DefinitionJson,
            Wording = condition.Wording, EndorsementCode = condition.EndorsementCode, CreatedAt = now, UpdatedAt = now, CreatedBy = message.RecordedBy });
    }
}
