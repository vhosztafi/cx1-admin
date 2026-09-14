using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using BackOffice.Infrastructure.Agencies;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyDraftTests
{
    [Fact]
    public async Task RealSqlAgencyDraftUpgradePreservesLegacyIdentityAndAllocatesUniqueReferences()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var legacyId=Guid.NewGuid();
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20260914054253_MatchIntakeEvidence");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Agency] ([Id],[Reference],[LegalName],[State],[CreatedAt],[UpdatedAt]) VALUES ({legacyId},N'AG-DEMO-LEGACY',N'Fictional Legacy Agency',N'draft',SYSUTCDATETIME(),SYSUTCDATETIME())");
                await db.Database.MigrateAsync();
                var legacy=await db.Set<Agency>().SingleAsync(x=>x.Id==legacyId);Assert.Equal("Fictional Legacy Agency",legacy.LegalName);Assert.Equal("draft",legacy.State);Assert.Equal(1,legacy.OnboardingStep);
                Assert.Contains("Fictional Legacy Agency",(await db.Set<AgencyOnboarding>().SingleAsync(x=>x.AgencyId==legacyId)).Details);
                Assert.False(db.Database.HasPendingModelChanges());
            }
            var references=await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>{await using var db=new BackOfficeDbContext(options);await using var tx=await db.Database.BeginTransactionAsync();var reference=await AgencyDraftService.NextReference(db);db.Add(new Agency{Reference=reference});await db.SaveChangesAsync();await tx.CommitAsync();return reference;}));
            Assert.Equal(8,references.Distinct().Count());
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(9,await db.Set<Agency>().CountAsync());
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [Agency] ([Id],[Reference],[LegalName],[State],[CreatedAt],[UpdatedAt]) VALUES (NEWID(),{references[0]},N'',N'draft',SYSUTCDATETIME(),SYSUTCDATETIME())"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [AgencyOnboarding] ([Id],[AgencyId],[SchemaVersion],[Details],[CreatedAt],[UpdatedAt]) VALUES(NEWID(),NEWID(),N'1.0',{"{}"},SYSUTCDATETIME(),SYSUTCDATETIME())"));
                await using var tx=await db.Database.BeginTransactionAsync();var abandoned=await AgencyDraftService.NextReference(db);db.Add(new Agency{Reference=abandoned});await db.SaveChangesAsync();await tx.RollbackAsync();db.ChangeTracker.Clear();Assert.False(await db.Set<Agency>().AnyAsync(x=>x.Reference==abandoned));
            }
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    [Fact]
    public async Task RealSqlAgencyDraftApiPreservesInputsAtomicProductsRetriesScopeAndConstraints()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            await using(var db=new BackOfficeDbContext(options)){await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);Assert.False(db.Database.HasPendingModelChanges());}
            using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","agency-test-keys",owned))));
            using var anonymous=factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/agencies")).StatusCode);
            using var admin=factory.CreateClient();var csrf=await Login(admin,"agency-admin",password);
            using var servicing=factory.CreateClient();await Login(servicing,"servicing",password);Assert.Equal(HttpStatusCode.Forbidden,(await servicing.GetAsync("/api/v1/agencies")).StatusCode);
            using var uw=factory.CreateClient();var uwToken=await Login(uw,"underwriter",password);Assert.Equal(HttpStatusCode.OK,(await uw.GetAsync("/api/v1/agencies")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(uw,uwToken,HttpMethod.Post,"/api/v1/agencies",Body())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,null,HttpMethod.Post,"/api/v1/agencies",Body())).StatusCode);
            var catalog=await Read(admin,"/api/v1/agency-product-catalog");Assert.Equal(3,catalog.GetProperty("items").GetArrayLength());
            foreach(var item in catalog.GetProperty("items").EnumerateArray()){Assert.True(item.GetProperty("distributionEligible").GetBoolean());Assert.False(item.GetProperty("ratingReady").GetBoolean());}
            var productId=catalog.GetProperty("items")[0].GetProperty("productVersionId").GetGuid();var product=new{productVersionId=productId,effectiveFrom="2026-09-14",brokerCommissionBasisPoints=1250};
            var details=new{legalName="Fictional Agency Draft",territory="NI",arrangesGeneralInsurance="unchecked",correspondencePreference="portal-only",mainContact=new{name="Fictional Sam"},commercialTerms=new{commissionBasis="flat-rate",volumeCommitmentMode="target-tiered"},compliance=new{beneficialOwnershipVerified="refer"}};
            var body=new{details,onboardingStep=3,products=new[]{product}};var key=Guid.NewGuid().ToString("N");
            var simultaneous=await Task.WhenAll(Send(admin,csrf,HttpMethod.Post,"/api/v1/agencies",body,key),Send(admin,csrf,HttpMethod.Post,"/api/v1/agencies",body,key));
            Assert.All(simultaneous,r=>Assert.Equal(HttpStatusCode.Created,r.StatusCode));
            var id=(await simultaneous[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var path="/api/v1/agencies/"+id;var initial=simultaneous[0].Headers.ETag!.ToString();
            Assert.Equal(await simultaneous[0].Content.ReadAsStringAsync(),await simultaneous[1].Content.ReadAsStringAsync());
            Assert.DoesNotContain("Fictional",await simultaneous[0].Content.ReadAsStringAsync());foreach(var response in simultaneous)response.Dispose();
            var saved=await Read(admin,path);Assert.Equal(3,saved.GetProperty("onboardingStep").GetInt32());Assert.Equal("NI",saved.GetProperty("details").GetProperty("territory").GetString());Assert.False(saved.GetProperty("validation").GetProperty("valid").GetBoolean());
            Assert.Single((await Read(admin,path+"/products")).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,csrf,HttpMethod.Post,"/api/v1/agencies",Body(),key)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(admin,csrf,HttpMethod.Put,path,body)).StatusCode);
            var changed=new{details,onboardingStep=5,products=Array.Empty<object>()};var updateKey=Guid.NewGuid().ToString("N");
            using var updated=await Send(admin,csrf,HttpMethod.Put,path,changed,updateKey,initial);Assert.Equal(HttpStatusCode.OK,updated.StatusCode);var latest=updated.Headers.ETag!.ToString();Assert.NotEqual(initial,latest);
            using var replay=await Send(admin,csrf,HttpMethod.Put,path,changed,updateKey,initial);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(latest,replay.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(admin,csrf,HttpMethod.Put,path,body,etag:initial)).StatusCode);
            Assert.Empty((await Read(admin,path+"/products")).GetProperty("items").EnumerateArray());
            var competing=await Task.WhenAll(Send(admin,csrf,HttpMethod.Put,path,new{details,onboardingStep=4},etag:latest),Send(admin,csrf,HttpMethod.Put,path,new{details,onboardingStep=6},etag:latest));
            Assert.Single(competing,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(competing,r=>r.StatusCode==HttpStatusCode.PreconditionFailed);foreach(var response in competing)response.Dispose();
            using var current=await admin.GetAsync(path);var etag=current.Headers.ETag!.ToString();
            foreach(var invalid in new object[]{new{details=new{state="active"},onboardingStep=1},new{details=new{legalName=(string?)null},onboardingStep=1},new{details,onboardingStep=7},new{details,onboardingStep=1,products=new[]{new{productVersionId=Guid.NewGuid(),effectiveFrom="2026-09-14",brokerCommissionBasisPoints=1000}}}})
                Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(admin,csrf,HttpMethod.Put,path,invalid,etag:etag)).StatusCode);
            using var after=await admin.GetAsync(path);Assert.Equal(etag,after.Headers.ETag!.ToString());
            Assert.Equal((HttpStatusCode)413,(await Send(admin,csrf,HttpMethod.Put,path,new{details=new{legalName=new string('x',66000)},onboardingStep=1},etag:etag)).StatusCode);
            var list=await Read(admin,"/api/v1/agencies?pageSize=1");var cursor=list.GetProperty("nextCursor").GetString();Assert.NotNull(cursor);
            Assert.Equal(HttpStatusCode.BadRequest,(await uw.GetAsync("/api/v1/agencies?pageSize=1&cursor="+Uri.EscapeDataString(cursor!))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/v1/agencies?pageSize=1&state=draft&cursor="+Uri.EscapeDataString(cursor!))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync("/api/v1/agencies?state=unknown")).StatusCode);
            Assert.Single((await Read(admin,"/api/v1/agencies?q=Fictional%20Agency%20Draft")).GetProperty("items").EnumerateArray());
            var activity=await Read(admin,path+"/activity");Assert.Equal(3,activity.GetProperty("totalCount").GetInt32());Assert.Contains("Demo agency-admin",activity.ToString());
            await using(var db=new BackOfficeDbContext(options))
            {
                await DemoDatabase.SeedAsync(db,password);Assert.Equal(3,await db.Set<Agency>().CountAsync());Assert.Equal(35,await db.Set<ClientAgencyRelationship>().CountAsync());
                Assert.Equal("Fictional Agency Draft",(await db.Set<Agency>().SingleAsync(x=>x.Id==id)).LegalName);
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Agency] SET [OnboardingStep]=7 WHERE [Id]={id}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [AgencyOnboarding] SET [Details]=N'[]' WHERE [AgencyId]={id}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AgencyActivity] WHERE [AgencyId]={id}"));
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==user.Id&&x.RoleId==role.Id));await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,csrf,HttpMethod.Put,path,changed,updateKey,initial)).StatusCode);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    private static object Body()=>new{details=new{},onboardingStep=1};
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return(await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
    private static async Task<string> Login(HttpClient client,string role,string password){var csrf=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var result=await Send(client,csrf,HttpMethod.Post,"/api/v1/auth/login",new{email=role+"@cover.example",password});result.EnsureSuccessStatusCode();return(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;}
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,HttpMethod method,string path,object body,string? key=null,string? etag=null){var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag is not null)request.Headers.Add("If-Match",etag);return client.SendAsync(request);}
}
