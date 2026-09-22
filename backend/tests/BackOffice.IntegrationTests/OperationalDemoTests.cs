using System.Security.Cryptography;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class OperationalDemoTests
{
    [Fact]
    public async Task RealSqlOperationalDemoMatchBridgePreservesRecordedRequestAndEditedDraft()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,s=>s.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();
            var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
            await DemoDatabase.SeedAsync(db,password,includeMatches:true);
            var original=await db.Set<MatchInformationRequest>().AsNoTracking().SingleAsync();
            var factory=new Factory(options);var commands=new SqlCommandBoundary(factory,TimeProvider.System);
            var bridge=new LegacyOperationalBridge(factory,new TaskService(factory,commands,TimeProvider.System),new ThreadService(factory,commands,TimeProvider.System),TimeProvider.System);
            var messageId=await bridge.MatchRequest(original.Id);
            var seed=new OperationalDemoSeed(factory,bridge);
            Assert.Empty((await seed.Initialize()).Issues);
            var draft=await db.Set<OperationalMessageDraft>().SingleAsync(x=>x.Id==messageId);
            Assert.Contains(original.Description,draft.Body);Assert.Equal("draft",draft.State);
            var thread=await db.Set<OperationalThread>().SingleAsync(x=>x.Id==draft.ThreadId);
            Assert.Equal("internal",thread.Visibility);Assert.Null(thread.RelationshipId);
            Assert.Empty(await db.Set<MessageDraftRecipient>().Where(x=>x.MessageId==messageId).ToListAsync());
            draft.Body="Staff revised this retained request; preserve this edit.";draft.UpdatedAt=DateTimeOffset.UtcNow;await db.SaveChangesAsync();
            Assert.Equal(messageId,await bridge.MatchRequest(original.Id));db.ChangeTracker.Clear();
            Assert.Empty((await seed.Initialize()).Issues);
            Assert.Equal(draft.Body,(await db.Set<OperationalMessageDraft>().SingleAsync(x=>x.Id==messageId)).Body);
            var retained=await db.Set<MatchInformationRequest>().AsNoTracking().SingleAsync(x=>x.Id==original.Id);
            Assert.Equal(original.Description,retained.Description);Assert.Equal(original.RecordedAt,retained.RecordedAt);Assert.Equal("recorded",retained.DeliveryState);
            Assert.Single(await db.Set<OperationalMessageDraft>().ToListAsync());
            Assert.Empty(await db.Set<OperationalDelivery>().ToListAsync());
            var association=await db.Set<MatchCorrespondence>().SingleAsync();
            var immutable=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MatchCorrespondence SET CreatedAt=CreatedAt WHERE Id={association.Id}"));
            Assert.Equal(52040,immutable.Number);
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0").UseSetting("Cover:SqlConnection",connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:LegacyOperationalWorkerEnabled","false")
                .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","operational-demo-test-keys",owned))));
            host.UseKestrel(0);using var client=host.CreateClient();var path=$"/api/v1/matches/{original.MatchId}/information-requests";
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(path)).StatusCode);
            var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})};login.Headers.Add("X-CSRF-Token",csrf);
            (await client.SendAsync(login)).EnsureSuccessStatusCode();
            var response=await client.GetFromJsonAsync<JsonElement>(path);var item=Assert.Single(response.GetProperty("items").EnumerateArray());
            Assert.Equal("recorded",item.GetProperty("deliveryState").GetString());
            Assert.Equal(messageId,item.GetProperty("correspondence").GetProperty("messageId").GetGuid());
            (await client.GetAsync($"/api/v1/messages/{messageId}")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/v1/matches/{Guid.NewGuid()}/information-requests")).StatusCode);
            await RunMatchBrowser(host,db,password,original.MatchId,messageId);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={original.ActorId}");
            await Assert.ThrowsAsync<OperationalAccessException>(()=>bridge.MatchRequest(original.Id));
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(path)).StatusCode);
        }
        finally
        {
            if(connection.InitialCatalog!=owned||!owned.StartsWith("CoverMGA_Test_",StringComparison.Ordinal))throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options):IDbContextFactory<BackOfficeDbContext>
    { public BackOfficeDbContext CreateDbContext()=>new(options); }
}
