using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyActivationDecisions(AgencyDraftService agencies,AgencyActivationService assessment,SqlCommandBoundary commands,InvitationService invitations,AgencyNotificationService notifications,TimeProvider time)
{
    public async Task<CommandOutcome> Decide(ActorContext actor,Guid agencyId,Guid requestId,string key,byte[] expected,bool approve,string reason,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");reason=reason.Trim();
        await agencies.Authorize(actor,agencyId,token);
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agency-state-requests/{requestId}/decision",key,Guid.NewGuid()),new{agencyId,approve,reason},"agency.activation-decided",async(db,ct)=>
        {
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleAsync(ct);
            await AgencyApprovalLocks.Authority(db,actor.UserId,ct);
            var request=await db.Set<AgencyStateRequest>().SingleOrDefaultAsync(x=>x.Id==requestId&&x.AgencyId==agencyId&&x.Kind=="activation",ct)??throw new AgencyCommandException(404,"activation-request-not-found");
            if(request.RequestedBy==actor.UserId)throw new AgencyCommandException(403,"independent-reviewer-required");
            if(!CryptographicOperations.FixedTimeEquals(request.RowVersion,expected))throw new AgencyCommandException(412,"stale-activation-request");
            if(request.State!="pending")throw new AgencyCommandException(409,"activation-already-decided");
            if(approve)
            {
                if(agency.State!="draft")throw new AgencyCommandException(409,"agency-not-draft");
                if(!CryptographicOperations.FixedTimeEquals(request.BaseVersion,agency.RowVersion))throw new AgencyCommandException(409,"activation-stale-base");
                if(await assessment.Fingerprint(db,agency,ct)!=request.ProposedInputFingerprint)throw new AgencyCommandException(409,"activation-stale-prerequisites");
                if(await db.Set<AgencyTermsVersion>().AnyAsync(x=>x.AgencyId==agencyId,ct))throw new AgencyCommandException(409,"initial-terms-already-exist");
            }
            var now=time.GetUtcNow();request.State=approve?"applied":"rejected";request.DecisionBy=actor.UserId;request.DecisionReason=reason;request.DecidedAt=now;
            await db.SaveChangesAsync(ct);
            if(approve)await Apply(db,agency,request,actor.UserId,now,ct);
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,Action=approve?"agency.activated":"agency.activation-rejected",OccurredAt=now,CreatedAt=now,CreatedBy=actor.UserId});await db.SaveChangesAsync(ct);
            return new(request.Id,200,JsonSerializer.Serialize(new{id=request.Id}),Etag:AgencyDraftService.Etag(request.RowVersion));
        },token);
    }
    private async Task Apply(BackOfficeDbContext db,Agency agency,AgencyStateRequest request,Guid actor,DateTimeOffset now,CancellationToken token)
    {
        var settings=await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope IN (N'agency-activation-delivery',N'agency-invitation-delivery')").AsNoTracking().ToListAsync(token);
        var delivery=settings.Where(x=>x.Scope=="agency-activation-delivery"&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefault()??throw new AgencyCommandException(503,"activation-delivery-unavailable");
        AgencyNotificationWorker.Scenario(delivery.Values);
        using var document=JsonDocument.Parse(await db.Set<AgencyOnboarding>().Where(x=>x.AgencyId==agency.Id).Select(x=>x.Details).SingleAsync(token));
        var products=await db.Set<AgencyDraftProduct>().Where(x=>x.AgencyId==agency.Id).Select(x=>new AgencyProductInput(x.ProductVersionId,x.EffectiveFrom,x.BrokerCommissionBasisPoints)).ToListAsync(token);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var terms=AgencyTermsRules.ExtractInitial(document.RootElement,products,today,request.RequestReason);
        agency.State="active";agency.UpdatedAt=now;db.Entry(agency).Property(x=>x.UpdatedAt).IsModified=true;
        db.Add(new AgencyTermsVersion{AgencyId=agency.Id,Version=1,EffectiveFrom=terms.EffectiveFrom,ApprovedStateRequestId=request.Id,Snapshot=terms.SnapshotJson,CreatedBy=actor,CreatedAt=now});
        await db.SaveChangesAsync(token);
        var staged=await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==agency.Id&&x.State=="staged").OrderBy(x=>x.UserId).ThenBy(x=>x.Id).Select(x=>x.Id).ToListAsync(token);
        if(staged.Count>0)
        {
            var scenario=await invitations.DefaultScenario(db,token);
            foreach(var invitation in staged)await invitations.IssueStaged(db,agency.Id,invitation,actor,scenario,token);
        }
        var recipient=document.RootElement.GetProperty("mainContact").GetProperty("email").GetString()!;
        await notifications.EnqueueActivation(db,agency.Id,request.Id,delivery.Id,actor,new(){Recipient=recipient,Template="agency-activated",Content=$"Fictional demo: agency {agency.Reference} activated after independent review. This notice does not confirm a finance ledger account."},token);
        var selected=products.Select(x=>x.ProductVersionId).ToArray();
        var providers=await(from product in db.Set<ProductVersion>() join provider in db.Set<CapacityProvider>() on product.ProviderId equals provider.Id where selected.Contains(product.Id) select new{provider.Id,provider.Name}).Distinct().ToListAsync(token);
        foreach(var provider in providers)
        {
            var operation=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"activation-provider:{request.Id:D}:{provider.Id:D}")).AsSpan(0,16));
            // Explicit fictional routing for the deterministic demo adapter.
            await notifications.EnqueueActivation(db,agency.Id,operation,delivery.Id,actor,new(){Recipient="capacity-demo@cover.example",Template="agency-activated",Content=$"Fictional capacity notice for {provider.Name}: agency {agency.Reference} activated for its agreed products."},token);
        }
        var pi=await db.Set<AgencyEvidence>().Where(x=>x.AgencyId==agency.Id&&x.Kind=="professional-indemnity").OrderByDescending(x=>x.Ordinal).FirstAsync(token);
        db.Add(new AgencyFollowUp{AgencyId=agency.Id,EvidenceId=pi.Id,Purpose="pi-expiry",DueOn=pi.ExpiresOn!.Value,CreatedBy=actor,CreatedAt=now});
        db.Add(new AgencyFollowUp{AgencyId=agency.Id,ActivationRequestId=request.Id,Purpose="quarter-review",DueOn=today.AddMonths(3),CreatedBy=actor,CreatedAt=now});await db.SaveChangesAsync(token);
    }
}
