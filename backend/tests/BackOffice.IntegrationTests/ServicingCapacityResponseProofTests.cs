using System.Text;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<string> VerifyServicingCapacityResponseProof(BackOfficeDbContext db,DecisionFixture f,
        ServicingCapacityCase capacity,Guid submission,Guid lease,string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var service=new ServicingEvidenceService(f.Factory,f.Clock);
        var requirements=await service.RequirementsAsync(f.Underwriter,capacity.DraftId);
        var purpose=Assert.Single(requirements.Requirements,x=>x.Requirement.Code=="capacity-response").Requirement;
        Assert.Equal(submission,purpose.CapacitySubmissionId);
        var upload=await service.UploadAsync(f.Underwriter,capacity.DraftId,Version(etag),lease,"fictional-carrier-response.txt","text/plain",
            Encoding.UTF8.GetBytes("Fictional carrier response to this exact retained submission"),Key(),Guid.NewGuid());
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.AttachAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,
            Version(upload.Etag!),lease,upload.ResourceId,purpose.Code,null,new string('c',64),"Attach wrong submission fingerprint",Key(),Guid.NewGuid()))).Status);
        var attached=await service.AttachAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,Version(upload.Etag!),lease,
            upload.ResourceId,purpose.Code,null,purpose.InputFingerprint,"Attach exact fictional carrier reply",Key(),Guid.NewGuid());
        var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        Assert.Equal(submission,await db.Database.SqlQuery<Guid>($"SELECT CapacitySubmissionId AS Value FROM ServicingEvidenceAssociation WHERE Id={association.Id}").SingleAsync());
        var previous=await db.Set<ServicingCapacitySubmission>().Where(x=>x.CaseId==capacity.Id && x.Id!=submission).Select(x=>x.Id).FirstAsync();
        Assert.Equal(51461,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingEvidenceAssociation SET CapacitySubmissionId={previous} WHERE Id={association.Id}"))).Number);
        async Task CopyTo(Guid target)=>await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingEvidenceAssociation (Id,DraftId,CycleId,RevisionId,RatingId,FileId,RequirementCode,RiskItemId,CapacitySubmissionId,InputFingerprint,Reason,CreatedBy,CreatedAt,UpdatedAt) SELECT {Guid.NewGuid()},DraftId,CycleId,RevisionId,RatingId,FileId,RequirementCode,RiskItemId,{target},InputFingerprint,Reason,CreatedBy,CreatedAt,UpdatedAt FROM ServicingEvidenceAssociation WHERE Id={association.Id}");
        Assert.Equal(51460,(await Assert.ThrowsAsync<SqlException>(()=>CopyTo(previous))).Number);
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>CopyTo(Guid.NewGuid()))).Number);
        var history=await service.AssociationsAsync(f.Underwriter,capacity.DraftId,capacity.CycleId);
        Assert.Equal(submission,history.Items.Single(x=>x.Id==association.Id).CapacitySubmissionId);
        Assert.False((await service.RequirementsAsync(f.Underwriter,capacity.DraftId)).Requirements.Single(x=>x.Requirement.InputFingerprint==purpose.InputFingerprint).Satisfied);
        var reviewed=await service.ReviewAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,association.Id,Version(attached.Etag!),lease,
            association.RowVersion,"accepted",purpose.InputFingerprint,"Review exact fictional carrier reply",Key(),Guid.NewGuid());
        Assert.True((await service.RequirementsAsync(f.Underwriter,capacity.DraftId)).Requirements.Single(x=>x.Requirement.InputFingerprint==purpose.InputFingerprint).Satisfied);
        Assert.Equal("queued",await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.State).SingleAsync());
        return reviewed.Etag!;
    }
}
