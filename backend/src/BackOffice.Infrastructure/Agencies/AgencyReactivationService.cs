using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyReactivationService(AgencyDraftService agencies,AgencyReactivationAssessment assessment,SqlCommandBoundary commands,InvitationService invitations,TimeProvider time)
{
    public async Task<CommandOutcome> Propose(ActorContext actor,Guid agencyId,string key,byte[] expected,string reason,CancellationToken token=default)
    {
        reason=Reason(reason);await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/reactivate",key,Guid.NewGuid()),new{reason},"agency.reactivation-requested",async(db,ct)=>
        {
            var agency=await AgencyDraftService.Lock(db,agencyId,expected,ct);await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            var ready=await assessment.Assess(db,agencyId,ct);var now=time.GetUtcNow();
            var pending=await db.Set<AgencyStateRequest>().SingleOrDefaultAsync(x=>x.AgencyId==agencyId&&x.Kind=="reactivation"&&x.State=="pending",ct);
            if(pending is not null)
            {
                if(CryptographicOperations.FixedTimeEquals(pending.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"agency-reactivation-pending");
                pending.State="stale";pending.DecisionReason="Agency changed before a replacement reactivation proposal.";pending.DecidedAt=now;await db.SaveChangesAsync(ct);
            }
            var request=new AgencyStateRequest{AgencyId=agencyId,Kind="reactivation",RequestedState="active",BaseVersion=agency.RowVersion.ToArray(),ProposedInputFingerprint=ready.Fingerprint,RequestedBy=actor.UserId,RequestReason=reason,CreatedBy=actor.UserId,CreatedAt=now};
            db.Add(request);await db.SaveChangesAsync(ct);return Receipt(request,202);
        },token);
    }

    public async Task<CommandOutcome> Decide(ActorContext actor,Guid agencyId,Guid requestId,string key,byte[] expected,bool approve,string reason,CancellationToken token=default)
    {
        reason=Reason(reason);await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agency-state-requests/{requestId}/decision",key,Guid.NewGuid()),new{agencyId,approve,reason},"agency.reactivation-decided",async(db,ct)=>
        {
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleAsync(ct);
            await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            var request=await db.Set<AgencyStateRequest>().SingleOrDefaultAsync(x=>x.Id==requestId&&x.AgencyId==agencyId&&x.Kind=="reactivation",ct)??throw new AgencyCommandException(404,"reactivation-request-not-found");
            if(request.RequestedBy==actor.UserId)throw new AgencyCommandException(403,"independent-reviewer-required");
            if(!CryptographicOperations.FixedTimeEquals(request.RowVersion,expected))throw new AgencyCommandException(412,"stale-reactivation-request");
            if(request.State!="pending")throw new AgencyCommandException(409,"reactivation-already-decided");
            AgencyReactivationReadiness? ready=null;
            if(approve)
            {
                if(!CryptographicOperations.FixedTimeEquals(request.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"reactivation-stale-base");
                ready=await assessment.Assess(db,agencyId,ct);
                if(ready.Fingerprint!=request.ProposedInputFingerprint)throw new AgencyCommandException(409,"reactivation-stale-prerequisites");
            }
            var now=time.GetUtcNow();request.State=approve?"applied":"rejected";request.DecisionBy=actor.UserId;request.DecisionReason=reason;request.DecidedAt=now;await db.SaveChangesAsync(ct);
            if(ready is not null)
            {
                var users=await db.Set<StaffUser>().Where(x=>x.AgencyId==agencyId).OrderBy(x=>x.Id).ToListAsync(ct);
                foreach(var user in users)
                {
                    user.SecurityStamp=Guid.NewGuid().ToString("N");user.UpdatedAt=now;
                    var old=await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==agencyId&&x.UserId==user.Id&&(x.State=="staged"||x.State=="pending")).ToListAsync(ct);
                    foreach(var invitation in old){invitation.State="revoked";invitation.RevokedAt=now;invitation.UpdatedAt=now;}
                    await db.Set<UserSession>().Where(x=>x.UserId==user.Id&&x.RevokedAt==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,now),ct);
                }
                agency.State="active";agency.UpdatedAt=now;db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;await db.SaveChangesAsync(ct);
                if(ready.FreshInvitationUserIds.Count>0)
                {
                    await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'agency-invitation-delivery'").AsNoTracking().ToListAsync(ct);
                    var scenario=await invitations.DefaultScenario(db,ct);
                    foreach(var userId in ready.FreshInvitationUserIds)
                    {
                        var replacement=new AgencyInvitation{AgencyId=agencyId,UserId=userId,CreatedBy=actor.UserId,CreatedAt=now};db.Add(replacement);await db.SaveChangesAsync(ct);
                        await invitations.IssueStaged(db,agencyId,replacement.Id,actor.UserId,scenario,ct);
                    }
                }
            }
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,Action=approve?"agency.reactivated":"agency.reactivation-rejected",OccurredAt=now,CreatedAt=now,CreatedBy=actor.UserId});
            await db.SaveChangesAsync(ct);return Receipt(request,200);
        },token);
    }
    private static string Reason(string reason)
    {if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");return reason.Trim();}
    private static CommandOutcome Receipt(AgencyStateRequest request,int status)=>new(request.Id,status,JsonSerializer.Serialize(new{id=request.Id}),Etag:AgencyDraftService.Etag(request.RowVersion));
}
