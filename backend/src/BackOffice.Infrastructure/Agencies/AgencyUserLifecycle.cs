using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyUserLifecycle(AgencyDraftService agencies,SqlCommandBoundary commands,InvitationService issuer,TimeProvider time)
{
    public Task<CommandOutcome> Edit(ActorContext actor,Guid agencyId,Guid userId,string key,byte[] version,string displayName,string role,string reason,CancellationToken token=default)
    {
        var input=AgencyUserRules.ValidateProfile(displayName,role);
        return Execute(actor,agencyId,userId,key,version,"edit",input.DisplayName,input.Role,reason,token);
    }
    public Task<CommandOutcome> Deactivate(ActorContext actor,Guid agencyId,Guid userId,string key,byte[] version,string reason,CancellationToken token=default)
        =>Execute(actor,agencyId,userId,key,version,"deactivate",null,null,reason,token);
    public Task<CommandOutcome> Reactivate(ActorContext actor,Guid agencyId,Guid userId,string key,byte[] version,string reason,CancellationToken token=default)
        =>Execute(actor,agencyId,userId,key,version,"reactivate",null,null,reason,token);

    private async Task<CommandOutcome> Execute(ActorContext actor,Guid agencyId,Guid userId,string key,byte[] version,string action,string? name,string? role,string reason,CancellationToken token)
    {
        if(actor.AgencyId is null)await agencies.Authorize(actor,agencyId,token);
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/users/{userId}"+(action=="edit"?"":"/"+action),key,Guid.NewGuid()),new{name,role,reason},"agency.user-"+action,(db,ct)=>AgencyUserAuthority.Authorize(db,actor,agencyId,true,ct),async(db,ct)=>
        {
            // All user/invitation mutations serialize on the agency before the user.
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={agencyId}").SingleAsync(ct);
            var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={userId} AND [AgencyId]={agencyId}").SingleOrDefaultAsync(ct)
                ??throw new AgencyCommandException(404,"agency-user-not-found");
            if(!CryptographicOperations.FixedTimeEquals(version,user.RowVersion))throw new AgencyCommandException(412,"stale-agency-user");
            if(agency.State is not ("draft" or "active" or "suspended"))throw new AgencyCommandException(409,"agency-user-not-changeable");
            var link=await db.Set<UserRole>().SingleAsync(x=>x.UserId==userId,ct);
            var currentRole=await db.Set<Role>().SingleAsync(x=>x.Id==link.RoleId,ct);
            if(currentRole.Scope!="agency"||currentRole.Code is not ("broker-admin" or "broker-user" or "broker-readonly"))throw new AgencyCommandException(409,"invalid-agency-user-role");
            if(user.State=="active"&&currentRole.Code=="broker-admin"&&(action=="deactivate"||(action=="edit"&&role!="broker-admin")))
            {
                var otherAdmin=await(from other in db.Set<StaffUser>() join grant in db.Set<UserRole>() on other.Id equals grant.UserId join permitted in db.Set<Role>() on grant.RoleId equals permitted.Id
                    where other.AgencyId==agencyId&&other.Id!=userId&&other.State=="active"&&permitted.Scope=="agency"&&permitted.Code=="broker-admin" select other.Id).AnyAsync(ct);
                if(!otherAdmin)throw new AgencyCommandException(409,"last-active-agency-admin");
            }
            var now=time.GetUtcNow();var before=new{user.State,user.DisplayName,role=currentRole.Code};var revokeSessions=false;
            if(action=="edit")
            {
                user.DisplayName=name!;
                if(currentRole.Code!=role){link.RoleId=(await db.Set<Role>().SingleAsync(x=>x.Code==role&&x.Scope=="agency",ct)).Id;revokeSessions=true;}
            }
            else if(action=="deactivate")
            {
                if(user.State is not ("active" or "invited"))throw new AgencyCommandException(409,"agency-user-not-active");
                foreach(var invitation in await db.Set<AgencyInvitation>().Where(x=>x.UserId==userId&&(x.State=="staged"||x.State=="pending")).ToListAsync(ct))
                {invitation.State="revoked";invitation.RevokedAt=now;}
                user.State="suspended";revokeSessions=true;
            }
            else
            {
                if(user.State!="suspended"||agency.State is not ("draft" or "active"))throw new AgencyCommandException(409,"agency-user-not-reactivatable");
                if(await db.Set<AgencyInvitation>().AnyAsync(x=>x.UserId==userId&&(x.State=="staged"||x.State=="pending"),ct))throw new AgencyCommandException(409,"agency-user-has-live-invitation");
                var credential=await db.Set<UserCredential>().AnyAsync(x=>x.UserId==userId,ct);
                if(credential&&agency.State!="active")throw new AgencyCommandException(409,"agency-not-active");
                user.State=credential?"active":"invited";revokeSessions=true;
                if(!credential)
                {
                    var replacement=new AgencyInvitation{AgencyId=agencyId,UserId=userId,CreatedBy=actor.UserId,CreatedAt=now};db.Add(replacement);await db.SaveChangesAsync(ct);
                    if(agency.State=="active")await issuer.IssueStaged(db,agencyId,replacement.Id,actor.UserId,await issuer.DefaultScenario(db,ct),ct);
                }
            }
            if(revokeSessions)
            {
                user.SecurityStamp=Guid.NewGuid().ToString("N");
                await db.Set<UserSession>().Where(x=>x.UserId==userId&&x.RevokedAt==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,now),ct);
            }
            db.Entry(user).Property(x=>x.UpdatedAt).IsModified=true;db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
            db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,SubjectRecordId=userId,EventType="agency.user-change-reason",Reason=reason,OccurredAt=now,Before=JsonSerializer.Serialize(before),After=JsonSerializer.Serialize(new{user.State,user.DisplayName,role=role??currentRole.Code})});
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,CreatedBy=actor.UserId,Action="agency.user-"+action,OccurredAt=now});
            await db.SaveChangesAsync(ct);return new(userId,200,JsonSerializer.Serialize(new{id=userId}),Etag:AgencyDraftService.Etag(user.RowVersion));
        },token);
    }
}
