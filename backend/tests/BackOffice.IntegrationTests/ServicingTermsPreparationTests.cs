using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingTermsPreparation(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,Guid fence,string etag,string deliveryScenario="success",string? httpPassword=null,bool signatureCondition=false,bool posting=false,bool issue=false,Func<ServicingAcceptance,string,Task>? onAccepted=null,
        Func<BackOfficeDbContext,DecisionFixture,ServicingCycle,ServicingTermsVersion,Task>? onPrepared=null)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var terms=new ServicingTermsService(f.Factory,f.Clock);var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        var now=f.Clock.GetUtcNow();
        var template=new TemplateVersion{Code="fictional-servicing-prepare",Kind="servicing-terms",Version=1,ProductId=cycle.ProductId,
            EffectiveFrom=now.AddDays(-1),EffectiveTo=now.AddYears(1),ContentJson="{\"format\":\"servicing-template-1\",\"title\":\"Fictional servicing terms\",\"notice\":\"Fictional retained risk and price\"}"};
        db.Add(template);await db.SaveChangesAsync();
        var missing=await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.PrepareAsync(f.Servicing,cycle.DraftId,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,Version(etag),fence,Key(),Guid.NewGuid()));
        Assert.Equal("servicing-proof-review-required",missing.Code);
        var takeover=await new ServicingDraftService(f.Factory,f.Clock).LeaseAsync(f.Underwriter,cycle.DraftId,Version(etag),"takeover",null,"Review servicing contract evidence",Key(),Guid.NewGuid());
        fence=JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();etag=takeover.Etag!;
        var uploaded=await evidence.UploadAsync(f.Underwriter,cycle.DraftId,Version(etag),fence,"contract-proof.txt","text/plain",Encoding.UTF8.GetBytes("Fictional reviewed servicing contract evidence"),Key(),Guid.NewGuid());etag=uploaded.Etag!;
        foreach(var item in (await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements)
        {
            var r=item.Requirement;
            var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(etag),fence,uploaded.ResourceId,r.Code,r.RiskItemId,r.InputFingerprint,"Attach fictional current contract proof",Key(),Guid.NewGuid());
            var a=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
            var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,a.Id,Version(attached.Etag!),fence,a.RowVersion,"accepted",r.InputFingerprint,"Review fictional current contract proof",Key(),Guid.NewGuid());etag=reviewed.Etag!;
        }
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.PrepareAsync(f.Underwriter,cycle.DraftId,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,new byte[8],fence,Key(),Guid.NewGuid()))).Status);
        var original=Version(etag);var key=Key();
        var prepared=await terms.PrepareAsync(f.Underwriter,cycle.DraftId,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,original,fence,key,Guid.NewGuid());
        Assert.Equal(201,prepared.Status);
        Assert.True((await terms.PrepareAsync(f.Underwriter,cycle.DraftId,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,original,fence,key,Guid.NewGuid())).Replayed);
        var stored=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==prepared.ResourceId);
        using var payload=JsonDocument.Parse(stored.TermsJson);
        Assert.Equal(2,payload.RootElement.GetProperty("slices").GetArrayLength());
        Assert.Equal(cycle.BaseVersionId,payload.RootElement.GetProperty("baseVersionId").GetGuid());
        Assert.True(JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(cycle.InputJson),payload.RootElement.GetProperty("ratingInput")));
        Assert.Equal(stored.Id,(await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==cycle.Id)).CurrentTermsVersionId);
        var purposes=(await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements;
        Assert.Equal(stored.Id,Assert.Single(purposes,x=>x.Requirement.Code=="signed-statement").Requirement.TermsVersionId);
        Assert.Equal(stored.Id,Assert.Single(purposes,x=>x.Requirement.Code=="acceptance-proof").Requirement.TermsVersionId);
        if(onPrepared is not null){await onPrepared(db,f,cycle,stored);return;}
        var queuedEtag=await VerifyServicingDeliveryQueue(db,f,cycle,stored,uploaded.ResourceId,fence,prepared.Etag!,deliveryScenario);
        if(deliveryScenario!="success")return;
        var reused=await terms.PrepareAsync(f.Underwriter,cycle.DraftId,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,Version(queuedEtag),fence,Key(),Guid.NewGuid());
        Assert.Equal(stored.Id,reused.ResourceId);Assert.Equal(1,await db.Set<ServicingTermsVersion>().CountAsync());
        var acceptedEtag=await VerifyServicingAcceptance(db,f,cycle,stored,uploaded.ResourceId,fence,reused.Etag!,posting,issue,httpPassword,onAccepted);
        if(posting || issue || onAccepted is not null)return;
        if(httpPassword is not null)
        {await VerifyServicingTermsHttp(db,f,httpPassword,cycle,stored,uploaded.ResourceId,fence,acceptedEtag);return;}
        if(signatureCondition){await VerifyServicingSignedCondition(db,f,cycle,stored,fence,acceptedEtag);return;}
        var proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().FirstAsync(x=>x.CycleId==cycle.Id && x.RequirementCode!="signed-statement" && x.RequirementCode!="acceptance-proof");
        await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,proof.Id,Version(acceptedEtag),fence,proof.RowVersion,"Withdraw fictional contract evidence",Key(),Guid.NewGuid());
        Assert.Equal("servicing-proof-review-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.PrepareAsync(f.Underwriter,cycle.DraftId,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,original,fence,key,Guid.NewGuid()))).Code);
    }
}
