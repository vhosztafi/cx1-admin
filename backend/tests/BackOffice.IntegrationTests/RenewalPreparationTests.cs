using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlRenewalPreparationExperienceCommandsEnforceLeaseExactRetryAndCurrentPermission()
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,"motor-trade-road-risks");var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var drafts=new ServicingDraftService(f.Factory,f.Clock);
            static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
            var created=await drafts.CreateAsync(f.Servicing,issued.TermId,Version((await drafts.ListAsync(f.Servicing,issued.TermId)).Etag),
                new("renewal",issued.Id,JsonSerializer.SerializeToElement(new{localDate="2027-10-01",localTime="01:00",timeZone="Europe/London"}),"Fictional renewal experience command"),Guid.NewGuid().ToString(),Guid.NewGuid());
            var leased=await drafts.LeaseAsync(f.Servicing,created.ResourceId,Version(created.Etag!),"acquire",null,null,Guid.NewGuid().ToString(),Guid.NewGuid());
            using var leaseBody=JsonDocument.Parse(leased.Body);var fence=leaseBody.RootElement.GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var service=new RenewalPreparationService(f.Factory,f.Clock);var bytes=Encoding.UTF8.GetBytes("Fictional renewal claims statement.");
            var uploadKey=Guid.NewGuid().ToString();
            var uploaded=await service.UploadExperienceAsync(f.Servicing,created.ResourceId,Version(leased.Etag!),fence,"claims.txt","text/plain",bytes,uploadKey,Guid.NewGuid());
            Assert.True((await service.UploadExperienceAsync(f.Servicing,created.ResourceId,Version(leased.Etag!),fence,"claims.txt","text/plain",bytes,uploadKey,Guid.NewGuid())).Replayed);
            var facts=new RenewalExperienceFacts(new(2025,9,16),new(2026,9,16),1,500.01m,0m,1000m,"agency","Fictional supplied statement",uploaded.ResourceId);
            var saveKey=Guid.NewGuid().ToString();
            Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveExperienceAsync(f.Servicing,created.ResourceId,Version(leased.Etag!),fence,facts,Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveExperienceAsync(f.Servicing,created.ResourceId,Version(uploaded.Etag!),Guid.NewGuid(),facts,Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveExperienceAsync(f.Servicing,created.ResourceId,Version(uploaded.Etag!),fence,facts with{EvidenceAssociationId=Guid.NewGuid()},Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            var saved=await service.SaveExperienceAsync(f.Servicing,created.ResourceId,Version(uploaded.Etag!),fence,facts,saveKey,Guid.NewGuid());
            Assert.True((await service.SaveExperienceAsync(f.Servicing,created.ResourceId,Version(uploaded.Etag!),fence,facts,saveKey,Guid.NewGuid())).Replayed);
            Assert.Single(await db.Set<RenewalExperienceVersion>().ToArrayAsync());
            var view=await service.ReadExperienceAsync(f.Servicing,created.ResourceId);
            Assert.Equal(saved.ResourceId,view.Experience!.Id);Assert.Null(view.Review);Assert.Equal(500.01m,view.Experience.Paid);
            var takeover=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,Version(saved.Etag!),"takeover",null,"Review the supplied renewal evidence",Guid.NewGuid().ToString(),Guid.NewGuid());
            using var takeoverBody=JsonDocument.Parse(takeover.Body);var reviewFence=takeoverBody.RootElement.GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var reviewKey=Guid.NewGuid().ToString();
            var reviewed=await service.ReviewExperienceAsync(f.Underwriter,created.ResourceId,saved.ResourceId,Version(takeover.Etag!),reviewFence,"accepted","Checked fictional statement against supplied figures",reviewKey,Guid.NewGuid());
            Assert.True((await service.ReviewExperienceAsync(f.Underwriter,created.ResourceId,saved.ResourceId,Version(takeover.Etag!),reviewFence,"accepted","Checked fictional statement against supplied figures",reviewKey,Guid.NewGuid())).Replayed);
            Assert.Equal("accepted",(await service.ReadExperienceAsync(f.Servicing,created.ResourceId)).Review!.Outcome);
            var changed=await service.SaveExperienceAsync(f.Underwriter,created.ResourceId,Version(reviewed.Etag!),reviewFence,facts with{Paid=600m},Guid.NewGuid().ToString(),Guid.NewGuid());
            Assert.Null((await service.ReadExperienceAsync(f.Servicing,created.ResourceId)).Review);
            Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewExperienceAsync(f.Underwriter,created.ResourceId,saved.ResourceId,Version(changed.Etag!),reviewFence,"accepted","Old figures must not be newly approved",Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional review authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReviewExperienceAsync(f.Underwriter,created.ResourceId,saved.ResourceId,Version(takeover.Etag!),reviewFence,"accepted","Checked fictional statement against supplied figures",reviewKey,Guid.NewGuid()))).Status);
            var role=await db.Set<Role>().SingleAsync(x=>x.Code=="servicing");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId} AND RoleId={role.Id}");db.ChangeTracker.Clear();
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveExperienceAsync(f.Servicing,created.ResourceId,Version(uploaded.Etag!),fence,facts,saveKey,Guid.NewGuid()))).Status);
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReadExperienceAsync(f.Servicing,created.ResourceId))).Status);
        });
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlRenewalPreparationStorageKeepsExactExperienceAndEvidenceOwnership(string product)
    {
        await WithDatabase(async (db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var drafts=new ServicingDraftService(f.Factory,f.Clock);
            async Task<Guid> Draft(string kind) => (await drafts.CreateAsync(f.Servicing,issued.TermId,
                Convert.FromBase64String((await drafts.ListAsync(f.Servicing,issued.TermId)).Etag.Trim('"')),
                new(kind,issued.Id,JsonSerializer.SerializeToElement(new{localDate="2027-10-01",localTime="01:00",timeZone="Europe/London"}),
                    "Fictional supplied experience storage"),Guid.NewGuid().ToString(),Guid.NewGuid())).ResourceId;
            var renewal=await Draft("renewal");var adjustment=await Draft("adjustment");
            var content=Encoding.UTF8.GetBytes("Fictional claims experience: paid GBP500, earned GBP1000.");var now=f.Clock.GetUtcNow();
            var file=new ServicingEvidenceFile{DraftId=renewal,FileName="experience.txt",ContentType="text/plain",Content=content,
                ByteLength=content.Length,Sha256=Convert.ToHexStringLower(SHA256.HashData(content)),CreatedBy=f.Servicing.UserId,CreatedAt=now};
            db.Add(file);await db.SaveChangesAsync();
            var association=new RenewalExperienceEvidence{DraftId=renewal,FileId=file.Id,CreatedBy=f.Servicing.UserId,CreatedAt=now};
            db.Add(association);await db.SaveChangesAsync();
            async Task Insert(Guid id,Guid draft,Guid evidence,int sequence=1,decimal paid=500m,decimal earned=1000m,int claims=1) =>
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT RenewalExperienceVersion (Id,DraftId,Sequence,ObservationStartsOn,ObservationEndsOn,ClaimCount,Paid,Outstanding,EarnedPremium,SourceCode,SourceReference,EvidenceAssociationId,CreatedBy,CreatedAt) VALUES ({id},{draft},{sequence},'2025-09-16','2026-09-16',{claims},{paid},0,{earned},'agency','Fictional statement',{evidence},{f.Servicing.UserId},{now})");
            var experience=Guid.NewGuid();await Insert(experience,renewal,association.Id);
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),adjustment,association.Id));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),renewal,Guid.NewGuid(),2));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),renewal,association.Id));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),renewal,association.Id,2,paid:-1m));
            await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),renewal,association.Id,2,claims:100001));
            var grant=await (from g in db.Set<UserAuthorityGrant>().AsNoTracking()
                join a in db.Set<AuthorityVersion>().AsNoTracking() on g.AuthorityVersionId equals a.Id
                join p in db.Set<Product>().AsNoTracking() on a.ProductId equals p.Id
                where g.UserId==f.Underwriter.UserId && g.RevokedAt==null && p.Code==product
                select g).FirstAsync();
            async Task Review(Guid actor,int sequence=1) => await db.Database.ExecuteSqlInterpolatedAsync($"INSERT RenewalExperienceReview (Id,DraftId,ExperienceVersionId,Sequence,Outcome,Reason,AuthorityVersionId,AuthorityGrantId,CreatedBy,CreatedAt) VALUES ({Guid.NewGuid()},{renewal},{experience},{sequence},'accepted','Checked fictional statement and supplied amounts',{grant.AuthorityVersionId},{grant.Id},{actor},{now})");
            await Assert.ThrowsAsync<SqlException>(()=>Review(f.Servicing.UserId));
            await Review(f.Underwriter.UserId);
            // Zero denominator is stored truthfully, then blocks a complete assessment.
            await Insert(Guid.NewGuid(),renewal,association.Id,2,paid:0m,earned:0m);
            await Assert.ThrowsAsync<SqlException>(()=>Review(f.Underwriter.UserId,2));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RenewalExperienceVersion SET Paid=0 WHERE Id={experience}"));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE RenewalExperienceEvidence WHERE Id={association.Id}"));
            Assert.Equal(2,await db.Set<RenewalExperienceVersion>().CountAsync());
            Assert.Single(await db.Set<RenewalExperienceReview>().ToArrayAsync());
            var authority=await db.Set<AuthorityVersion>().AsNoTracking().SingleAsync(x=>x.Id==grant.AuthorityVersionId);
            var productVersion=await db.Set<ProductVersion>().AsNoTracking().SingleAsync(x=>x.Id==authority.ProductVersionId);
            var binder=await db.Set<BinderVersion>().AsNoTracking().SingleAsync(x=>x.Id==authority.BinderVersionId);
            var productFile=new ProductEvidenceFileVersion{ProductId=authority.ProductId,ProductVersionId=authority.ProductVersionId,
                BinderVersionId=binder.Id,FileName="fictional-fair-value.txt",ContentType="text/plain",Content=content,
                ByteLength=content.Length,Sha256=file.Sha256,CreatedAt=now,CreatedBy=f.Underwriter.UserId};
            db.Add(productFile);await db.SaveChangesAsync();
            var validFrom=productVersion.EffectiveFrom>binder.EffectiveFrom?productVersion.EffectiveFrom:binder.EffectiveFrom;
            var validTo=productVersion.EffectiveTo is { } until && until<binder.EffectiveTo?until:binder.EffectiveTo;
            async Task Assessment(Guid fileId,string outcome="pass")=>await db.Database.ExecuteSqlInterpolatedAsync($"INSERT FairValueAssessmentVersion (Id,ProductId,ProductVersionId,BinderVersionId,EvidenceFileVersionId,ValidFrom,ValidTo,Outcome,ApprovedBy,ApprovedAt,Reason,CreatedAt,CreatedBy) VALUES ({Guid.NewGuid()},{authority.ProductId},{authority.ProductVersionId},{binder.Id},{fileId},{validFrom},{validTo},{outcome},{f.Underwriter.UserId},{now},'Fictional demonstration fair value assessment',{now},{f.Underwriter.UserId})");
            await Assessment(productFile.Id);
            await Assert.ThrowsAsync<SqlException>(()=>Assessment(file.Id));
            await Assert.ThrowsAsync<SqlException>(()=>Assessment(productFile.Id,"unknown"));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductEvidenceFileVersion SET FileName='rewritten.txt' WHERE Id={productFile.Id}"));
            await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FairValueAssessmentVersion"));
            Assert.Single(await db.Set<FairValueAssessmentVersion>().ToArrayAsync());
            Assert.Equal(issued.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
        });
    }
}
