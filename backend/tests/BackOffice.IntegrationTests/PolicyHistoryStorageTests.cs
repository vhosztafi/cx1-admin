using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlPolicyHistoryStorageRetainsExactReconstructionAndRejectsMutation()
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var request=new PolicyReconstructionRequest{PolicyId=version.PolicyId,TermId=version.TermId,VersionId=version.Id,VersionHash=version.ContentHash,
                EffectiveAt=version.EffectiveAt,KnownAt=f.Clock.GetUtcNow(),CoverageState="active",ActorId=f.Underwriter.UserId,CreatedBy=f.Underwriter.UserId,
                CreatedAt=f.Clock.GetUtcNow(),Reason="Retain exact fictional policy reconstruction"};
            request.ManifestJson=JsonSerializer.Serialize(new{format="policy-reconstruction-1",requestId=request.Id,policyId=request.PolicyId,termId=request.TermId,
                versionId=request.VersionId,contentHash=Convert.ToHexStringLower(version.ContentHash),effectiveAt=request.EffectiveAt,knownAt=request.KnownAt,coverageState=request.CoverageState});
            request.ManifestHash=SHA256.HashData(Encoding.UTF8.GetBytes(request.ManifestJson));
            var work=new OutboxWork{Kind="policy-reconstruction",OperationKey=$"policy-reconstruction/{request.Id:N}",SubjectRecordId=request.Id,
                Payload=request.ManifestJson,CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow(),NextAttemptAt=f.Clock.GetUtcNow()};
            db.Add(work);await db.SaveChangesAsync();request.WorkId=work.Id;
            db.Add(request);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicyReconstructionRequest SET Reason='Changed request reason' WHERE Id={request.Id}"));
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PolicyReconstructionRequest WHERE Id={request.Id}"));
            Assert.Equal(request.ManifestHash,await db.Set<PolicyReconstructionRequest>().Select(x=>x.ManifestHash).SingleAsync());
        });
    }
    [Fact]
    public async Task RealSqlPolicyHistoryStorageRejectsUnownedSourcesAndUnhashedRequests()
    {
        await WithDatabase(async(db,_)=>
        {
            db.Add(new PolicyReconstructionRequest{PolicyId=Guid.NewGuid(),TermId=Guid.NewGuid(),EffectiveAt=DateTimeOffset.UtcNow,KnownAt=DateTimeOffset.UtcNow,
                ManifestJson="{}",ManifestHash=new byte[32],ActorId=Guid.NewGuid(),CreatedBy=Guid.NewGuid(),Reason="Attempt an unowned reconstruction request",WorkId=Guid.NewGuid()});
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
            db.Add(new PolicyQuoteClone{PolicyId=Guid.NewGuid(),VersionId=Guid.NewGuid(),VersionHash=new byte[32],QuoteId=Guid.NewGuid(),RevisionId=Guid.NewGuid(),
                ActorId=Guid.NewGuid(),CreatedBy=Guid.NewGuid(),Reason="Attempt an unowned policy clone"});
            await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        });
    }
}
