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
    private static async Task VerifyServicingEvidenceReview(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,Guid sibling,Guid fence,string etag)
    {
        var service=new ServicingEvidenceService(f.Factory,f.Clock);var drafts=new ServicingDraftService(f.Factory,f.Clock);
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var before=await service.RequirementsAsync(f.Servicing,cycle.DraftId);
        Assert.True(before.Applicable);Assert.All(before.Requirements,x=>Assert.False(x.Satisfied));
        var requirement=before.Requirements.First(x=>x.Requirement.Code=="driving-record").Requirement;
        Assert.Equal(2,requirement.EffectiveDates.Count);
        var upload=await service.UploadAsync(f.Servicing,cycle.DraftId,Version(etag),fence,"proof.txt","text/plain",Encoding.UTF8.GetBytes("Fictional servicing proof"),Key(),Guid.NewGuid());
        var draftVersion=Version(upload.Etag!);
        var ownFile=await db.Set<ServicingEvidenceFile>().AsNoTracking().SingleAsync(x=>x.Id==upload.ResourceId);
        var foreignFile=new ServicingEvidenceFile {DraftId=sibling,FileName=ownFile.FileName,ContentType=ownFile.ContentType,Content=ownFile.Content,
            ByteLength=ownFile.ByteLength,Sha256=ownFile.Sha256,CreatedBy=f.Servicing.UserId,CreatedAt=f.Clock.GetUtcNow()};
        db.Add(foreignFile);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.AttachAsync(f.Servicing,cycle.DraftId,cycle.Id,draftVersion,fence,foreignFile.Id,requirement.Code,requirement.RiskItemId,requirement.InputFingerprint,"Wrong draft file proof",Key(),Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.AttachAsync(f.Servicing,sibling,cycle.Id,draftVersion,fence,upload.ResourceId,requirement.Code,requirement.RiskItemId,requirement.InputFingerprint,"Wrong servicing draft",Key(),Guid.NewGuid()))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.AttachAsync(f.Servicing,cycle.DraftId,cycle.Id,draftVersion,fence,upload.ResourceId,requirement.Code,Guid.NewGuid(),requirement.InputFingerprint,"Wrong stable item proof",Key(),Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.AttachAsync(f.Servicing,cycle.DraftId,cycle.Id,draftVersion,fence,upload.ResourceId,requirement.Code,requirement.RiskItemId,new string('b',64),"Wrong proof fingerprint",Key(),Guid.NewGuid()))).Status);
        var attached=await service.AttachAsync(f.Servicing,cycle.DraftId,cycle.Id,draftVersion,fence,upload.ResourceId,requirement.Code,requirement.RiskItemId,requirement.InputFingerprint,"Attach current driver proof",Key(),Guid.NewGuid());
        Assert.All((await service.RequirementsAsync(f.Servicing,cycle.DraftId)).Requirements,x=>Assert.False(x.Satisfied));
        var row=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewAsync(f.Servicing,cycle.DraftId,cycle.Id,row.Id,Version(attached.Etag!),fence,row.RowVersion,"accepted",requirement.InputFingerprint,"Unauthorized proof review",Key(),Guid.NewGuid()))).Status);
        var takeover=await drafts.LeaseAsync(f.Underwriter,cycle.DraftId,Version(attached.Etag!),"takeover",null,"Review current driver evidence",Key(),Guid.NewGuid());
        var reviewFence=JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var reviewVersion=Version(takeover.Etag!);var reviewKey=Key();
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,reviewVersion,reviewFence,new byte[8],"accepted",requirement.InputFingerprint,"Stale evidence version",Key(),Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,reviewVersion,reviewFence,row.RowVersion,"accepted",new string('b',64),"Wrong proof fingerprint",Key(),Guid.NewGuid()))).Status);
        var reviewed=await service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,reviewVersion,reviewFence,row.RowVersion,"accepted",requirement.InputFingerprint,"Reviewed fictional proof",reviewKey,Guid.NewGuid());
        Assert.True((await service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,reviewVersion,reviewFence,row.RowVersion,"accepted",requirement.InputFingerprint,"Reviewed fictional proof",reviewKey,Guid.NewGuid())).Replayed);
        var after=await service.RequirementsAsync(f.Servicing,cycle.DraftId);
        Assert.True(Assert.Single(after.Requirements,x=>x.Requirement.Code==requirement.Code && x.Requirement.RiskItemId==requirement.RiskItemId).Satisfied);
        Assert.Contains(after.Requirements,x=>!x.Satisfied);
        var now=f.Clock.Current;f.Clock.Current=(await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId)).ExpiresAt;
        var expired=await service.RequirementsAsync(f.Servicing,cycle.DraftId);Assert.False(expired.Applicable);Assert.All(expired.Requirements,x=>Assert.False(x.Satisfied));f.Clock.Current=now;
        var fresh=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==row.Id);
        var rejected=await service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,Version(reviewed.Etag!),reviewFence,fresh.RowVersion,"rejected",requirement.InputFingerprint,"Reject corrected fictional proof",Key(),Guid.NewGuid());
        Assert.All((await service.RequirementsAsync(f.Servicing,cycle.DraftId)).Requirements,x=>Assert.False(x.Satisfied));
        fresh=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==row.Id);
        var acceptedAgain=await service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,Version(rejected.Etag!),reviewFence,fresh.RowVersion,"accepted",requirement.InputFingerprint,"Accept independently rechecked proof",Key(),Guid.NewGuid());
        Assert.True(Assert.Single((await service.RequirementsAsync(f.Servicing,cycle.DraftId)).Requirements,x=>x.Requirement.Code==requirement.Code && x.Requirement.RiskItemId==requirement.RiskItemId).Satisfied);
        fresh=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==row.Id);
        var withdrawn=await service.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,Version(acceptedAgain.Etag!),reviewFence,fresh.RowVersion,"Withdraw corrected fictional proof",Key(),Guid.NewGuid());
        Assert.All((await service.RequirementsAsync(f.Servicing,cycle.DraftId)).Requirements,x=>Assert.False(x.Satisfied));
        fresh=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==row.Id);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,Version(withdrawn.Etag!),reviewFence,fresh.RowVersion,"accepted",requirement.InputFingerprint,"Cannot reactivate proof",Key(),Guid.NewGuid()))).Status);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,row.Id,reviewVersion,reviewFence,row.RowVersion,"accepted",requirement.InputFingerprint,"Reviewed fictional proof",reviewKey,Guid.NewGuid()))).Status);
        Assert.Equal(4,await db.Set<ServicingEvidenceEvent>().CountAsync(x=>x.AssociationId==row.Id));
    }
}
