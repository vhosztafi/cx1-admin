using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyEvidenceTests
{
    [Fact]
    public async Task RealSqlAgencyEvidencePreservesOwnedFilesImmutableAttemptsReplayAndRollback()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            ActorContext actor;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);Assert.False(db.Database.HasPendingModelChanges());
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(user.Id,user.TeamId,null,new HashSet<string>{"agency-admin"});
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=new FrozenClock();var boundary=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,boundary,clock);var service=new AgencyEvidenceService(boundary,drafts,clock);
            using var input=JsonDocument.Parse("""{"legalName":"Fictional Evidence Agency","regulatoryReference":"123456","compliance":{"dataProcessingAgreement":"signed"}}""");
            var created=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(input.RootElement),4,null,default);var id=created.ResourceId;
            var other=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(input.RootElement),4,null,default);
            var file=AgencyEvidenceRules.ValidateFile("fictional-evidence.txt","text/plain","Fictional signed agreement evidence"u8.ToArray());
            var uploadKey=Key();var uploaded=await service.Upload(actor,id,uploadKey,Version(created),file,default);
            var replay=await service.Upload(actor,id,uploadKey,Version(created),file,default);Assert.True(replay.Replayed);Assert.Equal(uploaded, replay with{Replayed=false});
            Assert.Single(JsonDocument.Parse(uploaded.Body).RootElement.EnumerateObject());
            var cross=await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Attest(actor,other.ResourceId,Key(),Version(other),"dpa",uploaded.ResourceId,"Fictional reviewed agreement",null,default));Assert.Equal(422,cross.Status);
            var attested=await service.Attest(actor,id,Key(),Version(uploaded),"dpa",uploaded.ResourceId,"Fictional reviewed agreement",null,default);
            var failed=await service.Check(actor,id,Key(),Version(attested),"fca",default);
            using var corrected=JsonDocument.Parse("""{"legalName":"Fictional Evidence Agency","regulatoryReference":"123456","regulatoryStatus":"directly-authorised","arrangesGeneralInsurance":"confirmed","compliance":{"dataProcessingAgreement":"signed"}}""");
            var saved=await drafts.Save(actor,id,Key(),Version(failed),AgencyDraftRules.Validate(corrected.RootElement),4,null,default);
            var checkKey=Key();var passed=await service.Check(actor,id,checkKey,Version(saved),"fca",default);
            Assert.True((await service.Check(actor,id,checkKey,Version(saved),"fca",default)).Replayed);
            await using(var db=new BackOfficeDbContext(options))
            {
                var storedFile=await db.Set<AgencyEvidenceFile>().SingleAsync();Assert.Equal(file.Content,storedFile.Content);Assert.Equal(file.Sha256,storedFile.Sha256);
                var rows=await db.Set<AgencyCheckAttempt>().Where(x=>x.AgencyId==id).OrderBy(x=>x.Ordinal).ToListAsync();Assert.Equal(new[]{"refer","passed"},rows.Select(x=>x.State));Assert.True(rows[0].Ordinal<rows[1].Ordinal);Assert.Equal(rows[0].CreatedAt,rows[1].CreatedAt);
                var old=await db.Set<AgencyEvidence>().SingleAsync(x=>x.Id==rows[0].EvidenceId);Assert.NotEqual(old.InputFingerprint,AgencyEvidenceRules.Fingerprint(corrected.RootElement,"fca"));
                Assert.Equal("verified",(await db.Set<AgencyEvidence>().SingleAsync(x=>x.Id==attested.ResourceId)).State);
                Assert.Equal(3,await db.Set<AgencyEvidence>().CountAsync());Assert.Equal(other.Etag,AgencyDraftService.Etag((await db.Set<Agency>().SingleAsync(x=>x.Id==other.ResourceId)).RowVersion));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [AgencyEvidenceFile] SET [FileName]=N'changed.txt' WHERE [Id]={uploaded.ResourceId}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AgencyEvidence] WHERE [Id]={attested.ResourceId}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AgencyCheckAttempt] WHERE [Id]={failed.ResourceId}"));
                var rule=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-compliance"&&x.Version==1);
                var noNotes=new AgencyEvidence{AgencyId=id,Kind="dpa",State="verified",InputFingerprint=new string('a',64),RuleVersionId=rule.Id,FileId=uploaded.ResourceId,AttestedBy=actor.UserId,VerifiedAt=clock.GetUtcNow(),ResultCode="demo-attested"};db.Add(noNotes);var missingNotes=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Contains("CK_AgencyEvidence_Attestation",missingNotes.InnerException!.Message);db.ChangeTracker.Clear();
                var foreign=new AgencyEvidence{AgencyId=other.ResourceId,Kind="dpa",State="verified",InputFingerprint=new string('a',64),RuleVersionId=rule.Id,FileId=uploaded.ResourceId,AttestedBy=actor.UserId,VerifiedAt=clock.GetUtcNow(),Notes="Fictional wrong-owner proof",ResultCode="demo-attested"};db.Add(foreign);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                await DemoDatabase.SeedAsync(db,password);Assert.Equal(1,await db.Set<AgencyEvidenceFile>().CountAsync());Assert.Equal(3,await db.Set<AgencyEvidence>().CountAsync());
            }
            var stale=await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Check(actor,id,Key(),Version(saved),"sanctions",default));Assert.Equal(412,stale.Status);
            var denied=await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Upload(actor with{Roles=new HashSet<string>{"underwriter"}},id,uploadKey,Version(created),file,default));Assert.Equal(403,denied.Status);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(passed.Etag,AgencyDraftService.Etag((await db.Set<Agency>().SingleAsync(x=>x.Id==id)).RowVersion));
                var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==actor.UserId&&x.RoleId==role.Id));await db.SaveChangesAsync();
            }
            Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Check(actor,id,checkKey,Version(saved),"fca",default))).Status);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    private static string Key()=>Guid.NewGuid().ToString("N");
    private static byte[] Version(CommandOutcome result)=>Convert.FromBase64String(result.Etag!.Trim('"'));
    private sealed class FrozenClock:TimeProvider{private readonly DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;}
}
