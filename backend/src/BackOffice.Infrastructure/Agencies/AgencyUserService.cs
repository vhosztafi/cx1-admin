using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyUserService(AgencyDraftService agencies,SqlCommandBoundary commands,TimeProvider time,InvitationService? issuer=null)
{
    public async Task<CommandOutcome> Stage(ActorContext actor,Guid agencyId,string key,byte[] version,AgencyUserInput input,CancellationToken token=default)
        =>await Create(actor,agencyId,key,version,input,false,token);

    public async Task<CommandOutcome> Invite(ActorContext actor,Guid agencyId,string key,byte[] version,AgencyUserInput input,CancellationToken token=default)
        =>await Create(actor,agencyId,key,version,input,true,token);

    private async Task<CommandOutcome> Create(ActorContext actor,Guid agencyId,string key,byte[] version,AgencyUserInput input,bool allowActive,CancellationToken token)
    {
        input=AgencyUserRules.Validate(input.Email,input.DisplayName,input.Role);
        if(actor.AgencyId is null)await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/users",key,Guid.NewGuid()),input,allowActive?"agency.user-created":"agency.user-staged",(db,ct)=>AgencyUserAuthority.Authorize(db,actor,agencyId,allowActive,ct),async(db,ct)=>
        {
            var agency=await AgencyDraftService.Lock(db,agencyId,version,ct);
            if(agency.State!="draft"&&(!allowActive||agency.State!="active"))throw new AgencyCommandException(409,"agency-not-invitable");
            if(await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,HOLDLOCK) WHERE [NormalizedEmail]={input.NormalizedEmail}").AnyAsync(ct))
                throw new AgencyCommandException(409,"agency-user-email-unavailable");
            var role=await db.Set<Role>().SingleAsync(x=>x.Code==input.Role&&x.Scope=="agency",ct);
            var user=new StaffUser{AgencyId=agencyId,Email=input.Email,NormalizedEmail=input.NormalizedEmail,DisplayName=input.DisplayName,State="invited",CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};
            db.Add(user);await db.SaveChangesAsync(ct);
            db.Add(new UserRole{UserId=user.Id,RoleId=role.Id,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()});await db.SaveChangesAsync(ct);
            var invitation=new AgencyInvitation{AgencyId=agencyId,UserId=user.Id,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};db.Add(invitation);
            db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,CreatedBy=actor.UserId,Action=agency.State=="draft"?"agency.user-staged":"agency.user-created",OccurredAt=time.GetUtcNow()});
            await db.SaveChangesAsync(ct);
            if(agency.State=="active")
            {
                if(issuer is null)throw new InvalidOperationException("Invitation issuer is required for active agencies.");
                await issuer.IssueStaged(db,agencyId,invitation.Id,actor.UserId,await issuer.DefaultScenario(db,ct),ct);
            }
            return new(user.Id,201,JsonSerializer.Serialize(new{id=user.Id,invitationId=invitation.Id}),Etag:AgencyDraftService.Etag(agency.RowVersion));
        },token);
    }

    internal static async Task RevokeDraftUsers(BackOfficeDbContext db,Guid agencyId,DateTimeOffset now,CancellationToken token)
    {
        // The caller owns the agency lock. Keep agency -> ordered users -> invitations.
        var users=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,ROWLOCK) WHERE [AgencyId]={agencyId}").OrderBy(x=>x.Id).ToListAsync(token);
        foreach(var user in users)
        {
            var invitations=await db.Set<AgencyInvitation>().Where(x=>x.UserId==user.Id&&(x.State=="staged"||x.State=="pending")).ToListAsync(token);
            foreach(var invitation in invitations){invitation.State="revoked";invitation.RevokedAt=now;}
            user.State="suspended";user.SecurityStamp=Guid.NewGuid().ToString("N");
            await db.Set<UserSession>().Where(x=>x.UserId==user.Id&&x.RevokedAt==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,now),token);
        }
    }
}
