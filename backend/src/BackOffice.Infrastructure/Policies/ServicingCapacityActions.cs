using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService
{
    public Task<CommandOutcome> ActionAsync(ActorContext actor, Guid draftId, Guid cycleId, Guid caseId,
        byte[] version, byte[] caseVersion, Guid lease, string action, string reason, string key, Guid correlation,
        CancellationToken token = default)
    {
        if (draftId == Guid.Empty || cycleId == Guid.Empty || caseId == Guid.Empty || lease == Guid.Empty ||
            version is null || version.Length != 8 || caseVersion is null || caseVersion.Length != 8 ||
            action is not ("withdraw" or "reopen") || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
            throw new QuoteOperationException(422, "servicing-capacity-action-invalid");
        reason = QuoteRatingService.Reason(reason); version = version.ToArray(); caseVersion = caseVersion.ToArray();
        ServicingDecisionContext? held = null; ServicingCapacityCase? capacity = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/drafts/{draftId:D}/capacity/{caseId:D}/actions", key, correlation),
            new { draftId, cycleId, caseId, version = Convert.ToBase64String(version), caseVersion = Convert.ToBase64String(caseVersion),
                lease, action, reason }, "servicing.capacity-action",
            async (db, ct) =>
            {
                held = await HoldEscalationAuthority(db, actor, draftId, cycleId, lease, ct);
                capacity = await db.Set<ServicingCapacityCase>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCase WITH(UPDLOCK,HOLDLOCK) WHERE Id={caseId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "servicing-capacity-case-not-found");
                if (capacity.State == "superseded" || capacity.BinderVersionId != held.Cycle.BinderVersionId ||
                    capacity.ProviderId != held.Scope.Eligible.BinderVersion.ProviderId)
                    throw new QuoteOperationException(409, "servicing-capacity-case-stale");
                if (await db.Set<ServicingReferral>().AnyAsync(x => x.Id == capacity.ReferralId && x.State == "superseded", ct))
                    throw new QuoteOperationException(409, "servicing-capacity-case-stale");
            },
            async (db, ct) =>
            {
                await held!.Current(db, factory, time, version, lease, ct);
                if (!CryptographicOperations.FixedTimeEquals(capacity!.RowVersion, caseVersion))
                    throw new QuoteOperationException(412, "servicing-capacity-case-stale");
                string next;
                try { next = ServicingCapacityRules.ActionState(capacity.State, action); }
                catch (ArgumentException) { throw new QuoteOperationException(409, "servicing-capacity-action-state"); }
                var now = time.GetUtcNow();
                var before = JsonSerializer.Serialize(new { capacity.State, capacity.CurrentSubmissionId });
                // Pending work and immutable submissions remain history. The
                // worker must recheck this locked state before applying a result.
                capacity.State = next; capacity.UpdatedAt = now;
                db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now, OccurredAt = now,
                    SubjectRecordId = caseId, EventType = "servicing.capacity-action-detail", Reason = reason, Before = before,
                    After = JsonSerializer.Serialize(new { action, capacity.State, capacity.CurrentSubmissionId }), CorrelationId = correlation });
                return await held.Receipt(db, caseId, 200, now, ct);
            }, token);
    }
}
