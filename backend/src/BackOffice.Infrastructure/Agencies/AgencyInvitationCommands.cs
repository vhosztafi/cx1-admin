using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyInvitationCommands(IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies,SqlCommandBoundary commands,InvitationService issuer,TimeProvider time)
{
    public Task<CommandOutcome> Resend(ActorContext actor,Guid invitationId,string key,byte[] version,string reason,CancellationToken token=default)
        =>Execute(actor,invitationId,key,version,reason,true,token);
    public Task<CommandOutcome> Revoke(ActorContext actor,Guid invitationId,string key,byte[] version,string reason,CancellationToken token=default)
        =>Execute(actor,invitationId,key,version,reason,false,token);

    private async Task<CommandOutcome> Execute(ActorContext actor,Guid invitationId,string key,byte[] version,string reason,bool resend,CancellationToken token)
    {
        await agencies.Authorize(actor,null,token);
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");
        Guid agencyId;
        await using(var db=await factory.CreateDbContextAsync(token))
        {agencyId=await db.Set<AgencyInvitation>().Where(x=>x.Id==invitationId).Select(x=>x.AgencyId).SingleOrDefaultAsync(token);if(agencyId==Guid.Empty)throw new AgencyCommandException(404,"invitation-not-found");}
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/invitations/{invitationId}/"+(resend?"resend":"revoke"),key,Guid.NewGuid()),new{reason},resend?"agency.invitation-resent":"agency.invitation-revoked",async(db,ct)=>
        {
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={agencyId}").SingleAsync(ct);
            var reference=await db.Set<AgencyInvitation>().AsNoTracking().SingleAsync(x=>x.Id==invitationId,ct);
            var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.UserId}").SingleAsync(ct);
            var invitation=await db.Set<AgencyInvitation>().FromSqlInterpolated($"SELECT * FROM [AgencyInvitation] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={invitationId}").SingleAsync(ct);
            if(!CryptographicOperations.FixedTimeEquals(version,invitation.RowVersion))throw new AgencyCommandException(412,"stale-invitation");
            if(user.AgencyId!=agencyId||invitation.AgencyId!=agencyId||user.State!="invited"||agency.State is not ("draft" or "active" or "suspended"))throw new AgencyCommandException(409,"invitation-not-changeable");
            AgencyInvitation result=invitation;var now=time.GetUtcNow();
            if(resend)
            {
                if(agency.State!="active"||invitation.IssuedAt is null||invitation.State is not ("pending" or "expired" or "revoked"))throw new AgencyCommandException(409,"invitation-not-resendable");
                if(await db.Set<AgencyInvitation>().AnyAsync(x=>x.UserId==user.Id&&x.Id!=invitationId&&(x.State=="staged"||x.State=="pending"),ct))throw new AgencyCommandException(409,"invitation-replaced");
                if(invitation.State=="pending"){invitation.State="revoked";invitation.RevokedAt=now;await db.SaveChangesAsync(ct);}
                result=new AgencyInvitation{AgencyId=agencyId,UserId=user.Id,CreatedAt=now,CreatedBy=actor.UserId};db.Add(result);await db.SaveChangesAsync(ct);
                await issuer.IssueStaged(db,agencyId,result.Id,actor.UserId,await issuer.DefaultScenario(db,ct),ct);
            }
            else
            {
                if(invitation.State is not ("staged" or "pending"))throw new AgencyCommandException(409,"invitation-not-revocable");
                invitation.State="revoked";invitation.RevokedAt=now;
            }
            db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
            db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,SubjectRecordId=invitationId,EventType="agency.invitation-change-reason",Reason=reason,OccurredAt=now,After=JsonSerializer.Serialize(new{invitationId,resultId=result.Id})});
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,CreatedBy=actor.UserId,Action=resend?"agency.invitation-resent":"agency.invitation-revoked",OccurredAt=now});
            await db.SaveChangesAsync(ct);return new(result.Id,resend?202:200,JsonSerializer.Serialize(new{id=result.Id,userId=user.Id}),Etag:AgencyDraftService.Etag(result.RowVersion));
        },token);
    }
}
