using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyActionProjectionTests
{
    [Fact]
    public async Task RealSqlAgencyActionCountsReconcileRowsTerminalDecisionsAndDueDateBoundaries()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Guid.NewGuid().ToString("N")+"a1";var today=AgencyActionCounts.LondonDate(DateTimeOffset.UtcNow);
        try
        {
            Guid first,second,stateId,permissionId,reviewerId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
                var actor=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");var reviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-reviewer@cover.example");reviewerId=reviewer.Id;
                var agency=new Agency{Reference="AG-KPI-FIRST",LegalName="Fictional KPI first",State="active"};var other=new Agency{Reference="AG-KPI-SECOND",LegalName="Fictional KPI second",State="active"};db.AddRange(agency,other);await db.SaveChangesAsync();first=agency.Id;second=other.Id;
                var state=new AgencyStateRequest{AgencyId=first,Kind="suspension",RequestedState="suspended",BaseVersion=agency.RowVersion,ProposedInputFingerprint=new string('a',64),RequestedBy=actor.Id,CreatedBy=actor.Id,RequestReason="Fictional pending state"};db.Add(state);await db.SaveChangesAsync();stateId=state.Id;
                var product=await db.Set<ProductVersion>().Select(x=>x.Id).FirstAsync();
                var snapshot=JsonSerializer.Serialize(new{effectiveFrom=today.ToString("yyyy-MM-dd"),commercialTerms=new{commissionBasis="per-product"},creditLimit="0.00",products=new[]{new{productVersionId=product,effectiveFrom=today.ToString("yyyy-MM-dd"),brokerCommissionBasisPoints=1250}}});
                db.Add(new AgencyTermsRequest{AgencyId=first,BaseVersion=agency.RowVersion,EffectiveFrom=today,ProposedSnapshot=snapshot,ProposedInputFingerprint=new string('b',64),RequestedBy=actor.Id,CreatedBy=actor.Id,RequestReason="Fictional pending terms"});
                var permission=new AgencyPermissionRequest{AgencyId=second,RequestedBy=actor.Id,CreatedBy=actor.Id,Reason="Fictional pending permission"};db.Add(permission);await db.SaveChangesAsync();permissionId=permission.Id;
                var rule=await db.Set<SettingVersion>().FirstAsync(x=>x.Scope=="agency-compliance");
                foreach(var date in new[]{today,today.AddDays(1)})
                {
                    var file=new AgencyEvidenceFile{AgencyId=first,FileName="proof.txt",ContentType="text/plain",Content="Fictional PI"u8.ToArray(),CreatedBy=actor.Id};file.ByteLength=file.Content.Length;file.Sha256=Convert.ToHexString(SHA256.HashData(file.Content)).ToLowerInvariant();db.Add(file);await db.SaveChangesAsync();
                    var evidence=new AgencyEvidence{AgencyId=first,Kind="professional-indemnity",State="verified",InputFingerprint=new string('c',64),RuleVersionId=rule.Id,FileId=file.Id,AttestedBy=actor.Id,Notes="Fictional KPI evidence",VerifiedAt=DateTimeOffset.UtcNow,ExpiresOn=date,CreatedBy=actor.Id};db.Add(evidence);await db.SaveChangesAsync();
                    db.Add(new AgencyFollowUp{AgencyId=first,EvidenceId=evidence.Id,Purpose="pi-expiry",DueOn=date,CreatedBy=actor.Id});await db.SaveChangesAsync();
                }
            }
            async Task<AgencyActionCounts> Counts(Guid[]? ids,DateOnly date)
            {
                await using var db=new BackOfficeDbContext(options);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                return AgencyActionCounts.Total((await AgencyActionProjection.Read(db,ids,date)).Values);
            }
            await using(var db=new BackOfficeDbContext(options))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(()=>AgencyActionProjection.Read(db,null,today));
                await using var tx=await db.Database.BeginTransactionAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>AgencyActionProjection.Read(db,null,today));
            }
            Assert.Equal(new(1,1,1,1),await Counts(null,today));Assert.Equal(2,(await Counts([first],today)).OpenActions);
            Assert.Equal(new(0,0,1,0),await Counts([second],today));Assert.Equal(AgencyActionCounts.Empty,await Counts([],today));
            Assert.Equal(0,(await Counts([first],today.AddDays(-1))).DueFollowUps);Assert.Equal(2,(await Counts([first],today.AddDays(1))).DueFollowUps);
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:AgencyNotificationWorkerEnabled","false").UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","kpi-keys",owned))));
            using var client=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/agencies/kpis")).StatusCode);
            var csrf=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
            using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="agency-admin@cover.example",password})}){login.Headers.Add("X-CSRF-Token",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
            var kpis=await Read(client,"/api/v1/agencies/kpis");Assert.Equal(3,kpis.GetProperty("openActions").GetInt32());Assert.Equal(1,kpis.GetProperty("dueFollowUps").GetInt32());Assert.Equal(today.ToString("yyyy-MM-dd"),kpis.GetProperty("asOfDate").GetString());
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/v1/agencies/kpis?state=active")).StatusCode);
            var rows=(await Read(client,"/api/v1/agencies?q=AG-KPI-FIRST")).GetProperty("items");Assert.Single(rows.EnumerateArray());Assert.Equal(2,rows[0].GetProperty("openActionCount").GetInt32());Assert.Equal(1,rows[0].GetProperty("dueFollowUpCount").GetInt32());
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'rejected',DecisionBy={reviewerId},DecisionReason=N'Fictional KPI rejection',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={stateId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyPermissionRequest SET State=N'rejected',DecisionBy={reviewerId},DecisionReason=N'Fictional KPI rejection',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={permissionId}");
            }
            kpis=await Read(client,"/api/v1/agencies/kpis");Assert.Equal(1,kpis.GetProperty("openActions").GetInt32());Assert.Equal(0,kpis.GetProperty("pendingStateRequests").GetInt32());Assert.Equal(0,kpis.GetProperty("pendingPermissionRequests").GetInt32());Assert.Equal(1,kpis.GetProperty("pendingTermsRequests").GetInt32());Assert.Equal(1,kpis.GetProperty("dueFollowUps").GetInt32());
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static async Task<JsonElement> Read(HttpClient client,string path){using var result=await client.GetAsync(path);result.EnsureSuccessStatusCode();return await result.Content.ReadFromJsonAsync<JsonElement>();}
}
