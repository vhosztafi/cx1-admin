using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencySharingPagingTests
{
    [Fact]
    public async Task RealSqlSharingPagesKeepScopeAndAuditTogetherAndInvalidateVisibleMembershipChanges()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Guid.NewGuid().ToString("N") + "a1";
        var first = PartyDemoSeed.FirstAgencyId; var second = PartyDemoSeed.SecondAgencyId;
        var firstRelationship = PartyDemoSeed.RelationshipId(3, 1); var secondRelationship = PartyDemoSeed.RelationshipId(3, 2);
        try
        {
            Guid staffId;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password, includeSupportFlags: true);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={first} OR Id={second}");
                staffId = await db.Set<StaffUser>().Where(x => x.Email == "agency-admin@cover.example").Select(x => x.Id).SingleAsync();
            }
            var staff = new ActorContext(staffId, null, null, new HashSet<string> { "agency-admin" });
            var query = new AgencySharingQuery(RelationshipId: secondRelationship);
            async Task<string> Fingerprint()
            {
                await using var db = new BackOfficeDbContext(options); await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                return await AgencySharingService.PreviewPageScope(db, staff, second, "instructions", query);
            }
            await using (var db = new BackOfficeDbContext(options))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => AgencySharingService.PreviewPageScope(db, staff, second, "instructions", query));
                await using var transaction = await db.Database.BeginTransactionAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => AgencySharingService.PreviewInstructions(db, staff, second, query));
            }
            var original = await Fingerprint();
            await using (var db = new BackOfficeDbContext(options))
                await db.Database.ExecuteSqlRawAsync("UPDATE SupportFlag SET Reason=N'Private reason edited',InternalInstruction=N'Private instruction edited'");
            Assert.Equal(original, await Fingerprint());
            await using (var db = new BackOfficeDbContext(options))
                await db.Database.ExecuteSqlRawAsync("UPDATE SupportFlag SET AgencyInstruction=N'Changed public instruction' WHERE AgencyInstruction IS NOT NULL");
            Assert.NotEqual(original, await Fingerprint());
            // Outer transaction owns audit and materialization; nested projection must
            // not commit it early. Visible rows remain locked across both operations.
            await using (var db = new BackOfficeDbContext(options))
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                await AgencySharingService.PreviewPageScope(db, staff, first, "contacts", new(RelationshipId: firstRelationship));
                Assert.Equal(2, (await AgencySharingService.PreviewContacts(db, staff, first, new(RelationshipId: firstRelationship))).Total);
                Assert.NotNull(db.Database.CurrentTransaction);
                await using var writer = new BackOfficeDbContext(options);
                var locked = await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync($"SET LOCK_TIMEOUT 150; UPDATE Contact SET Role=N'Changed role' WHERE Id={ContactDemoSeed.ContactId(1)}")); Assert.Equal(1222, locked.Number);
                await transaction.RollbackAsync();
                await writer.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT -1");
            }
            await using (var db = new BackOfficeDbContext(options)) Assert.False(await db.Set<AuditEvent>().AnyAsync(x => x.EventType == "agency.sharing-preview"));
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Cover:SqlConnection", connection.ConnectionString).UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "sharing-page-test-keys", owned))));
            using var client = host.CreateClient(); await Login(client, "agency-admin", password);
            using var otherActor = host.CreateClient(); await Login(otherActor, "agency-reviewer", password);
            var firstPath = $"/api/v1/agencies/{first}/sharing"; var secondPath = $"/api/v1/agencies/{second}/sharing";
            var page = await Read(client, secondPath + "/clients?pageSize=1"); Assert.Equal(17, page.GetProperty("totalCount").GetInt32());
            var cursor = Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            Assert.Single((await Read(client, secondPath + "/clients?pageSize=1&cursor=" + cursor)).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(firstPath + "/clients?pageSize=1&cursor=" + cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await otherActor.GetAsync(secondPath + "/clients?pageSize=1&cursor=" + cursor)).StatusCode);
            Assert.Equal(0, (await Read(client, secondPath + "/clients?q=Traders%2004")).GetProperty("totalCount").GetInt32());
            var contacts = firstPath + $"/relationships/{firstRelationship}/contacts";
            var contactPage = await Read(client, contacts + "?pageSize=1"); Assert.Equal(2, contactPage.GetProperty("totalCount").GetInt32());
            var next = contacts + "?pageSize=1&cursor=" + Uri.EscapeDataString(contactPage.GetProperty("nextCursor").GetString()!);
            await using (var db = new BackOfficeDbContext(options))
                await db.Database.ExecuteSqlRawAsync("UPDATE Person SET FullName=N'Private master identity changed'");
            Assert.Single((await Read(client, next)).GetProperty("items").EnumerateArray());
            Assert.Equal(0, (await Read(client, secondPath + $"/relationships/{firstRelationship}/contacts")).GetProperty("totalCount").GetInt32());
            var instructions = secondPath + $"/relationships/{secondRelationship}/instructions";
            var safe = await Read(client, instructions); Assert.Equal("Changed public instruction", safe.GetProperty("items")[0].GetProperty("instruction").GetString());
            Assert.DoesNotContain("Private", safe.GetRawText()); Assert.DoesNotContain("reason", safe.GetRawText());
            await using (var db = new BackOfficeDbContext(options))
            {
                var contact = await db.Set<Contact>().SingleAsync(x => x.Id == ContactDemoSeed.ContactId(2)); contact.EndedAt = DateTimeOffset.UtcNow; contact.EndedBy = staffId; contact.EndReason = "Fictional membership ended"; contact.IsPrimary = false; await db.SaveChangesAsync();
                db.RemoveRange(await db.Set<FlagVisibility>().Where(x => x.RelationshipId == secondRelationship).ToListAsync()); await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(next)).StatusCode);
            Assert.Equal(1, (await Read(client, contacts)).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Read(client, instructions)).GetProperty("totalCount").GetInt32());
            await using (var db = new BackOfficeDbContext(options)) Assert.True(await db.Set<AuditEvent>().AnyAsync(x => x.ActorId == staffId && x.EventType == "agency.sharing-preview"));
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static async Task<JsonElement> Read(HttpClient client, string path) { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task Login(HttpClient client, string role, string password)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = role + "@cover.example", password }) }; request.Headers.Add("X-CSRF-Token", token);
        using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
    }
}
