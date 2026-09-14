using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
    [Fact]
    public async Task RealSqlAgencyEvidenceApiEnforcesUploadsReadinessOwnershipAndCurrentResults()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            await using(var db=new BackOfficeDbContext(options)){await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);}
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","evidence-api-keys",owned))));
            using var admin=host.CreateClient();var csrf=await SignIn(admin,"agency-admin",password);using var uw=host.CreateClient();var uwCsrf=await SignIn(uw,"underwriter",password);using var anonymous=host.CreateClient();
            var body=new{details=new{legalName="Fictional Evidence API",regulatoryReference="123456",compliance=new{dataProcessingAgreement="signed"}},onboardingStep=4};
            using var created=await Send(admin,csrf,"/api/v1/agencies",body);Assert.Equal(HttpStatusCode.Created,created.StatusCode);var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var path="/api/v1/agencies/"+id;var etag=created.Headers.ETag!.ToString();
            var initial=await Read(admin,path);Assert.False(initial.GetProperty("validation").GetProperty("valid").GetBoolean());Assert.Contains(initial.GetProperty("validation").GetProperty("items").EnumerateArray(),x=>x.GetProperty("code").GetString()=="evidence-fca"&&x.GetProperty("state").GetString()=="missing");
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,null,path+"/checks",new{kind="fca"},etag)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(uw,uwCsrf,path+"/checks",new{kind="fca"},etag)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,csrf,path+"/checks",new{kind="fca",verifiedAt="2026-09-14"},etag)).StatusCode);
            var fileKey=Key();using var uploaded=await Upload(admin,csrf,path,etag,fileKey,"proof.txt","text/plain","Fictional signed DPA"u8.ToArray());Assert.True(uploaded.StatusCode==HttpStatusCode.Created,await uploaded.Content.ReadAsStringAsync());var fileId=(await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();etag=uploaded.Headers.ETag!.ToString();
            using var replay=await Upload(admin,csrf,path,created.Headers.ETag!.ToString(),fileKey,"proof.txt","text/plain","Fictional signed DPA"u8.ToArray());Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Equal(fileId,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Upload(admin,csrf,path,etag,Key(),"proof.pdf","application/pdf","not a PDF"u8.ToArray())).StatusCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Upload(admin,csrf,path,etag,Key(),"proof.txt","text/plain",new byte[AgencyEvidenceRules.MaximumFileBytes+16384])).StatusCode);
            var files=await Read(admin,path+"/evidence-files?pageSize=1");Assert.Equal(1,files.GetProperty("totalCount").GetInt32());Assert.False(files.GetProperty("items")[0].TryGetProperty("content",out _));
            using var download=await uw.GetAsync(path+"/evidence-files/"+fileId+"/content");Assert.Equal(HttpStatusCode.OK,download.StatusCode);Assert.Equal("attachment",download.Content.Headers.ContentDisposition!.DispositionType);Assert.Equal("nosniff",download.Headers.GetValues("X-Content-Type-Options").Single());Assert.Equal("Fictional signed DPA",await download.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(path+"/evidence-files/"+fileId+"/content")).StatusCode);
            var fileRead=await Read(admin,path+"/evidence-files/"+fileId);Assert.Equal("proof.txt",fileRead.GetProperty("fileName").GetString());
            using var otherCreated=await Send(admin,csrf,"/api/v1/agencies",body);var otherId=(await otherCreated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var otherPath="/api/v1/agencies/"+otherId;
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync(otherPath+"/evidence-files/"+fileId+"/content")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(admin,csrf,otherPath+"/evidence",new{kind="dpa",fileId,notes="Fictional wrong-owner proof"},otherCreated.Headers.ETag!.ToString())).StatusCode);
            using var attestation=await Send(admin,csrf,path+"/evidence",new{kind="dpa",fileId,notes="Fictional signed document reviewed"},etag);Assert.Equal(HttpStatusCode.Created,attestation.StatusCode);etag=attestation.Headers.ETag!.ToString();
            using var check=await Send(admin,csrf,path+"/checks",new{kind="fca"},etag);Assert.Equal(HttpStatusCode.Accepted,check.StatusCode);etag=check.Headers.ETag!.ToString();var checkId=(await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.Equal("refer",(await Read(admin,path+"/checks/"+checkId)).GetProperty("state").GetString());
            using var validate=await Send(admin,csrf,path+"/validate",null,etag);Assert.Equal(HttpStatusCode.OK,validate.StatusCode);var readiness=await validate.Content.ReadFromJsonAsync<JsonElement>();Assert.False(readiness.GetProperty("valid").GetBoolean());Assert.Contains(readiness.GetProperty("items").EnumerateArray(),x=>x.GetProperty("code").GetString()=="evidence-dpa"&&x.GetProperty("state").GetString()=="satisfied");
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(admin,csrf,path+"/validate",null,created.Headers.ETag!.ToString())).StatusCode);
            using var update=new HttpRequestMessage(HttpMethod.Put,path){Content=JsonContent.Create(new{details=new{legalName="Changed Fictional Evidence API",regulatoryReference="123456",regulatoryStatus="directly-authorised",arrangesGeneralInsurance="confirmed",compliance=new{dataProcessingAgreement="signed"}},onboardingStep=4})};Headers(update,csrf,etag,Key());using var updated=await admin.SendAsync(update);Assert.Equal(HttpStatusCode.OK,updated.StatusCode);etag=updated.Headers.ETag!.ToString();
            var stale=await Read(admin,path);Assert.Contains(stale.GetProperty("validation").GetProperty("items").EnumerateArray(),x=>x.GetProperty("code").GetString()=="evidence-dpa"&&x.GetProperty("state").GetString()=="stale");
            using var rerun=await Send(admin,csrf,path+"/checks",new{kind="fca"},etag);Assert.Equal(HttpStatusCode.Accepted,rerun.StatusCode);etag=rerun.Headers.ETag!.ToString();var results=await Read(admin,path+"/checks?pageSize=1");Assert.Equal(2,results.GetProperty("totalCount").GetInt32());var cursor=results.GetProperty("nextCursor").GetString()!;
            Assert.Equal(HttpStatusCode.BadRequest,(await uw.GetAsync(path+"/checks?pageSize=1&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
            var passed=await Read(admin,path);Assert.Contains(passed.GetProperty("validation").GetProperty("items").EnumerateArray(),x=>x.GetProperty("code").GetString()=="evidence-fca"&&x.GetProperty("state").GetString()=="satisfied");
            await using(var db=new BackOfficeDbContext(options)){var rule=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-compliance"&&x.Version==1);db.Add(new SettingVersion{Scope=rule.Scope,Version=2,EffectiveFrom=rule.EffectiveFrom,Values=rule.Values.Replace("\"fca\":\"pass\"","\"fca\":\"unavailable\"")});await db.SaveChangesAsync();}
            using var unavailable=await Send(admin,csrf,path+"/checks",new{kind="fca"},etag);Assert.Equal(HttpStatusCode.Accepted,unavailable.StatusCode);var latest=await Read(admin,path);Assert.Contains(latest.GetProperty("validation").GetProperty("items").EnumerateArray(),x=>x.GetProperty("code").GetString()=="evidence-fca"&&x.GetProperty("state").GetString()=="unavailable");
            Assert.Equal(3,(await Read(admin,path+"/checks")).GetProperty("totalCount").GetInt32());
            var activity=await Read(admin,path+"/activity");Assert.Contains(activity.GetProperty("items").EnumerateArray(),x=>x.GetProperty("summary").GetString()=="Demo compliance check completed.");Assert.Contains(activity.GetProperty("items").EnumerateArray(),x=>x.GetProperty("summary").GetString()=="Evidence attestation recorded.");
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    [Fact]
    public async Task RealSqlAgencyEvidenceReadinessRechecksExpiryAndRuleWithStoredProof()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext actor;await using(var db=new BackOfficeDbContext(options)){await db.Database.MigrateAsync();db.Add(new SettingVersion{Scope="agency-compliance",Version=1,EffectiveFrom=new(2026,9,1,0,0,0,TimeSpan.Zero),Values="""{"demo":true,"minimumPi":"1300000.00","tobaVersion":"2026.1","checkValidityDays":90,"scenarios":{"fca":"pass","financial-check":"pass","sanctions":"pass","ownership":"pass"}}"""});await db.SaveChangesAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(user.Id,user.TeamId,null,new HashSet<string>{"agency-admin"});}
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=new FrozenClock();var boundary=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,boundary,clock);var service=new AgencyEvidenceService(boundary,drafts,clock);
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            using var details=JsonDocument.Parse(JsonSerializer.Serialize(new{legalName="Fictional Expiring Evidence",clientMoneyBasis="cass5-client-money",compliance=new{professionalIndemnityStatus="meets-minimum",professionalIndemnityLimit="2000000.00",piExpiresOn=today.ToString("yyyy-MM-dd")}}));
            var created=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(details.RootElement),4,null,default);var id=created.ResourceId;
            var file=await service.Upload(actor,id,Key(),Version(created),AgencyEvidenceRules.ValidateFile("pi-proof.txt","text/plain","Fictional expiring insurance evidence"u8.ToArray()),default);
            await service.Attest(actor,id,Key(),Version(file),"professional-indemnity",file.ResourceId,"Reviewed fictional PI evidence",today,default);
            async Task<AgencyReadiness> Readiness(){await using var db=new BackOfficeDbContext(options);await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);return await service.Validate(db,agency,default);}
            var current=await Readiness();Assert.False(current.Valid);Assert.Contains(current.Items,x=>x.Code=="evidence-professional-indemnity"&&x.State=="satisfied");Assert.Contains(current.Items,x=>x.Code=="evidence-client-money"&&x.State=="missing");Assert.Contains(current.Items,x=>x.Code=="broker-administrator"&&x.State=="missing");
            clock.Advance(TimeSpan.FromDays(1));Assert.Contains((await Readiness()).Items,x=>x.Code=="evidence-professional-indemnity"&&x.State=="expired");
            await using(var db=new BackOfficeDbContext(options)){var rule=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-compliance"&&x.Version==2);Assert.Contains("1700000.00",rule.Values);Assert.Contains("1300000.00",(await db.Set<SettingVersion>().SingleAsync(x=>x.Scope==rule.Scope&&x.Version==1)).Values);db.Add(new SettingVersion{Scope=rule.Scope,Version=3,EffectiveFrom=rule.EffectiveFrom,Values=rule.Values});await db.SaveChangesAsync();Assert.Equal("verified",(await db.Set<AgencyEvidence>().SingleAsync(x=>x.AgencyId==id)).State);}
            Assert.Contains((await Readiness()).Items,x=>x.Code=="evidence-professional-indemnity"&&x.State=="stale");
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<string> SignIn(HttpClient client,string role,string password){var csrf=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var result=await Send(client,csrf,"/api/v1/auth/login",new{email=role+"@cover.example",password});result.EnsureSuccessStatusCode();return(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;}
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,string path,object? body,string? etag=null){var request=new HttpRequestMessage(HttpMethod.Post,path);if(body is not null)request.Content=JsonContent.Create(body);Headers(request,csrf,etag,Key());return client.SendAsync(request);}
    private static void Headers(HttpRequestMessage request,string? csrf,string? etag,string key){if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);if(etag is not null)request.Headers.Add("If-Match",etag);request.Headers.Add("Idempotency-Key",key);}
    private static Task<HttpResponseMessage> Upload(HttpClient client,string csrf,string path,string etag,string key,string name,string type,byte[] bytes){var content=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new MediaTypeHeaderValue(type);content.Add(file,"file",name);content.Add(new StringContent(name),"fileName");content.Add(new StringContent(type),"contentType");var request=new HttpRequestMessage(HttpMethod.Post,path+"/evidence-files"){Content=content};Headers(request,csrf,etag,key);return client.SendAsync(request);}
    private static string Key()=>Guid.NewGuid().ToString("N");
    private static byte[] Version(CommandOutcome result)=>Convert.FromBase64String(result.Etag!.Trim('"'));
    private sealed class FrozenClock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan elapsed)=>now+=elapsed;}
}
