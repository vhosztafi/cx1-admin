using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyNotificationApiTests
{
    [Fact]
    public async Task RealSqlAgencyNotificationApiScopesReadsAndAuthorizesVersionedReplay()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            Guid agencyId,otherId,actorId,notificationId,rejectedId;var secret="Fictional secret "+Guid.NewGuid();
            var clock=new Clock();var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);
            var payload=new AgencyNotificationPayload(new EphemeralDataProtectionProvider());var service=new AgencyNotificationService(payload,clock);
            var worker=new AgencyNotificationWorker(factory,payload,clock);var leases=new SqlJobLeases(factory,clock);
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
                actorId=(await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example")).Id;
                var agency=new Agency{Reference="AG-API-NOTICE",LegalName="Fictional API Notifications",State="active"};
                var other=new Agency{Reference="AG-API-OTHER",LegalName="Fictional Other Agency",State="active"};db.AddRange(agency,other);await db.SaveChangesAsync();agencyId=agency.Id;otherId=other.Id;
            }
            async Task<Guid> Enqueue(string scenario,int version)
            {
                await using var db=new BackOfficeDbContext(options);var setting=new SettingVersion{Scope="agency-notification",Version=version,EffectiveFrom=clock.GetUtcNow(),Values=JsonSerializer.Serialize(new{demo=true,scenario})};
                db.Add(setting);await db.SaveChangesAsync();await using var tx=await db.Database.BeginTransactionAsync();
                var id=await service.EnqueueActivation(db,agencyId,Guid.NewGuid(),setting.Id,actorId,new(){Recipient="fictional@cover.example",Template="agency-activated",Content=secret});await tx.CommitAsync();return id;
            }
            notificationId=await Enqueue("unavailable",1);
            for(var i=0;i<6;i++){var lease=(await leases.ClaimKindAsync(AgencyNotificationService.Kind))!;var failure=await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>worker.Deliver(lease));await leases.FailAsync(lease,failure.Failure);clock.Advance();}
            rejectedId=await Enqueue("reject",2);var rejectLease=(await leases.ClaimKindAsync(AgencyNotificationService.Kind))!;await worker.Apply(rejectLease,(await worker.Deliver(rejectLease))!.Value);
            // Use wall-clock creation instants in paged reads; simulated retry clock is intentionally advanced.
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:AgencyNotificationWorkerEnabled","false")
                .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","notification-api-keys",owned))));
            using var admin=host.CreateClient();var csrf=await Login(admin,"agency-admin",password);
            using var uw=host.CreateClient();await Login(uw,"underwriter",password);
            var path=$"/api/v1/agencies/{agencyId}/notifications";var detail=path+"/"+notificationId;var retry=detail+"/retry";
            Assert.Equal(HttpStatusCode.Forbidden,(await uw.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/agencies/{otherId}/notifications/{notificationId}")).StatusCode);
            using var response=await admin.GetAsync(detail);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
            var data=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("exhausted",data.GetProperty("state").GetString());Assert.True(data.GetProperty("retryAllowed").GetBoolean());
            Assert.DoesNotContain(secret,data.ToString());Assert.False(data.TryGetProperty("protectedPayload",out _));Assert.False(data.TryGetProperty("contentHash",out _));
            var rejected=await Read(admin,path+"/"+rejectedId);Assert.Equal("rejected",rejected.GetProperty("state").GetString());Assert.False(rejected.GetProperty("retryAllowed").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,path+"/"+rejectedId+"/retry",csrf,rejected.GetProperty("etag").GetString(),Guid.NewGuid().ToString(),new{reason="Rejected retry"})).StatusCode);
            var etag=response.Headers.ETag!.ToString();var key=Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,retry,null,etag,key,new{reason="Fictional recovery"})).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionRequired,(await Send(admin,retry,csrf,null,key,new{reason="Fictional recovery"})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(admin,retry,csrf,etag,key,new{reason=""})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,retry,csrf,etag,key,new{reason="Fictional",recipient="forged"})).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(admin,retry,csrf,"\"AAAAAAAAAAA=\"",key,new{reason="Fictional recovery"})).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(admin,$"/api/v1/agencies/{otherId}/notifications/{notificationId}/retry",csrf,etag,key,new{reason="Wrong owner"})).StatusCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Send(admin,retry,csrf,etag,key,new{reason=new string('a',9000)})).StatusCode);
            var page=await Read(admin,path+"?pageSize=1");Assert.Equal(2,page.GetProperty("totalCount").GetInt32());var cursor=page.GetProperty("nextCursor").GetString()!;
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync($"/api/v1/agencies/{otherId}/notifications?pageSize=1&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
            using var queued=await Send(admin,retry,csrf,etag,key,new{reason="Fictional recovery"});Assert.Equal(HttpStatusCode.Accepted,queued.StatusCode);Assert.Single((await queued.Content.ReadFromJsonAsync<JsonElement>()).EnumerateObject());
            using var replay=await Send(admin,retry,csrf,etag,key,new{reason="Fictional recovery"});Assert.Equal(HttpStatusCode.Accepted,replay.StatusCode);Assert.Equal(await queued.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,retry,csrf,etag,key,new{reason="Changed recovery"})).StatusCode);
            var queuedData=await Read(admin,detail);Assert.Equal("queued",queuedData.GetProperty("state").GetString());Assert.False(queuedData.GetProperty("retryAllowed").GetBoolean());
            await using(var db=new BackOfficeDbContext(options))
            {
                var message=await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==notificationId);var job=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==message.WorkId);Assert.Equal(12,job.AttemptLimit);Assert.Equal(6,job.Attempts);
                Assert.Single(await db.Set<IdempotencyRecord>().Where(x=>x.Route==retry).ToListAsync());
                var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==actorId&&x.RoleId==role.Id));await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,retry,csrf,etag,key,new{reason="Fictional recovery"})).StatusCode);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow.AddDays(-1);public override DateTimeOffset GetUtcNow()=>now;public void Advance()=>now=now.AddHours(1);}
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {var csrf=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var response=await Send(client,"/api/v1/auth/login",csrf,null,Guid.NewGuid().ToString(),new{email=role+"@cover.example",password});response.EnsureSuccessStatusCode();return(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;}
    private static Task<HttpResponseMessage> Send(HttpClient client,string path,string? csrf,string? etag,string key,object body)
    {var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);if(etag is not null)request.Headers.Add("If-Match",etag);request.Headers.Add("Idempotency-Key",key);return client.SendAsync(request);}
}
