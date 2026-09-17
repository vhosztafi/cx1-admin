using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;
public sealed partial class CapacityService
{
    public Task<CommandOutcome> ActionAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid escalationId, byte[] version,
        byte[] escalationVersion, string action, Guid? assignedUserId, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        reason = QuoteRatingService.Reason(reason);
        if (action is not ("withdraw" or "reopen" or "assign") || (action == "assign") != assignedUserId.HasValue || assignedUserId == Guid.Empty)
            throw new QuoteOperationException(422, "capacity-action-invalid");
        UnderwritingDecisionContext? held = null; CapacityEscalation? escalation = null; QuoteReferral? referral = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/escalations/{escalationId:D}/actions", key, correlationId),
            new { quoteId, cycleId, escalationId, version = Convert.ToBase64String(version), escalationVersion = Convert.ToBase64String(escalationVersion), action, assignedUserId, reason }, "capacity.action",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-escalate", time.GetUtcNow(), true, ct);
                escalation = await HoldEscalation(db, held, escalationId, ct);
                referral = await db.Set<QuoteReferral>().FromSqlInterpolated($"SELECT * FROM QuoteReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={escalation.ReferralId} AND CycleId={cycleId} AND QuoteId={quoteId}").SingleAsync(ct);
                // Assignment is routing only. The assignee still needs their own
                // current authority for any later underwriting decision.
                if (assignedUserId is Guid target)
                {
                    var identity = await IdentitySnapshot.Lock(db, new IdentityReference(target, null), ct);
                    if (identity is null || !identity.Roles.Any(x => x.Code == "senior-underwriter" && x.Scope == "internal"))
                        throw new QuoteOperationException(409, "capacity-assignee-unavailable");
                }
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                UnderwritingDecisionContext.CheckVersion(escalation!.RowVersion, escalationVersion, "stale-capacity-escalation");
                if (escalation.State == "superseded" || referral!.State == "superseded" ||
                    action == "withdraw" && escalation.State is not ("queued" or "sent" or "queried" or "failed") ||
                    action == "reopen" && escalation.State is not ("approved" or "conditional" or "declined"))
                    throw new QuoteOperationException(409, "capacity-action-state");
                var before = JsonSerializer.Serialize(new { escalation.State, escalation.CurrentSubmissionId, escalation.CurrentResponseId, referral.AssignedUserId });
                if (action == "assign") { referral.AssignedUserId = assignedUserId; referral.UpdatedAt = now; }
                // Keep monotonic submission/response pointers. Draft state fences
                // their applicability until an explicit new submission is made.
                else escalation.State = "draft";
                escalation.UpdatedAt = now;
                db.Add(new AuditEvent { ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now, OccurredAt = now,
                    SubjectRecordId = escalation.Id, EventType = "capacity.action-recorded", Reason = reason, Before = before,
                    After = JsonSerializer.Serialize(new { action, assignedUserId, state = escalation.State }), CorrelationId = correlationId });
                await db.SaveChangesAsync(ct);
                if (action != "assign") await QuoteReferralService.RefreshState(db, held, now, ct);
                return await held.Receipt(db, escalation.Id, 200, "capacity." + action, now, ct);
            }, token);
    }
    internal static IQueryable<StaffUser> SeniorUsers(BackOfficeDbContext db) =>
        db.Set<StaffUser>().AsNoTracking().Where(user => user.State == "active" && user.AgencyId == null &&
            (from membership in db.Set<UserRole>() join role in db.Set<Role>() on membership.RoleId equals role.Id
             where membership.UserId == user.Id && role.Code == "senior-underwriter" && role.Scope == "internal" select role.Id).Any());
}
