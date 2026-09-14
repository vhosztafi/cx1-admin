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
            var agency=await AgencyDraftService.Lock(db,agencyId,expected,ct);await Authority(db,actor.UserId,ct);State(agency);
            var latest=await Latest(db,agencyId,ct);var now=time.GetUtcNow();AgencyTermsRules.ValidateSchedule(terms.EffectiveFrom,Today(now),latest.EffectiveFrom);
            var fingerprint=await Eligibility(db,terms,now,ct);
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
            await Authority(db,actor.UserId,ct);
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
                if(await Eligibility(db,terms,now,ct)!=request.ProposedInputFingerprint)throw new AgencyCommandException(409,"agency-terms-stale-rule");
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
    private static async Task Authority(BackOfficeDbContext db,Guid actor,CancellationToken token)
    {
        // Hold current identity and role membership until commit, including revocation races.
        var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH(HOLDLOCK) WHERE Id={actor}").SingleOrDefaultAsync(token);
        var links=await db.Set<UserRole>().FromSqlInterpolated($"SELECT * FROM UserRole WITH(HOLDLOCK) WHERE UserId={actor}").ToListAsync(token);
        var allowed=false;
        foreach(var link in links.OrderBy(x=>x.RoleId))
        {
            var role=await db.Set<Role>().FromSqlInterpolated($"SELECT * FROM Role WITH(HOLDLOCK) WHERE Id={link.RoleId}").SingleAsync(token);
            allowed|=role.Scope=="internal"&&role.Code is "agency-admin" or "system-admin";
        }
        if(user is null||user.State!="active"||user.AgencyId is not null||!allowed)throw new AgencyCommandException(403,"agency-access-denied");
    }
    private static async Task<string> Eligibility(BackOfficeDbContext db,ValidatedAgencyTerms terms,DateTimeOffset now,CancellationToken token)
    {
        // Range lock includes future and newly inserted rule versions. Publication
        // must not race configuration withdrawal after the final eligibility check.
        var settings=await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'agency-distribution'").AsNoTracking().ToListAsync(token);
        var setting=settings.Where(x=>x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefault();
        var eligible=setting is null?null:AgencyDistributionRules.Parse(setting.Values);
        if(eligible is null)throw new AgencyCommandException(503,"agency-distribution-unavailable");
        var families=new HashSet<Guid>();
        foreach(var selected in terms.Products.OrderBy(x=>x.ProductVersionId))
        {
            if(!eligible.Contains(selected.ProductVersionId))throw new AgencyCommandException(422,"agency-product-ineligible");
            var version=await db.Set<ProductVersion>().FromSqlInterpolated($"SELECT * FROM ProductVersion WITH(HOLDLOCK) WHERE Id={selected.ProductVersionId}").AsNoTracking().SingleOrDefaultAsync(token)??throw new AgencyCommandException(422,"agency-product-ineligible");
            var product=await db.Set<Product>().FromSqlInterpolated($"SELECT * FROM Product WITH(HOLDLOCK) WHERE Id={version.ProductId}").AsNoTracking().SingleAsync(token);
            var provider=await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={version.ProviderId}").AsNoTracking().SingleAsync(token);
            var local=selected.EffectiveFrom.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified);
            var effective=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")));
            if(effective<now)effective=now;
            if(!families.Add(product.Id)||product.Code is not ("motor-trade-road-risks" or "motor-trade-combined" or "commercial-combined")||provider.State!="active"||version.EffectiveFrom>effective||version.EffectiveTo is DateTimeOffset end&&end<=effective)throw new AgencyCommandException(422,"agency-product-ineligible");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(setting!.Id.ToString("D")+":"+terms.Fingerprint))).ToLowerInvariant();
    }
    private static async Task<AgencyTermsVersion> Latest(BackOfficeDbContext db,Guid agency,CancellationToken token)=>await db.Set<AgencyTermsVersion>().AsNoTracking().Where(x=>x.AgencyId==agency).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token)??throw new AgencyCommandException(409,"agency-initial-terms-required");
    private static void State(Agency agency){if(agency.State is not ("active" or "suspended"))throw new AgencyCommandException(409,"agency-terms-state");}
    private static bool Equal(byte[] left,byte[] right)=>CryptographicOperations.FixedTimeEquals(left,right);
    private static DateOnly Today(DateTimeOffset now)=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
    private static CommandOutcome Receipt(AgencyTermsRequest request,int status)=>new(request.Id,status,JsonSerializer.Serialize(new{id=request.Id}),Etag:AgencyDraftService.Etag(request.RowVersion));
}
