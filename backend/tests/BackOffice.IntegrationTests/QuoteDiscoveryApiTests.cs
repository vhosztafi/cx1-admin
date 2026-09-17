using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteDiscoveryScopesFiltersCursorsClientLinksAndAcceptedAgencySummaries()
    {
        await WithDatabase(async (db, password) =>
        {
            var fixture = await CreateFixture(db); var foreign = await CreateFixture(db, "-DISCOVERY-FOREIGN");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency} OR Id={foreign.Agency}");
            await using (var tx = await db.Database.BeginTransactionAsync()) { await QuoteCaptureDemoSeed.SeedAsync(db); await tx.CommitAsync(); }
            var actorId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(actorId, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options;
            var service = new QuoteService(new QuoteFactory(options), new QuoteTime());
            string Proposal(string date, params string[] registrations) => JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode = "motor-trade-road-risks",
                termIntent = new { localStartDate = date }, risk = new { vehicles = registrations.Select(registration => new { id = Guid.NewGuid(), registration }).ToArray() } });
            var first = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, Proposal("2026-12-01", "X11AAA", "X11AAB"), "discovery-first", Guid.NewGuid());
            var second = await service.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, Proposal("2026-11-01", "X22BBB"), "discovery-second", Guid.NewGuid());
            var other = await service.CreateAsync(actor, foreign.Relationship, foreign.ProductVersion, Proposal("2026-10-01", "SECRET99"), "discovery-foreign", Guid.NewGuid());
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:AgencyNotificationWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "quote-discovery-keys", db.Database.GetDbConnection().Database))));
            async Task<JsonElement> Read(HttpClient client, string path)
            {
                using var response = await client.GetAsync(path);
                Assert.True(response.IsSuccessStatusCode, $"{path}: {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                Assert.True(response.Headers.CacheControl!.NoStore);
                return await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            async Task<HttpResponseMessage> Send(HttpClient client, string path, object body, string? etag = null)
            {
                var csrf = (await Read(client, "/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
                request.Headers.Add("X-CSRF-Token", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                if (etag is not null) request.Headers.Add("If-Match", etag);
                return await client.SendAsync(request);
            }
            async Task Login(HttpClient client, string email) { using var login = await Send(client, "/api/v1/auth/login", new { email, password }); login.EnsureSuccessStatusCode(); }
            using var staff = host.CreateClient(); await Login(staff, "underwriter@cover.example");
            using var admin = host.CreateClient(); await Login(admin, "agency-admin@cover.example");
            using var anonymous = host.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/quotes")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/quotes")).StatusCode);
            var page = await Read(staff, "/api/v1/quotes?pageSize=1"); Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
            var cursor = Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            // Session maintenance must not invalidate a data-list cursor. The
            // next authenticated request will touch this session again as well.
            await db.Database.ExecuteSqlRawAsync("UPDATE [Session] SET LastSeenAt=DATEADD(minute,-2,LastSeenAt)");
            var next = await Read(staff, "/api/v1/quotes?pageSize=1&cursor=" + cursor);
            Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id"), next.GetProperty("items")[0].GetProperty("id"));
            Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/quotes?pageSize=1&sort=updated&cursor=" + cursor)).StatusCode);
            foreach (var query in new[] { "q=a&q=b", "sort=unknown", "pageSize=101", "agencyId=bad", "clientId=" + Guid.Empty, "status=issued", "other=1" })
                Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/quotes?" + query)).StatusCode);
            var registrations = await Read(staff, "/api/v1/quotes?q=X11");
            Assert.Equal(1, registrations.GetProperty("totalCount").GetInt32());
            Assert.Equal(first.ResourceId, registrations.GetProperty("items")[0].GetProperty("id").GetGuid());
            var filtered = await Read(staff, $"/api/v1/quotes?clientId={fixture.Client}&agencyId={fixture.Agency}&productCode=motor-trade-road-risks&status=draft&sort=start&direction=asc");
            Assert.Equal(2, filtered.GetProperty("totalCount").GetInt32());
            Assert.Equal(second.ResourceId, filtered.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.Equal("2026-11-01", filtered.GetProperty("items")[0].GetProperty("startDate").GetString());
            var records = await Read(staff, $"/api/v1/clients/{fixture.Client}/records?kind=quote"); Assert.Equal(2, records.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Read(staff, $"/api/v1/clients/{fixture.Client}/records?kind=policy")).GetProperty("totalCount").GetInt32());
            var activity = await Read(staff, $"/api/v1/clients/{fixture.Client}/activity");
            Assert.All(activity.GetProperty("items").EnumerateArray(), x => Assert.Equal("quote", x.GetProperty("recordKind").GetString()));
            var current = await service.GetAsync(actor, first.ResourceId);
            await service.SaveAsync(actor, first.ResourceId, current.Quote.RowVersion, Proposal("2026-12-01", "NEW11AA"), null, "discovery-edited", Guid.NewGuid());
            Assert.Equal(0, (await Read(staff, "/api/v1/quotes?q=X11")).GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/quotes?pageSize=1&cursor=" + cursor)).StatusCode);

            async Task Accept(HttpClient browser, Guid agency, string email)
            {
                var version = "\"" + Convert.ToBase64String(await db.Set<Agency>().AsNoTracking().Where(x => x.Id == agency).Select(x => x.RowVersion).SingleAsync()) + "\"";
                using var invite = await Send(admin, $"/api/v1/agencies/{agency}/invitations", new { email, displayName = "Fictional quote reader", role = "broker-readonly" }, version);
                invite.EnsureSuccessStatusCode(); var invitationId = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationId").GetGuid();
                using var reveal = await Send(admin, $"/api/v1/invitations/{invitationId}/demo-link", new { }); reveal.EnsureSuccessStatusCode();
                var invitationToken = (await reveal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationToken").GetString();
                using var accepted = await Send(browser, "/api/v1/auth/invitations/accept", new { invitationToken, password }); accepted.EnsureSuccessStatusCode();
                await Login(browser, email);
            }
            using var broker = host.CreateClient(); using var foreignBroker = host.CreateClient();
            await Accept(broker, fixture.Agency, "quote-own@example.test"); await Accept(foreignBroker, foreign.Agency, "quote-foreign@example.test");
            var shared = await Read(broker, "/api/v1/agency-context/quotes?pageSize=1"); Assert.Equal(2, shared.GetProperty("totalCount").GetInt32());
            Assert.Equal(new[] { "clientName", "id", "productCode", "reference", "startDate", "state", "updatedAt" }, shared.GetProperty("items")[0].EnumerateObject().Select(x => x.Name).Order().ToArray());
            var sharedCursor = Uri.EscapeDataString(shared.GetProperty("nextCursor").GetString()!);
            await Read(broker, "/api/v1/agency-context/quotes?pageSize=1&cursor=" + sharedCursor);
            Assert.Equal(HttpStatusCode.BadRequest, (await foreignBroker.GetAsync("/api/v1/agency-context/quotes?pageSize=1&cursor=" + sharedCursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await broker.GetAsync($"/api/v1/agency-context/quotes?agencyId={foreign.Agency}")).StatusCode);
            Assert.Equal(0, (await Read(broker, "/api/v1/agency-context/quotes?q=SECRET99")).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Read(broker, "/api/v1/agency-context/quotes?q=NEW11AA")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1, (await Read(foreignBroker, "/api/v1/agency-context/quotes")).GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.Forbidden, (await broker.GetAsync($"/api/v1/quotes/{other.ResourceId}")).StatusCode);
            Assert.Equal(2, (await Read(staff, $"/api/v1/agencies/{fixture.Agency}/sharing/quotes")).GetProperty("totalCount").GetInt32());
            Assert.Equal(2, (await Read(broker, "/api/v1/agency-context")).GetProperty("quotes").GetProperty("totalCount").GetInt32());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await broker.GetAsync("/api/v1/agency-context/quotes")).StatusCode);
        });
    }
}
