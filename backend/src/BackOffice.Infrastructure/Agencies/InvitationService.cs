using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class InvitationService(AgencyNotificationService notifications,TimeProvider time)
{
    public async Task<Guid> DefaultScenario(BackOfficeDbContext db,CancellationToken token=default)
    {
        var now=time.GetUtcNow();
        var setting=await db.Set<SettingVersion>().Where(x=>x.Scope=="agency-invitation-delivery"&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token)
            ??throw new AgencyCommandException(503,"invitation-settings-unavailable");
        AgencyNotificationWorker.Scenario(setting.Values);return setting.Id;
    }

    // Transaction participant for approved activation or an authorized active-agency
    // invitation command. Returns only the persisted ID; raw tokens never enter receipts.
    public async Task<Guid> IssueStaged(BackOfficeDbContext db,Guid agencyId,Guid invitationId,Guid actorId,Guid scenarioVersionId,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Invitation issue requires its owning transaction.");
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={agencyId}").SingleOrDefaultAsync(token);
        if(agency?.State!="active")throw new AgencyCommandException(409,"agency-not-active");
        var reference=await db.Set<AgencyInvitation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==invitationId&&x.AgencyId==agencyId,token)
            ??throw new AgencyCommandException(404,"invitation-not-found");
        var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.UserId}").SingleAsync(token);
        var invitation=await db.Set<AgencyInvitation>().FromSqlInterpolated($"SELECT * FROM [AgencyInvitation] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={invitationId}").SingleAsync(token);
        if(user.AgencyId!=agencyId||user.State!="invited"||invitation.State!="staged")throw new AgencyCommandException(409,"invitation-not-staged");
        var roles=await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id select role).ToListAsync(token);
        if(roles.Count!=1||roles[0].Scope!="agency"||roles[0].Code is not ("broker-admin" or "broker-user" or "broker-readonly"))throw new AgencyCommandException(409,"invalid-agency-user-role");
        var secret=InvitationToken.Create();var now=time.GetUtcNow();
        var notificationId=await notifications.EnqueueInvitation(db,agencyId,invitationId,scenarioVersionId,actorId,new(){Recipient=user.Email,Template="agency-invitation",Content=secret.Value},token);
        invitation.TokenHash=secret.Hash;invitation.IssuedAt=now;invitation.ExpiresAt=now.AddDays(14);invitation.NotificationId=notificationId;invitation.State="pending";
        db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actorId,CreatedBy=actorId,Action="agency.invitation-issued",OccurredAt=now,CreatedAt=now});
        await db.SaveChangesAsync(token);return invitation.Id;
    }

    // Caller must already hold the agency lock, which serializes issue, revoke and delivery.
    internal static async Task<bool> CurrentForDelivery(BackOfficeDbContext db,AgencyNotification message,DateTimeOffset now,CancellationToken token)
    {
        if(message.InvitationId is not Guid id)return false;
        var invitation=await db.Set<AgencyInvitation>().SingleOrDefaultAsync(x=>x.Id==id&&x.AgencyId==message.AgencyId,token);
        if(invitation is null||invitation.State!="pending"||invitation.NotificationId!=message.Id||invitation.IssuedAt>now||invitation.ExpiresAt<=now||invitation.TokenHash is null)return false;
        return await db.Set<StaffUser>().AnyAsync(x=>x.Id==invitation.UserId&&x.AgencyId==message.AgencyId&&x.State=="invited",token);
    }
}
