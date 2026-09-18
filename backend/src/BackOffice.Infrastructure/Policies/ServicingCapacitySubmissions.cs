using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService
{
    public const string WorkKind = "servicing-capacity";

    public Task<CommandOutcome> SubmitAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid caseId,
        byte[] version, byte[] caseVersion, Guid lease, string body, string reason, IReadOnlyList<Guid> evidenceIds,
        Guid scenarioVersionId, string key, Guid correlation, CancellationToken token = default)
        =>SubmitCoreAsync(actor,draftId,cycleId,caseId,version,caseVersion,lease,body,reason,evidenceIds,scenarioVersionId,null,key,correlation,token);

    public Task<CommandOutcome> ReplyAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid caseId,Guid responseId,
        byte[] version,byte[] caseVersion,Guid lease,string body,string reason,IReadOnlyList<Guid> evidenceIds,
        Guid scenarioVersionId,string key,Guid correlation,CancellationToken token=default)
        =>SubmitCoreAsync(actor,draftId,cycleId,caseId,version,caseVersion,lease,body,reason,evidenceIds,scenarioVersionId,responseId,key,correlation,token);

    private Task<CommandOutcome> SubmitCoreAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid caseId,
        byte[] version, byte[] caseVersion, Guid lease, string body, string reason, IReadOnlyList<Guid> evidenceIds,
        Guid scenarioVersionId,Guid? queryResponseId,string key, Guid correlation, CancellationToken token)
    {
        if (draftId == Guid.Empty || cycleId == Guid.Empty || caseId == Guid.Empty || lease == Guid.Empty || scenarioVersionId == Guid.Empty || queryResponseId==Guid.Empty ||
            version is null || version.Length != 8 || caseVersion is null || caseVersion.Length != 8 ||
            string.IsNullOrWhiteSpace(body) || body.Length > 10000 || body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10 || evidenceIds is null || evidenceIds.Count > 20 ||
            evidenceIds.Contains(Guid.Empty) || evidenceIds.Distinct().Count() != evidenceIds.Count)
            throw new QuoteOperationException(422, "servicing-capacity-submission-invalid");
        reason = QuoteRatingService.Reason(reason); body = body.Trim();
        version = version.ToArray(); caseVersion = caseVersion.ToArray(); var selectedIds = evidenceIds.Order().ToArray();
        ServicingDecisionContext? held = null; ServicingCapacityCase? capacity = null; ServicingReferral? referral = null;
        SettingVersion? scenario = null; int? dueDays = null; int? dueHours = null;
        var selected = new List<(ServicingEvidenceAssociation Association, Guid ReviewId)>();
        var route=queryResponseId is null?"submissions":"query-replies";
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/drafts/{draftId:D}/capacity/{caseId:D}/{route}", key, correlation),
            new { draftId, cycleId, caseId, version = Convert.ToBase64String(version), caseVersion = Convert.ToBase64String(caseVersion),
                lease, body, reason, evidenceIds = selectedIds, scenarioVersionId,queryResponseId }, "servicing.capacity-submitted",
            async (db, ct) =>
            {
                held = await HoldEscalationAuthority(db, actor, draftId, cycleId, lease, ct);
                capacity = await db.Set<ServicingCapacityCase>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCase WITH(UPDLOCK,HOLDLOCK) WHERE Id={caseId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "servicing-capacity-case-not-found");
                if (capacity.State == "superseded" || capacity.BinderVersionId != held.Cycle.BinderVersionId || capacity.ProviderId != held.Scope.Eligible.BinderVersion.ProviderId)
                    throw new QuoteOperationException(409, "servicing-capacity-case-stale");
                referral = await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x => x.Id == capacity.ReferralId && x.CycleId == cycleId, ct);
                if (referral.State is "declined" or "superseded") throw new QuoteOperationException(409, "servicing-referral-reopen-required");
                if(queryResponseId is Guid response && !await db.Set<ServicingCapacityResponseRecord>().AnyAsync(x=>x.Id==response && x.CaseId==caseId &&
                    x.DraftId==draftId && x.CycleId==cycleId && x.Outcome=="query" && x.ApplicationState=="applied",ct))
                    throw new QuoteOperationException(404,"servicing-capacity-query-not-found");
                scenario = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Id={scenarioVersionId}").AsNoTracking().SingleOrDefaultAsync(ct);
                var setting = scenario is null ? null : CapacitySeed.Parse(scenario);
                if (setting is null || scenario!.EffectiveFrom > time.GetUtcNow() ||
                    await db.Set<SettingVersion>().AnyAsync(x => x.Scope == scenario.Scope && x.Version > scenario.Version && x.EffectiveFrom <= time.GetUtcNow(), ct))
                    throw new QuoteOperationException(409, "servicing-capacity-scenario-unavailable");
                dueDays = setting.Value.ResponseDueWorkingDays; dueHours = setting.Value.ResponseDueHours;
                var requirements = await ServicingEvidenceProjection.RequirementsAsync(db, held, ct);
                foreach (var id in selectedIds)
                {
                    var proof = await db.Set<ServicingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM ServicingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={id} AND DraftId={draftId} AND CycleId={cycleId}").AsNoTracking().SingleOrDefaultAsync(ct)
                        ?? throw new QuoteOperationException(404, "servicing-evidence-not-found");
                    if (proof.WithdrawnEventId is not null || proof.LatestReviewId is not Guid review ||
                        !requirements.Any(x => x.Code == proof.RequirementCode && x.RiskItemId == proof.RiskItemId && x.InputFingerprint == proof.InputFingerprint) ||
                        !await db.Set<ServicingEvidenceEvent>().AnyAsync(x => x.Id == review && x.AssociationId == id && x.Kind == "review" && x.Outcome == "accepted", ct) ||
                        !await db.Set<ServicingEvidenceFile>().AnyAsync(x => x.Id == proof.FileId && x.DraftId == draftId && x.ScreeningState == "accepted", ct))
                        throw new QuoteOperationException(409, "servicing-capacity-evidence-unavailable");
                    selected.Add((proof, review));
                }
            },
            async (db, ct) =>
            {
                await held!.Current(db, factory, time, version, lease, ct);
                if (!CryptographicOperations.FixedTimeEquals(capacity!.RowVersion, caseVersion)) throw new QuoteOperationException(412, "servicing-capacity-case-stale");
                if(queryResponseId is Guid query)
                {
                    if(capacity.State!="queried" || capacity.CurrentResponseId!=query || capacity.CurrentSubmissionId is null)
                        throw new QuoteOperationException(409,"servicing-capacity-query-stale");
                    await AddCorrespondence(db,capacity,capacity.CurrentSubmissionId.Value,"query-reply",body,actor.UserId,time.GetUtcNow(),ct);
                    await db.SaveChangesAsync(ct);
                    // This transition and the next immutable submission commit
                    // together. No observer sees an unqueued partial reply.
                    capacity.State="draft";capacity.UpdatedAt=time.GetUtcNow();await db.SaveChangesAsync(ct);
                }
                if (capacity.State != "draft") throw new QuoteOperationException(409, "servicing-capacity-submission-state");
                var now = time.GetUtcNow(); var id = Guid.NewGuid();
                var sequence = checked((await db.Set<ServicingCapacitySubmission>().Where(x => x.CaseId == caseId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1);
                var targetDates = ServicingEvidenceProjection.Slices(held).SelectMany(x => x.Proposal.GetProperty("cover").GetProperty("requestedSections").EnumerateArray()
                    .Where(section => section.GetProperty("selected").GetBoolean() && section.GetProperty("code").GetString() == "premises")
                    .SelectMany(section => section.GetProperty("premisesIds").EnumerateArray().Select(p => new {premisesId=p.GetGuid(),x.EffectiveAt})))
                    .GroupBy(x=>x.premisesId).OrderBy(x=>x.Key).Select(x=>new {premisesId=x.Key,effectiveDates=x.Select(d=>d.EffectiveAt).Distinct().Order().ToArray()}).ToArray();
                var targets=targetDates.Select(x=>x.premisesId).ToArray();
                // Explicit minimal carrier projection, never full proposal,
                // contacts, private notes or evidence bytes not selected by staff.
                var context = JsonSerializer.Serialize(new { format = "servicing-capacity-submission-1", draftId, cycleId,
                    revisionId = held.Cycle.RevisionId, ratingId = held.Rating.Id, caseId, referralId = referral!.Id,
                    providerId = capacity.ProviderId, binderVersionId = capacity.BinderVersionId, productVersionId = held.Cycle.ProductVersionId,
                    submissionId = id, sequence,queryResponseId, inputHash = Convert.ToHexStringLower(held.Cycle.InputHash),
                    startsAt = held.Input.Slices[0].EffectiveAt, endsAt = held.Input.Term.EndsAt,
                    ruleCode = referral.RuleCode, dimension = referral.Dimension, targetId = referral.RiskItemId, conditionTargetIds = targets, conditionTargets=targetDates,
                    requiredAuthority = JsonSerializer.Deserialize<JsonElement>(referral.RequiredAuthorityJson), body, reason, scenarioVersionId,
                    evidence = selected.Select(x => new { associationId = x.Association.Id, reviewId = x.ReviewId }).ToArray() });
                var bytes = Encoding.UTF8.GetBytes(context);
                if (bytes.Length > 1048576) throw new QuoteOperationException(422, "servicing-capacity-request-too-large");
                var work = new OutboxWork { Kind = WorkKind, SubjectRecordId = id, OperationKey = $"servicing-capacity/{id:N}",
                    ScenarioVersionId = scenarioVersionId, Payload = JsonSerializer.Serialize(new { draftId, cycleId, caseId, submissionId = id }),
                    CorrelationId = correlation, NextAttemptAt = now, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(work); await db.SaveChangesAsync(ct);
                var submission = new ServicingCapacitySubmission { Id = id, CaseId = caseId, DraftId = draftId, CycleId = cycleId,
                    RevisionId = held.Cycle.RevisionId, RatingId = held.Rating.Id, Sequence = sequence, Body = body, Reason = reason,
                    ContextJson = context, ContextHash = SHA256.HashData(bytes), WorkId = work.Id, ScenarioVersionId = scenarioVersionId,
                    SubmittedBy = actor.UserId, SubmittedAt = now, CreatedBy = actor.UserId, CreatedAt = now,
                    ResponseDueAt = dueDays is int days ? CapacityRules.ResponseDue(now, days) : now.AddHours(dueHours!.Value) };
                db.Add(submission); await db.SaveChangesAsync(ct);
                await AddCorrespondence(db,capacity,id,"submission",body,actor.UserId,now,ct);
                foreach (var proof in selected)
                    db.Add(new ServicingCapacitySubmissionEvidence { SubmissionId = id, CaseId = caseId, DraftId = draftId, CycleId = cycleId,
                        RevisionId = held.Cycle.RevisionId, RatingId = held.Rating.Id, AssociationId = proof.Association.Id,
                        ReviewId = proof.ReviewId, CreatedBy = actor.UserId, CreatedAt = now });
                await db.SaveChangesAsync(ct);
                capacity.CurrentSubmissionId = id; capacity.CurrentResponseId = null; capacity.State = "queued"; capacity.UpdatedAt = now;
                var receipt = await held.Receipt(db, caseId, 202, now, ct);
                return receipt with { Body = JsonSerializer.Serialize(new { id = caseId, draftId, cycleId, revisionId = held.Cycle.RevisionId,
                    submissionId = id, jobId = work.Id, state = "queued", draftEtag = receipt.Etag }) };
            }, token);
    }

    private async Task<ServicingDecisionContext> HoldEscalationAuthority(BackOfficeDbContext db, ActorContext actor,
        Guid draftId, Guid cycleId, Guid lease, CancellationToken token)
    {
        var now = time.GetUtcNow();
        var held = await ServicingDecisionContext.Hold(db, actor, draftId, "underwriting-escalate", now, token, cycleId);
        if (held.Rating.ExpiresAt <= now) throw new QuoteOperationException(409, "servicing-rating-expired");
        await new ServicingDraftService(factory, time).DemandLease(db, draftId, held.Scope.Source.Scope.Actor.UserId, lease, token);
        var remaining = held.Input.Term with { Kind = "short-period", StartsAt = held.Input.Slices[0].EffectiveAt };
        var grants = await QuoteUnderwritingScope.GrantsAsync(db, held.Scope.Source, held.Cycle.ProductVersionId,
            held.Scope.Eligible.BinderVersion, held.Scope.Eligible.Capture.Product.Code, remaining, now, token);
        if (grants.Count == 0) throw new QuoteOperationException(403, "servicing-capacity-authority-required");
        var provider = held.Scope.Eligible.BinderVersion.ProviderId;
        if (!await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={provider} AND State='active'").AsNoTracking().AnyAsync(token))
            throw new QuoteOperationException(409, "servicing-capacity-provider-unavailable");
        return held;
    }
}
