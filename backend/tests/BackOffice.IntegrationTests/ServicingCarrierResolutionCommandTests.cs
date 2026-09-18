using System.Text;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCarrierResolutionCommand(BackOfficeDbContext db,DecisionFixture f,
        ServicingCapacityCase capacity,Guid conditionId,Guid lease)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var service=new ServicingCapacityService(f.Factory,f.Clock);
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,capacity.DraftId,conditionId));
        var purpose=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,capacity.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history").Requirement;
        var draftVersion=await db.Set<ServicingDraft>().Where(x=>x.Id==capacity.DraftId).Select(x=>x.RowVersion).SingleAsync();
        var upload=await evidence.UploadAsync(f.Underwriter,capacity.DraftId,draftVersion,lease,"fictional-trading.txt","text/plain",
            Encoding.UTF8.GetBytes("Fictional current trading history"),Key(),Guid.NewGuid());
        var attached=await evidence.AttachAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,Version(upload.Etag!),lease,upload.ResourceId,
            purpose.Code,null,purpose.InputFingerprint,"Attach fictional carrier condition proof",Key(),Guid.NewGuid());
        var conditionVersion=await db.Set<ServicingCapacityCondition>().Where(x=>x.Id==conditionId).Select(x=>x.RowVersion).SingleAsync();
        Task<BackOffice.Infrastructure.Platform.CommandOutcome> Resolve(string etag,Guid id,string key)=>service.ResolveConditionAsync(f.Underwriter,
            capacity.DraftId,capacity.CycleId,conditionId,Version(etag),conditionVersion,lease,id,"satisfied","Resolve fictional carrier condition",key,Guid.NewGuid());
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Resolve(attached.Etag!,Guid.NewGuid(),Key()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Resolve(attached.Etag!,attached.ResourceId,Key()))).Status);
        var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,association.Id,Version(attached.Etag!),lease,
            association.RowVersion,"accepted",purpose.InputFingerprint,"Review fictional carrier condition proof",Key(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,capacity.DraftId,conditionId));
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Resolve("\"AAAAAAAAAAA=\"",attached.ResourceId,Key()))).Status);
        var key=Key();var resolved=await Resolve(reviewed.Etag!,attached.ResourceId,key);
        Assert.Equal(200,resolved.Status);Assert.True((await Resolve(reviewed.Etag!,attached.ResourceId,key)).Replayed);
        Assert.True(await service.ConditionSatisfiedAsync(f.Underwriter,capacity.DraftId,conditionId));
        association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        await evidence.WithdrawAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,association.Id,Version(resolved.Etag!),lease,
            association.RowVersion,"Withdraw fictional carrier condition proof",Key(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,capacity.DraftId,conditionId));
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Resolve(reviewed.Etag!,attached.ResourceId,key))).Status);
    }
}
