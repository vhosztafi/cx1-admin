using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencySharingContextTests
{
    [Fact]
    public async Task RealSqlSharingContextSelectsCurrentTermsAndKeepsApprovedFutureFeaturesUnavailable()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Guid.NewGuid().ToString("N") + "a1";
        var agencyId = PartyDemoSeed.FirstAgencyId; var otherId = PartyDemoSeed.SecondAgencyId;
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London"); var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, london).DateTime);
        var tomorrow = today.AddDays(1); var midnight = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(tomorrow.ToDateTime(TimeOnly.MinValue), london));
        var clock = new Clock { Now = midnight.AddTicks(-1) };
        try
        {
            Guid brokerId, requesterId, reviewerId, grantId; string firstCode, secondCode;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password);
                requesterId = await db.Set<StaffUser>().Where(x => x.Email == "agency-admin@cover.example").Select(x => x.Id).SingleAsync();
                reviewerId = await db.Set<StaffUser>().Where(x => x.Email == "agency-reviewer@cover.example").Select(x => x.Id).SingleAsync();
                var agency = await db.Set<Agency>().SingleAsync(x => x.Id == agencyId); agency.State = "active";
                (await db.Set<Agency>().SingleAsync(x => x.Id == otherId)).State = "active"; await db.SaveChangesAsync();
                var products = await (from version in db.Set<ProductVersion>() join product in db.Set<Product>() on version.ProductId equals product.Id orderby product.Code select new { version.Id, product.Code }).ToListAsync();
                firstCode = products[0].Code; secondCode = products[1].Code;
                // Fixture establishes published provenance to isolate read semantics;
                // full activation/terms commands have their own workflow tests.
                var activation = new AgencyStateRequest { AgencyId = agencyId, BaseVersion = agency.RowVersion, ProposedInputFingerprint = new string('a', 64), RequestedBy = requesterId, CreatedBy = requesterId, RequestReason = "Fictional initial terms" };
                db.Add(activation); await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={reviewerId},DecisionReason=N'Fixture approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={activation.Id}");
                db.Add(new AgencyTermsVersion { AgencyId = agencyId, Version = 1, EffectiveFrom = today, ApprovedStateRequestId = activation.Id, CreatedBy = reviewerId, Snapshot = Snapshot(today, products[0].Id) }); await db.SaveChangesAsync();
                var change = new AgencyTermsRequest { AgencyId = agencyId, BaseVersion = agency.RowVersion, EffectiveFrom = tomorrow, ProposedSnapshot = Snapshot(tomorrow, products[1].Id), ProposedInputFingerprint = new string('b', 64), RequestedBy = requesterId, CreatedBy = requesterId, RequestReason = "Fictional next terms" };
                db.Add(change); await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyTermsRequest SET State=N'applied',DecisionBy={reviewerId},DecisionReason=N'Fixture approval',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={change.Id}");
                db.Add(new AgencyTermsVersion { AgencyId = agencyId, Version = 2, EffectiveFrom = tomorrow, ApprovedTermsRequestId = change.Id, CreatedBy = reviewerId, Snapshot = change.ProposedSnapshot }); await db.SaveChangesAsync();
                var permission = new AgencyPermissionRequest { AgencyId = agencyId, RequestedBy = requesterId, CreatedBy = requesterId, Reason = "Fictional download request" }; db.Add(permission); await db.SaveChangesAsync();
                permission.State = "granted"; permission.DecisionBy = reviewerId; permission.DecisionReason = "Fictional approval"; permission.DecidedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); grantId = await db.Set<AgencyPermissionGrant>().Select(x => x.Id).SingleAsync();
                var user = new StaffUser { AgencyId = agencyId, State = "invited", Email = "summary-broker@example.test", NormalizedEmail = "SUMMARY-BROKER@EXAMPLE.TEST", DisplayName = "Fictional broker" }; db.Add(user); await db.SaveChangesAsync(); brokerId = user.Id;
                db.Add(new UserRole { UserId = user.Id, RoleId = await db.Set<Role>().Where(x => x.Code == "broker-readonly").Select(x => x.Id).SingleAsync() }); await db.SaveChangesAsync(); user.State = "active"; await db.SaveChangesAsync();
            }
            var broker = new ActorContext(brokerId, null, agencyId, new HashSet<string> { "broker-readonly" });
            var staff = new ActorContext(requesterId, null, null, new HashSet<string> { "agency-admin" });
            await using (var db = new BackOfficeDbContext(options))
            {
                var before = await AgencySharingService.Context(db, broker, agencyId, clock);
                var product = Assert.Single(before.Products); Assert.Equal(firstCode, product.ProductCode); Assert.Equal(tomorrow, product.EffectiveTo); Assert.False(product.Available);
                var preview = await AgencySharingService.PreviewContext(db, staff, agencyId, clock);
                Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(preview));
                Assert.True(Assert.Single(before.Permissions).Granted); Assert.False(before.Permissions[0].Available);
                Assert.Equal(new[] { 10 }, before.UnavailableSections.Select(x => x.OwningPhase));
                Assert.Equal("available",before.OpenItems.State);Assert.Equal(0,before.OpenItems.TotalCount);
                var json = JsonSerializer.Serialize(before);
                foreach (var secret in new[] { "Snapshot", "Commission", "CreditLimit", "DecisionReason", "Balance", "Password" }) Assert.DoesNotContain(secret, json);
                clock.Now = midnight; var after = await AgencySharingService.Context(db, broker, agencyId, clock);
                Assert.Equal(secondCode, Assert.Single(after.Products).ProductCode); Assert.Null(after.Products[0].EffectiveTo);
                var other = await AgencySharingService.PreviewContext(db, staff, otherId, clock); Assert.Empty(other.Products); Assert.False(Assert.Single(other.Permissions).Granted);
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.Context(db, broker, otherId, clock));
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.PreviewContext(db, staff with { UserId = brokerId }, agencyId, clock));
                var grant = await db.Set<AgencyPermissionGrant>().SingleAsync(x => x.Id == grantId); grant.RevokedAt = DateTimeOffset.UtcNow; grant.RevokedBy = requesterId; grant.RevocationReason = "Fictional withdrawal"; await db.SaveChangesAsync();
                Assert.False(Assert.Single((await AgencySharingService.Context(db, broker, agencyId, clock)).Permissions).Granted);
            }
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Cover:SqlConnection", connection.ConnectionString).UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "sharing-context-test-keys", owned))));
            var path = $"/api/v1/agencies/{agencyId}/sharing";
            using var anonymous = host.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
            using var client = host.CreateClient(); await Login(client, "underwriter", password);
            using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(agencyId, body.GetProperty("agency").GetProperty("id").GetGuid()); Assert.False(body.GetProperty("permissions")[0].GetProperty("available").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?agencyId=" + otherId)).StatusCode);
            using var limited = host.CreateClient(); await Login(limited, "servicing", password); Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(path)).StatusCode);
            await using (var db = new BackOfficeDbContext(options))
            {
                var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "underwriter@cover.example"); Assert.Null(user.AgencyId);
                Assert.True(await db.Set<AuditEvent>().AnyAsync(x => x.ActorId == user.Id && x.EventType == "agency.sharing-preview"));
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agencyId}");
                await Assert.ThrowsAsync<AgencyCommandException>(() => AgencySharingService.Context(db, broker, agencyId, clock));
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static string Snapshot(DateOnly date, Guid product) => JsonSerializer.Serialize(new { effectiveFrom = date.ToString("yyyy-MM-dd"), commercialTerms = new { effectiveFrom = date.ToString("yyyy-MM-dd"), commissionBasis = "per-product", feeSharing = "none", volumeCommitmentMode = "none", minimumPremiumOverrideMode = "none", referralRouting = "standard-internal-underwriting" }, settlement = new { statementCycle = "monthly", method = "bank-transfer", premiumCollection = "agency", commissionSettlement = "net-remittance" }, paymentTermsDays = 30, creditLimit = "1000.00", products = new[] { new { productVersionId = product, effectiveFrom = date.ToString("yyyy-MM-dd"), brokerCommissionBasisPoints = 1250 } } });
    private sealed class Clock : TimeProvider { public DateTimeOffset Now; public override DateTimeOffset GetUtcNow() => Now; }
    private static async Task Login(HttpClient client, string role, string password)
    {
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = role + "@cover.example", password }) }; request.Headers.Add("X-CSRF-Token", token);
        using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
    }
}
