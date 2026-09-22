using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalClaimsApiPinsFactsAndRecoversProviderTimeout()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();f.Clock.Current=term.EndsAt.AddDays(2);
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalClaimsSeed.Seed(db);await tx.CommitAsync();}
        db.Add(new SettingVersion{Scope=ClaimsHandoffService.WorkKind,Version=2,EffectiveFrom=f.Clock.GetUtcNow(),Values="{\"demo\":true,\"kind\":\"operational-claims\",\"schemaVersion\":\"2\",\"scenario\":\"timeout-after-success\"}"});await db.SaveChangesAsync();
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);var resolver=new IncidentOccurrenceResolver(f.Factory,f.Clock);var incidents=new IncidentService(f.Factory,boundary,resolver,f.Clock);
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1);
        var draft=JsonSerializer.SerializeToElement(new{policyId=version.PolicyId,productCode="motor-trade-road-risks",occurrence=new{occurredOn=day.ToString("yyyy-MM-dd"),timeZone="Europe/London",precision="date"},kind="other",thirdPartyInvolvement="unknown",reportedBy="Fictional reporter",reportingRoute="agency",bestContactDescription="01632 960001",description="A fictional customer reported damage to a boundary wall.",motorSubject=new{kind="third-party-only",itemDescription="Customer boundary wall"}});
        var created=await incidents.Create(f.Underwriter,draft,Guid.NewGuid().ToString(),default);var resolved=await incidents.Resolve(f.Underwriter,created.ResourceId,created.Etag!,false,Guid.NewGuid().ToString(),default);
        var incident=await db.Set<OperationalIncident>().AsNoTracking().SingleAsync();
        WebApplicationFactory<Program> StartHost()=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:OperationalClaimsWorkerEnabled","false").UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","claims-api-keys",db.Database.GetDbConnection().Database)))
            .ConfigureServices(s=>s.AddSingleton<TimeProvider>(f.Clock)));
        using var host=StartHost();var client=host.CreateClient();using var originalClient=client;var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})}){login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var path=$"/api/v1/incidents/{incident.Id}";
        async Task<HttpResponseMessage> Write(string url,object body,string etag,string? key=null,bool sendCsrf=true)
        {using var req=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};req.Headers.Add("If-Match",etag);req.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());if(sendCsrf)req.Headers.Add("X-CSRF-TOKEN",csrf);return await client.SendAsync(req);}
        var input=new{revisionId=incident.CurrentRevisionId,resolutionId=incident.CurrentResolutionId,providerId=OperationalClaimsSeed.AdministratorId};var key=Guid.NewGuid().ToString();
        using(var missingCsrf=await Write(path+"/log-and-handoff",input,resolved.Etag!,sendCsrf:false))Assert.Equal(HttpStatusCode.Forbidden,missingCsrf.StatusCode);
        using(var foreign=await Write(path+"/log-and-handoff",new{revisionId=Guid.NewGuid(),resolutionId=incident.CurrentResolutionId,providerId=OperationalClaimsSeed.AdministratorId},resolved.Etag!))Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        var receiptsBefore=await db.Set<IdempotencyRecord>().CountAsync();var auditBefore=await db.Set<AuditEvent>().CountAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_ClaimsRequest_TestFailure ON ClaimsRequest AFTER INSERT AS THROW 52099,'Owned claims rollback probe.',1;");
        try
        {
            var direct=new ClaimsHandoffService(boundary,resolver,f.Clock);
            var failure=await Assert.ThrowsAsync<DbUpdateException>(()=>direct.Handoff(f.Underwriter,incident.Id,resolved.Etag!,incident.CurrentRevisionId!.Value,incident.CurrentResolutionId!.Value,OperationalClaimsSeed.AdministratorId,true,Guid.NewGuid().ToString(),default));
            Assert.Equal(52099,Assert.IsType<SqlException>(failure.InnerException).Number);
        }
        finally{await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_ClaimsRequest_TestFailure;");}
        Assert.Empty(await db.Set<ClaimsHandoff>().ToArrayAsync());Assert.Empty(await db.Set<ClaimsRequest>().ToArrayAsync());Assert.Equal(0,await db.Set<OutboxWork>().CountAsync(x=>x.Kind==ClaimsHandoffService.WorkKind));
        Assert.Equal(receiptsBefore,await db.Set<IdempotencyRecord>().CountAsync());Assert.Equal(auditBefore,await db.Set<AuditEvent>().CountAsync());Assert.Equal("draft",await db.Set<OperationalIncident>().Where(x=>x.Id==incident.Id).Select(x=>x.State).SingleAsync());
        using var queued=await Write(path+"/log-and-handoff",input,resolved.Etag!,key);Assert.Equal(HttpStatusCode.Accepted,queued.StatusCode);var receipt=await queued.Content.ReadAsStringAsync();
        using(var replay=await Write(path+"/log-and-handoff",input,resolved.Etag!,key)){Assert.Equal(HttpStatusCode.Accepted,replay.StatusCode);Assert.Equal(receipt,await replay.Content.ReadAsStringAsync());}
        var handoff=await db.Set<ClaimsHandoff>().AsNoTracking().SingleAsync();var request=await db.Set<ClaimsRequest>().AsNoTracking().SingleAsync();
        Assert.Equal(version.Id,handoff.SourceVersionId);Assert.Equal(incident.CurrentRevisionId,handoff.RevisionId);Assert.Contains("Customer boundary wall",handoff.RequestJson);
        Assert.Equal("queued",await db.Set<OperationalIncident>().Where(x=>x.Id==incident.Id).Select(x=>x.State).SingleAsync());
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClaimsRequest SET PayloadJson=N'{{}}' WHERE Id={request.Id}"))).Number);
        using(var job=await client.GetAsync($"/api/v1/jobs/{request.WorkId}")){Assert.Equal(HttpStatusCode.OK,job.StatusCode);Assert.Contains("no-store",job.Headers.CacheControl!.ToString());}
        var leases=new SqlJobLeases(f.Factory,f.Clock);var files=new FileService(f.Factory,boundary,new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","claims-files",db.Database.GetDbConnection().Database)),[]),f.Clock);
        var worker=new ClaimsHandoffWorker(f.Factory,resolver,files,f.Clock);var lease=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,request.WorkId);Assert.NotNull(lease);
        var timeout=await Assert.ThrowsAsync<ClaimsWorkerException>(()=>worker.ExecuteProvider(lease));Assert.Equal(JobFailure.ProviderTimeout,timeout.Failure);
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ClaimsHandoffService.WorkKind));Assert.Empty(await db.Set<ClaimsSummary>().ToArrayAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET LeaseExpiresAt={f.Clock.GetUtcNow().AddSeconds(-1)} WHERE Id={request.WorkId}");
        var recovery=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,request.WorkId);Assert.NotNull(recovery);
        // Stop and recreate the application host with the same SQL database and keys.
        // The provider effect must survive this application/worker restart.
        host.Dispose();using var resumedHost=StartHost();using var resumedClient=resumedHost.CreateClient();client=resumedClient;
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})}){login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        worker=new ClaimsHandoffWorker(f.Factory,new IncidentOccurrenceResolver(f.Factory,f.Clock),files,f.Clock);var outcome=await worker.ExecuteProvider(recovery);Assert.NotNull(outcome);
        Assert.Equal(InboxApplication.StaleLease,await worker.Apply(lease,outcome));Assert.Equal(InboxApplication.Applied,await worker.Apply(recovery,outcome));Assert.Equal(InboxApplication.Duplicate,await worker.Apply(recovery,outcome));
        Assert.Equal(InboxApplication.Quarantined,await worker.Apply(recovery,outcome with{State="rejected"}));
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ClaimsHandoffService.WorkKind));Assert.Equal(1,await db.Set<ClaimsSummary>().CountAsync());
        using var summaryResponse=await client.GetAsync(path+"/summaries");summaryResponse.EnsureSuccessStatusCode();var summaries=await summaryResponse.Content.ReadFromJsonAsync<JsonElement>();var summary=summaries.GetProperty("items")[0];Assert.Equal(JsonValueKind.Null,summary.GetProperty("paid").ValueKind);Assert.Equal(JsonValueKind.Null,summary.GetProperty("reserved").ValueKind);Assert.Equal("notified",summary.GetProperty("status").GetString());
        var current=await incidents.Read(f.Underwriter,incident.Id,default);
        using(var contact=await Write(path+"/contact",new{body="Please review this fictional notification."},current.Etag!))Assert.Equal(HttpStatusCode.Accepted,contact.StatusCode);
        using(var refresh=await Write(path+"/refresh",new{},current.Etag!))Assert.Equal(HttpStatusCode.Accepted,refresh.StatusCode);
        Assert.Equal(3,await db.Set<ClaimsRequest>().CountAsync());
        var refreshRequest=await db.Set<ClaimsRequest>().AsNoTracking().SingleAsync(x=>x.Purpose=="refresh");var refreshLease=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,refreshRequest.WorkId);Assert.NotNull(refreshLease);
        Assert.Equal(JobFailure.ProviderTimeout,(await Assert.ThrowsAsync<ClaimsWorkerException>(()=>worker.ExecuteProvider(refreshLease))).Failure);
        var lateOutcome=await worker.ExecuteProvider(refreshLease);Assert.NotNull(lateOutcome);
        var summaryId=await db.Set<ClaimsSummary>().Select(x=>x.Id).SingleAsync();Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClaimsSummary SET SummaryJson=N'{{}}' WHERE Id={summaryId}"))).Number);
        // Later MID/cancellation guards can reject a full downgrade first. Test
        // the actual claims migration guard without mutating unrelated history.
        var migrations=db.GetService<IMigrationsAssembly>();
        var migration=migrations.CreateMigration(migrations.Migrations["20260922125221_OperationalClaims"],db.Database.ProviderName!);
        var guard=migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().First();
        var downgrade=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync(guard.Sql));
        Assert.Equal(52000,downgrade.Number);Assert.Contains("Cannot remove retained claims history",downgrade.Message);
        Assert.Equal(1,await db.Set<ClaimsSummary>().CountAsync());
        await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).ExecuteUpdateAsync(setters=>setters.SetProperty(x=>x.State,"suspended"));
        Assert.Equal(InboxApplication.Applied,await worker.Apply(refreshLease,lateOutcome));Assert.Equal(1,await db.Set<ClaimsSummary>().CountAsync());
        Assert.Equal("claims-context-unavailable",await db.Set<OutboxWork>().Where(x=>x.Id==refreshRequest.WorkId).Select(x=>x.ErrorCode).SingleAsync());
        var contactRequest=await db.Set<ClaimsRequest>().AsNoTracking().SingleAsync(x=>x.Purpose=="contact");var contactLease=await leases.ClaimWorkAsync(ClaimsHandoffService.WorkKind,contactRequest.WorkId);Assert.NotNull(contactLease);
        Assert.Equal(JobFailure.Superseded,(await Assert.ThrowsAsync<ClaimsWorkerException>(()=>worker.ExecuteProvider(contactLease))).Failure);
        Assert.Equal(2,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ClaimsHandoffService.WorkKind));
        using(var denied=await Write(path+"/log-and-handoff",input,resolved.Etag!,key))Assert.False(denied.IsSuccessStatusCode);
        using(var denied=await client.GetAsync(path+"/summaries"))Assert.False(denied.IsSuccessStatusCode);
        using(var denied=await Write(path+"/refresh",new{},current.Etag!))Assert.False(denied.IsSuccessStatusCode);
    });
}
