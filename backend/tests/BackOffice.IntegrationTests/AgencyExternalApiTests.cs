using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyExternalApiTests
{
    [Fact]
    public async Task RealSqlAcceptedAgencyCookiesFenceReadsCommandsCursorsAndRevokedAuthority()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Guid.NewGuid().ToString("N") + "a1";
        var first = PartyDemoSeed.FirstAgencyId; var second = PartyDemoSeed.SecondAgencyId;
        try
        {
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password, includeSupportFlags: true);
                // Active aggregate fixtures; acceptance, credentials, cookies and subsequent commands use HTTP.
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={first} OR Id={second}");
            }
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection", connection.ConnectionString).UseSetting("Cover:DiagnosticWorkerEnabled", "false")
                .UseSetting("Cover:AgencyNotificationWorkerEnabled", "false").UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "external-api-keys", owned))));
            using var staff = host.CreateClient(); using var reviewer = host.CreateClient();
            using var broker = host.CreateClient(); using var foreign = host.CreateClient(); using var member = host.CreateClient(); using var backup = host.CreateClient();
            await Login(staff, "agency-admin@cover.example", password); await Login(reviewer, "agency-reviewer@cover.example", password);
            async Task<string> AgencyVersion(Guid id)
            {
                await using var db = new BackOfficeDbContext(options);
                return "\"" + Convert.ToBase64String(await db.Set<Agency>().Where(x => x.Id == id).Select(x => x.RowVersion).SingleAsync()) + "\"";
            }
            async Task<Guid> Accept(HttpClient client, Guid agency, string email, string role)
            {
                using var invite = await Send(staff, HttpMethod.Post, $"/api/v1/agencies/{agency}/invitations", new { email, displayName = "Fictional accepted " + role, role }, await AgencyVersion(agency));
                Assert.Equal(HttpStatusCode.Created, invite.StatusCode); var ids = await Json(invite); var id = ids.GetProperty("id").GetGuid(); var invitation = ids.GetProperty("invitationId").GetGuid();
                using var reveal = await Send(staff, HttpMethod.Post, $"/api/v1/invitations/{invitation}/demo-link", new { }); reveal.EnsureSuccessStatusCode();
                var token = (await Json(reveal)).GetProperty("invitationToken").GetString();
                using var acceptance = await Send(client, HttpMethod.Post, "/api/v1/auth/invitations/accept", new { invitationToken = token, password }); acceptance.EnsureSuccessStatusCode();
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/account")).StatusCode);
                var login = await Login(client, email, password); Assert.Equal("agency", login.GetProperty("user").GetProperty("scope").GetString());
                Assert.Equal(agency, login.GetProperty("user").GetProperty("agencyId").GetGuid()); return id;
            }
            var brokerId = await Accept(broker, first, "accepted-admin@example.test", "broker-admin");
            var foreignId = await Accept(foreign, second, "accepted-reader@example.test", "broker-readonly");
            await Accept(member, first, "accepted-user@example.test", "broker-user");
            var onlyAdmin = await Read(broker, $"/api/v1/agencies/{first}/users/{brokerId}");
            Assert.Equal(HttpStatusCode.Conflict, (await Send(broker, HttpMethod.Post, $"/api/v1/agencies/{first}/users/{brokerId}/deactivate", new { reason = "Cannot remove last administrator" }, onlyAdmin.GetProperty("etag").GetString())).StatusCode);
            await Accept(backup, first, "accepted-backup@example.test", "broker-admin");
            Assert.Equal("internal", (await Read(staff, "/api/v1/account")).GetProperty("scope").GetString());
            Assert.Equal(JsonValueKind.Null, (await Read(staff, "/api/v1/account")).GetProperty("agencyId").ValueKind);
            foreach (var client in new[] { broker, member, foreign })
            {
                var account = await Read(client, "/api/v1/account"); Assert.Equal("agency", account.GetProperty("scope").GetString());
                foreach (var path in new[] { "/api/v1/clients", "/api/v1/agencies", "/api/v1/agencies/kpis", $"/api/v1/jobs/{Guid.NewGuid()}", $"/api/v1/agencies/{first}/sharing", $"/api/v1/agencies/{first}/permission-matrix" })
                    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/agency-context")).StatusCode);
            Assert.Equal(first, (await Read(broker, "/api/v1/agency-context")).GetProperty("agency").GetProperty("id").GetGuid());
            Assert.Equal(second, (await Read(foreign, "/api/v1/agency-context")).GetProperty("agency").GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.BadRequest, (await broker.GetAsync($"/api/v1/agency-context?agencyId={second}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await broker.GetAsync($"/api/v1/agency-context/clients?agencyId={second}")).StatusCode);
            Assert.Equal(0, (await Read(broker, "/api/v1/agency-context/clients?q=Fictional%20Demo%20Traders%2005")).GetProperty("totalCount").GetInt32());
            var clients = await Read(broker, "/api/v1/agency-context/clients?pageSize=1");
            var cursor = Uri.EscapeDataString(clients.GetProperty("nextCursor").GetString()!);
            Assert.Single((await Read(broker, "/api/v1/agency-context/clients?pageSize=1&cursor=" + cursor)).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest, (await foreign.GetAsync("/api/v1/agency-context/clients?pageSize=1&cursor=" + cursor)).StatusCode);
            var ownRelationship = PartyDemoSeed.RelationshipId(3, 1); var foreignRelationship = PartyDemoSeed.RelationshipId(3, 2);
            var contacts = await Read(broker, $"/api/v1/agency-context/relationships/{ownRelationship}/contacts"); Assert.Equal(2, contacts.GetProperty("totalCount").GetInt32());
            Assert.DoesNotContain("marketingConsent", contacts.GetRawText());
            Assert.Equal(0, (await Read(broker, $"/api/v1/agency-context/relationships/{foreignRelationship}/contacts")).GetProperty("totalCount").GetInt32());
            var instructions = await Read(foreign, $"/api/v1/agency-context/relationships/{foreignRelationship}/instructions"); Assert.Single(instructions.GetProperty("items").EnumerateArray());
            Assert.DoesNotContain("reason", instructions.GetRawText()); Assert.DoesNotContain("internalInstruction", instructions.GetRawText());
            Assert.Equal(0, (await Read(foreign, $"/api/v1/agency-context/relationships/{ownRelationship}/instructions")).GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.Forbidden, (await broker.GetAsync($"/api/v1/agencies/{second}/users")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await broker.GetAsync($"/api/v1/agencies/{first}/users/{foreignId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"/api/v1/agencies/{first}/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync($"/api/v1/agencies/{second}/users")).StatusCode);
            var body = new { email = "own-command@example.test", displayName = "Fictional own command", role = "broker-user" };
            var key = Guid.NewGuid().ToString(); using var context = await broker.GetAsync("/api/v1/agency-context"); var version = context.Headers.ETag!.ToString();
            var createPath = $"/api/v1/agencies/{first}/invitations";
            using var created = await Send(broker, HttpMethod.Post, createPath, body, version, key); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var replay = await Send(broker, HttpMethod.Post, createPath, body, version, key); Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            var createdIds = await Json(created); var pending = createdIds.GetProperty("invitationId").GetGuid();
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(broker, HttpMethod.Post, $"/api/v1/invitations/{pending}/demo-link", new { })).StatusCode);
            var pendingRead = await Read(broker, $"/api/v1/agencies/{first}/invitations/{pending}");
            Assert.Equal(HttpStatusCode.NotFound, (await Send(backup, HttpMethod.Post, $"/api/v1/invitations/{Guid.NewGuid()}/resend", new { reason = "Unknown invitation" }, pendingRead.GetProperty("etag").GetString())).StatusCode);
            using var resent = await Send(broker, HttpMethod.Post, $"/api/v1/invitations/{pending}/resend", new { reason = "Fictional own resend" }, pendingRead.GetProperty("etag").GetString()); Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
            var replacement = (await Json(resent)).GetProperty("id").GetGuid();
            var replacementRead = await Read(broker, $"/api/v1/agencies/{first}/invitations/{replacement}");
            using var revokedInvite = await Send(broker, HttpMethod.Post, $"/api/v1/invitations/{replacement}/revoke", new { reason = "Fictional own revoke" }, replacementRead.GetProperty("etag").GetString()); revokedInvite.EnsureSuccessStatusCode();
            var managedId = createdIds.GetProperty("id").GetGuid(); var managedPath = $"/api/v1/agencies/{first}/users/{managedId}";
            var managed = await Read(broker, managedPath);
            using var deactivated = await Send(broker, HttpMethod.Post, managedPath + "/deactivate", new { reason = "Fictional own deactivation" }, managed.GetProperty("etag").GetString()); deactivated.EnsureSuccessStatusCode();
            managed = await Read(broker, managedPath);
            using var reactivated = await Send(broker, HttpMethod.Post, managedPath + "/reactivate", new { reason = "Fictional own reactivation" }, managed.GetProperty("etag").GetString()); reactivated.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(broker, HttpMethod.Post, $"/api/v1/agencies/{second}/invitations", body, version)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(broker, HttpMethod.Post, createPath, new { email = "elevate@example.test", displayName = "Denied", role = "system-admin" }, await AgencyVersion(first))).StatusCode);
            var permissionPath = $"/api/v1/agencies/{first}/permission-requests";
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(broker, HttpMethod.Post, permissionPath, new { permission = "platform-admin", reason = "Denied escalation" }, await AgencyVersion(first))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(member, HttpMethod.Post, permissionPath, new { permission = "bordereau-download", reason = "Denied ordinary user request" }, await AgencyVersion(first))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(broker, HttpMethod.Post, $"/api/v1/agencies/{second}/permission-requests", new { permission = "bordereau-download", reason = "Denied foreign request" }, await AgencyVersion(second))).StatusCode);
            using var requested = await Send(broker, HttpMethod.Post, permissionPath, new { permission = "bordereau-download", reason = "Fictional own request" }, await AgencyVersion(first)); requested.EnsureSuccessStatusCode();
            var requestId = (await Json(requested)).GetProperty("id").GetGuid();
            var request = (await Read(staff, permissionPath)).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == requestId);
            var decisionPath = permissionPath + $"/{requestId}/decision";
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(broker, HttpMethod.Post, decisionPath, new { outcome = "approve", reason = "Denied self approval" }, request.GetProperty("etag").GetString())).StatusCode);
            using var approval = await Send(reviewer, HttpMethod.Post, decisionPath, new { outcome = "approve", reason = "Fictional independent approval" }, request.GetProperty("etag").GetString()); approval.EnsureSuccessStatusCode();
            Assert.True((await Read(broker, "/api/v1/agency-context")).GetProperty("permissions")[0].GetProperty("granted").GetBoolean());
            var grant = (await Read(staff, $"/api/v1/agencies/{first}/permission-grants")).GetProperty("items")[0];
            using var revoke = await Send(reviewer, HttpMethod.Post, $"/api/v1/agencies/{first}/permission-grants/{grant.GetProperty("id").GetGuid()}/revoke", new { reason = "Fictional permission revoked" }, grant.GetProperty("etag").GetString()); revoke.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await broker.GetAsync("/api/v1/agency-context")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(broker, HttpMethod.Post, createPath, body, version, key)).StatusCode);
            await Login(broker, "accepted-admin@example.test", password);
            Assert.False((await Read(broker, "/api/v1/agency-context")).GetProperty("permissions")[0].GetProperty("granted").GetBoolean());
            var selfPath = $"/api/v1/agencies/{first}/users/{brokerId}"; var self = await Read(broker, selfPath);
            using var demote = await Send(broker, HttpMethod.Put, selfPath, new { displayName = "Fictional demoted", role = "broker-user", reason = "Fictional own demotion with another active admin" }, self.GetProperty("etag").GetString()); demote.EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Unauthorized, (await broker.GetAsync("/api/v1/account")).StatusCode);
            await Login(broker, "accepted-admin@example.test", password);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(broker, HttpMethod.Post, createPath, body, version, key)).StatusCode);
            await Read(broker, "/api/v1/agency-context");
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={second}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await foreign.GetAsync("/api/v1/agency-context")).StatusCode);
            using var disabled = await Send(foreign, HttpMethod.Post, "/api/v1/auth/login", new { email = "accepted-reader@example.test", password }); Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
        }
        finally { if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed."); await using var db = new BackOfficeDbContext(options); await db.Database.EnsureDeletedAsync(); }
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<JsonElement> Read(HttpClient client, string path) { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return await Json(response); }
    private static async Task<JsonElement> Login(HttpClient client, string email, string password) { using var response = await Send(client, HttpMethod.Post, "/api/v1/auth/login", new { email, password }); response.EnsureSuccessStatusCode(); return await Json(response); }
    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object body, string? version = null, string? key = null)
    {
        var csrf = (await Read(client, "/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf); request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        if (version != null) request.Headers.Add("If-Match", version);
        return await client.SendAsync(request);
    }
}
