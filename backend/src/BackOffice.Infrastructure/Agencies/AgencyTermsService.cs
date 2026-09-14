using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Agencies;

// Terms changes are separate from state activation so each command can be tested
// without exposing unfinished state transitions or public endpoints.
public sealed class AgencyTermsService(AgencyDraftService agencies,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Propose(ActorContext actor,Guid agencyId,string key,byte[] expected,JsonElement input,CancellationToken token=default)
    {
        // Structural normalization precedes replay, but time-dependent rules run
        // inside the handler so a successful command remains replayable tomorrow.
        var terms=AgencyTermsRules.ValidateProposal(input,DateOnly.MinValue,null);
        await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/terms-requests",key,Guid.NewGuid()),new{terms.SnapshotJson,terms.Reason},"agency.terms-requested",async(db,ct)=>
        {
            var agency=await AgencyDraftService.Lock(db,agencyId,expected,ct);await AgencyApprovalLocks.Authority(db,actor.UserId,ct);State(agency);
            var latest=await Latest(db,agencyId,ct);var now=time.GetUtcNow();AgencyTermsRules.ValidateSchedule(terms.EffectiveFrom,Today(now),latest.EffectiveFrom);
            var fingerprint=await AgencyApprovalLocks.Eligibility(db,terms,now,ct);
            var pending=await db.Set<AgencyTermsRequest>().SingleOrDefaultAsync(x=>x.AgencyId==agencyId&&x.State=="pending",ct);
            if(pending is not null)
            {
                if(Equal(pending.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"agency-terms-pending");
                pending.State="stale";pending.DecisionReason="Agency changed before a replacement proposal.";pending.DecidedAt=now;
                await db.SaveChangesAsync(ct);
            }
            var request=new AgencyTermsRequest{AgencyId=agencyId,BaseVersion=agency.RowVersion.ToArray(),EffectiveFrom=terms.EffectiveFrom,ProposedSnapshot=terms.SnapshotJson,ProposedInputFingerprint=fingerprint,RequestedBy=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,RequestReason=terms.Reason};
            db.Add(request);await db.SaveChangesAsync(ct);return Receipt(request,202);
        },token);
    }
    public async Task<CommandOutcome> Decide(ActorContext actor,Guid agencyId,Guid requestId,string key,byte[] expected,bool approve,string reason,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");reason=reason.Trim();
        await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agency-terms-requests/{requestId}/decision",key,Guid.NewGuid()),new{agencyId,approve,reason},"agency.terms-decided",async(db,ct)=>
        {
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleAsync(ct);
            await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            var request=await db.Set<AgencyTermsRequest>().SingleOrDefaultAsync(x=>x.Id==requestId&&x.AgencyId==agencyId,ct)??throw new AgencyCommandException(404,"agency-terms-request-not-found");
            if(request.RequestedBy==actor.UserId)throw new AgencyCommandException(403,"independent-reviewer-required");
            if(!Equal(request.RowVersion,expected))throw new AgencyCommandException(412,"stale-agency-terms-request");
            if(request.State!="pending")throw new AgencyCommandException(409,"agency-terms-already-decided");
            var now=time.GetUtcNow();AgencyTermsVersion? latest=null;
            if(approve)
            {
                State(agency);if(!Equal(request.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"agency-terms-stale-base");
                latest=await Latest(db,agencyId,ct);
                var input=JsonNode.Parse(request.ProposedSnapshot)!.AsObject();input["reason"]=request.RequestReason;
                using var document=JsonDocument.Parse(input.ToJsonString());var terms=AgencyTermsRules.ValidateProposal(document.RootElement,Today(now),latest.EffectiveFrom);
                if(await AgencyApprovalLocks.Eligibility(db,terms,now,ct)!=request.ProposedInputFingerprint)throw new AgencyCommandException(409,"agency-terms-stale-rule");
            }
            request.State=approve?"applied":"rejected";request.DecisionBy=actor.UserId;request.DecisionReason=reason;request.DecidedAt=now;
            await db.SaveChangesAsync(ct);
            if(approve)
            {
                // SQL creates the complete immutable product set with this parent.
                db.Add(new AgencyTermsVersion{AgencyId=agencyId,Version=latest!.Version+1,EffectiveFrom=request.EffectiveFrom,ApprovedTermsRequestId=request.Id,Snapshot=request.ProposedSnapshot,CreatedBy=actor.UserId,CreatedAt=now});
                agency.UpdatedAt=now;db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
            }
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,Action=approve?"agency.terms-applied":"agency.terms-rejected",OccurredAt=now,CreatedBy=actor.UserId,CreatedAt=now});
            await db.SaveChangesAsync(ct);return Receipt(request,200);
        },token);
    }
    private static async Task<AgencyTermsVersion> Latest(BackOfficeDbContext db,Guid agency,CancellationToken token)=>await db.Set<AgencyTermsVersion>().AsNoTracking().Where(x=>x.AgencyId==agency).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token)??throw new AgencyCommandException(409,"agency-initial-terms-required");
    private static void State(Agency agency){if(agency.State is not ("active" or "suspended"))throw new AgencyCommandException(409,"agency-terms-state");}
    private static bool Equal(byte[] left,byte[] right)=>CryptographicOperations.FixedTimeEquals(left,right);
    private static DateOnly Today(DateTimeOffset now)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
    private static CommandOutcome Receipt(AgencyTermsRequest request,int status)=>new(request.Id,status,JsonSerializer.Serialize(new{id=request.Id}),Etag:AgencyDraftService.Etag(request.RowVersion));
}
