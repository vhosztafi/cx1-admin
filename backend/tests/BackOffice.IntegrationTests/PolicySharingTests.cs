using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlPolicySharingTestsAcceptedCookiesCannotSearchHiddenRiskOrReadForeignPolicies()
    {
        await WithDatabase(async (db, password) => {
            async Task<Policy> Issue(string product) {
                var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
                var saved = await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
                return await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == saved.ResourceId);
            }
            var first = await Issue("motor-trade-road-risks"); var other = await Issue("motor-trade-combined");
            Assert.Equal(2, await db.Set<ClientActivity>().CountAsync(x => x.EventType == "policy.issued"));
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true);
            Assert.Equal(2, await db.Set<ClientActivity>().CountAsync(x => x.EventType == "policy.issued"));
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", db.Database.GetConnectionString()).UseSetting("Cover:QuoteRatingWorkerEnabled", "false")
                .UseSetting("Cover:CapacityWorkerEnabled", "false").UseSetting("Cover:QuoteDeliveryWorkerEnabled", "false")
                .UseSetting("Cover:AgencyNotificationWorkerEnabled", "false").UseSetting("Cover:DiagnosticWorkerEnabled", "false").UseSetting("Cover:QuoteLookupWorkerEnabled", "false")
                .UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "policy-discovery-keys", db.Database.GetDbConnection().Database))));
            async Task<JsonElement> Read(HttpClient client, string path) { using var r = await client.GetAsync(path); Assert.True(r.IsSuccessStatusCode, $"{path}: {r.StatusCode}: {await r.Content.ReadAsStringAsync()}"); Assert.True(r.Headers.CacheControl!.NoStore); return await r.Content.ReadFromJsonAsync<JsonElement>(); }
            async Task<JsonElement> Post(HttpClient client, string path, object body, string? etag = null) {
                var csrf = (await Read(client, "/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; request.Headers.Add("X-CSRF-Token", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); if (etag != null) request.Headers.Add("If-Match", etag);
                using var r = await client.SendAsync(request); Assert.True(r.IsSuccessStatusCode, $"{path}: {r.StatusCode}: {await r.Content.ReadAsStringAsync()}"); return await r.Content.ReadFromJsonAsync<JsonElement>();
            }
            using var staff = host.CreateClient(); await Post(staff, "/api/v1/auth/login", new { email = "underwriter@cover.example", password });
            using var admin = host.CreateClient(); await Post(admin, "/api/v1/auth/login", new { email = "agency-admin@cover.example", password });
            using var broker = host.CreateClient();
            var etag = "\"" + Convert.ToBase64String(await db.Set<Agency>().AsNoTracking().Where(x => x.Id == first.AgencyId).Select(x => x.RowVersion).SingleAsync()) + "\"";
            var invitation = await Post(admin, $"/api/v1/agencies/{first.AgencyId}/invitations", new { email = "policy-reader@example.invalid", displayName = "Fictional policy reader", role = "broker-readonly" }, etag);
            var link = await Post(admin, $"/api/v1/invitations/{invitation.GetProperty("invitationId").GetGuid()}/demo-link", new { });
            await Post(broker, "/api/v1/auth/invitations/accept", new { invitationToken = link.GetProperty("invitationToken").GetString(), password });
            await Post(broker, "/api/v1/auth/login", new { email = "policy-reader@example.invalid", password });
            var page = await Read(staff, "/api/v1/policies?pageSize=1"); Assert.Equal(2, page.GetProperty("totalCount").GetInt32()); var cursor = Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            await db.Database.ExecuteSqlRawAsync("UPDATE [Session] SET LastSeenAt=DATEADD(minute,-2,LastSeenAt)");
            var next = await Read(staff, "/api/v1/policies?pageSize=1&cursor=" + cursor); Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id"), next.GetProperty("items")[0].GetProperty("id"));
            foreach (var suffix in new[] { "&sort=issued", "&q=unknown", "&agencyId=" + other.AgencyId }) Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/policies?pageSize=1&cursor=" + cursor + suffix)).StatusCode);
            foreach (var query in new[] { "q=a&q=b", "unknown=1", "state=bound", "pageSize=101", "agencyId=bad", "registration=---", "inceptionFrom=2026-12-01&inceptionTo=2026-01-01", "cursor=tampered" }) Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync("/api/v1/policies?" + query)).StatusCode);
            Assert.Equal(1, (await Read(staff, $"/api/v1/policies?clientId={first.ClientId}&agencyId={first.AgencyId}&productCode=motor-trade-road-risks&sort=inception&direction=desc&inceptionFrom=2026-01-01&inceptionTo=2026-12-31")).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Read(staff, $"/api/v1/policies?clientId={Guid.NewGuid()}")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1, (await Read(staff, $"/api/v1/clients/{first.ClientId}/records?kind=policy")).GetProperty("totalCount").GetInt32());
            Assert.Contains((await Read(staff, $"/api/v1/clients/{first.ClientId}/activity")).GetProperty("items").EnumerateArray(), x => x.GetProperty("eventType").GetString() == "policy.issued");
            var registration = await db.Set<PolicyRegistration>().Where(x => x.PolicyId == first.Id).Select(x => x.NormalizedRegistration).FirstAsync();
            Assert.True((await Read(staff, "/api/v1/policies?registration=" + registration)).GetProperty("totalCount").GetInt32() > 0);
            var shared = await Read(broker, "/api/v1/agency-context/policies"); Assert.Equal(1, shared.GetProperty("totalCount").GetInt32());
            Assert.Equal(first.Id, shared.GetProperty("items")[0].GetProperty("id").GetGuid());
            var detail = await Read(broker, $"/api/v1/agency-context/policies/{first.Id}");
            Assert.Equal(new[] { "clientName", "endsAt", "id", "productCode", "reference", "startsAt", "state" }, detail.EnumerateObject().Select(x => x.Name).Order().ToArray());
            Assert.Equal(0, (await Read(broker, "/api/v1/agency-context/policies?q=" + registration)).GetProperty("totalCount").GetInt32());
            Assert.Equal(0, (await Read(broker, "/api/v1/agency-context/policies?q=" + other.Reference)).GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await broker.GetAsync("/api/v1/agency-context/policies?registration=" + registration)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await broker.GetAsync($"/api/v1/agency-context/policies/{other.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await broker.GetAsync($"/api/v1/policies/{first.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/policies")).StatusCode);
            Assert.Equal(detail.GetRawText(), (await Read(staff, $"/api/v1/agencies/{first.AgencyId}/sharing/policies/{first.Id}")).GetRawText());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={first.RelationshipId}");
            Assert.Equal(0, (await Read(broker, "/api/v1/agency-context/policies")).GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.NotFound, (await broker.GetAsync($"/api/v1/agency-context/policies/{first.Id}")).StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={first.AgencyId}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await broker.GetAsync("/api/v1/agency-context/policies")).StatusCode);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Email=N'underwriter@cover.example'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/v1/policies")).StatusCode);
        });
    }
}

