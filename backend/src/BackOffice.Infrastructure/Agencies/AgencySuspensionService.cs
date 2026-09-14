using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencySuspensionService(AgencyDraftService agencies,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Propose(ActorContext actor,Guid agencyId,string key,byte[] expected,string reason,CancellationToken token=default)
    {
        reason=Reason(reason);await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/suspend",key,Guid.NewGuid()),new{reason},"agency.suspension-requested",async(db,ct)=>
        {
            var agency=await AgencyDraftService.Lock(db,agencyId,expected,ct);await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            if(agency.State!="active")throw new AgencyCommandException(409,"agency-not-active");
            var now=time.GetUtcNow();
            var pending=await db.Set<AgencyStateRequest>().SingleOrDefaultAsync(x=>x.AgencyId==agencyId&&x.Kind=="suspension"&&x.State=="pending",ct);
            if(pending is not null)
            {
                if(CryptographicOperations.FixedTimeEquals(pending.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"agency-suspension-pending");
                pending.State="stale";pending.DecisionReason="Agency changed before a replacement suspension proposal.";pending.DecidedAt=now;await db.SaveChangesAsync(ct);
            }
            var request=new AgencyStateRequest{AgencyId=agencyId,Kind="suspension",RequestedState="suspended",BaseVersion=agency.RowVersion.ToArray(),ProposedInputFingerprint=Fingerprint(agency),RequestedBy=actor.UserId,RequestReason=reason,CreatedBy=actor.UserId,CreatedAt=now};
            db.Add(request);await db.SaveChangesAsync(ct);return Receipt(request,202);
        },token);
    }

    public async Task<CommandOutcome> Decide(ActorContext actor,Guid agencyId,Guid requestId,string key,byte[] expected,bool approve,string reason,CancellationToken token=default)
    {
        reason=Reason(reason);await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agency-state-requests/{requestId}/decision",key,Guid.NewGuid()),new{agencyId,approve,reason},"agency.suspension-decided",async(db,ct)=>
        {
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleAsync(ct);
            await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            var request=await db.Set<AgencyStateRequest>().SingleOrDefaultAsync(x=>x.Id==requestId&&x.AgencyId==agencyId&&x.Kind=="suspension",ct)??throw new AgencyCommandException(404,"suspension-request-not-found");
            if(request.RequestedBy==actor.UserId)throw new AgencyCommandException(403,"independent-reviewer-required");
            if(!CryptographicOperations.FixedTimeEquals(request.RowVersion,expected))throw new AgencyCommandException(412,"stale-suspension-request");
            if(request.State!="pending")throw new AgencyCommandException(409,"suspension-already-decided");
            if(approve)
            {
                if(agency.State!="active")throw new AgencyCommandException(409,"agency-not-active");
                if(!CryptographicOperations.FixedTimeEquals(request.BaseVersion,agency.RowVersion)||Fingerprint(agency)!=request.ProposedInputFingerprint)throw new AgencyCommandException(409,"suspension-stale-base");
            }
            var now=time.GetUtcNow();request.State=approve?"applied":"rejected";request.DecisionBy=actor.UserId;request.DecisionReason=reason;request.DecidedAt=now;
            await db.SaveChangesAsync(ct);
            if(approve)
            {
                // Preserve each user's individual status and credentials. Agency suspension
                // denies effective access; later reactivation must not undo individual deactivation.
                var users=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(UPDLOCK,HOLDLOCK) WHERE AgencyId={agencyId} ORDER BY Id").ToListAsync(ct);
                foreach(var user in users)
                {
                    user.SecurityStamp=Guid.NewGuid().ToString("N");user.UpdatedAt=now;
                    var invitations=await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==agencyId&&x.UserId==user.Id&&(x.State=="staged"||x.State=="pending")).ToListAsync(ct);
                    foreach(var invitation in invitations){invitation.State="revoked";invitation.RevokedAt=now;invitation.UpdatedAt=now;}
                    await db.Set<UserSession>().Where(x=>x.UserId==user.Id&&x.RevokedAt==null).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.RevokedAt,now),ct);
                }
                agency.State="suspended";agency.UpdatedAt=now;db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
            }
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,Action=approve?"agency.suspended":"agency.suspension-rejected",OccurredAt=now,CreatedAt=now,CreatedBy=actor.UserId});
            await db.SaveChangesAsync(ct);return Receipt(request,200);
        },token);
    }
    private static string Fingerprint(Agency agency)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{agency.Id,agency.State,agency.RowVersion,requestedState="suspended"}))).ToLowerInvariant();
    private static string Reason(string reason)
    {if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");return reason.Trim();}
    private static CommandOutcome Receipt(AgencyStateRequest request,int status)=>new(request.Id,status,JsonSerializer.Serialize(new{id=request.Id}),Etag:AgencyDraftService.Etag(request.RowVersion));
}
