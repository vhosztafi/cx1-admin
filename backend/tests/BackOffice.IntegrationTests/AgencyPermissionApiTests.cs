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

public sealed class AgencyPermissionApiTests
{
    [Fact]
    public async Task RealSqlPermissionApiUsesCurrentRolesVersionedReceiptsAndAuthorityBoundCursors()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Guid.NewGuid().ToString("N") + "a1";
        var path = "/api/v1/agencies/" + PartyDemoSeed.FirstAgencyId; var other = "/api/v1/agencies/" + PartyDemoSeed.SecondAgencyId;
        string adminLabel, reviewerLabel;
        try
        {
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password);
                adminLabel = await db.Set<StaffUser>().Where(x => x.Email == "agency-admin@cover.example").Select(x => x.DisplayName).SingleAsync();
                reviewerLabel = await db.Set<StaffUser>().Where(x => x.Email == "agency-reviewer@cover.example").Select(x => x.DisplayName).SingleAsync();
                foreach (var agency in await db.Set<Agency>().Where(x => x.Id == PartyDemoSeed.FirstAgencyId || x.Id == PartyDemoSeed.SecondAgencyId).ToListAsync()) agency.State = "active";
                await db.SaveChangesAsync();
            }
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Cover:SqlConnection", connection.ConnectionString).UseSetting("Cover:DataProtectionPath", Path.GetFullPath(Path.Combine(".local", "permission-api-test-keys", owned))));
            using var anonymous = host.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path + "/permission-requests")).StatusCode);
            using var admin = host.CreateClient(); var csrf = await Login(admin, "agency-admin", password);
            using var reviewer = host.CreateClient(); var reviewerCsrf = await Login(reviewer, "agency-reviewer", password);
            using var limited = host.CreateClient(); await Login(limited, "underwriter", password);
            Assert.Equal(HttpStatusCode.Forbidden, (await limited.GetAsync(path + "/permission-grants")).StatusCode);
            Assert.Equal(0, (await Read(admin, path + "/permission-requests")).GetProperty("totalCount").GetInt32());
            var requestBody = new { permission = "bordereau-download", reason = "Fictional reporting requirement" };
            var basis = await Tag(admin, path); var requests = path + "/permission-requests";
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, null, requests, requestBody, basis)).StatusCode);
            Assert.Equal((HttpStatusCode)428, (await Send(admin, csrf, requests, requestBody, null)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Send(admin, csrf, requests, new { permission = "system-admin", reason = "Forged" }, basis)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, csrf, requests, new { permission = "bordereau-download", reason = "Forged", requestedBy = Guid.NewGuid() }, basis)).StatusCode);
            var key = Guid.NewGuid().ToString("N"); using var created = await Send(admin, csrf, requests, requestBody, basis, key);
            Assert.Equal(HttpStatusCode.Accepted, created.StatusCode); var receipt = await created.Content.ReadFromJsonAsync<JsonElement>(); Assert.Single(receipt.EnumerateObject());
            Assert.NotEqual(basis, created.Headers.ETag!.ToString());
            using var replay = await Send(admin, csrf, requests, requestBody, basis, key); Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            var first = (await Read(admin, requests)).GetProperty("items")[0]; var firstDecision = requests + "/" + first.GetProperty("id").GetGuid() + "/decision";
            Assert.Equal(adminLabel, first.GetProperty("requestedByLabel").GetString());
            Assert.False(first.TryGetProperty("decisionByLabel", out _));
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(admin, csrf, firstDecision, new { outcome = "approve", reason = "Self" }, first.GetProperty("etag").GetString())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, reviewerCsrf, firstDecision, new { outcome = "reject", reason = "Not needed yet" }, first.GetProperty("etag").GetString())).StatusCode);
            using var secondCreated = await Send(admin, csrf, requests, requestBody, await Tag(admin, path)); Assert.Equal(HttpStatusCode.Accepted, secondCreated.StatusCode);
            var second = (await Read(admin, requests)).GetProperty("items")[0]; var secondDecision = requests + "/" + second.GetProperty("id").GetGuid() + "/decision";
            var approve = new { outcome = "approve", reason = "Reviewed independently" }; var approvalKey = Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.PreconditionFailed, (await Send(reviewer, reviewerCsrf, secondDecision, approve, "\"AAAAAAAAAAA=\"")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(reviewer, reviewerCsrf, other + "/permission-requests/" + second.GetProperty("id").GetGuid() + "/decision", approve, second.GetProperty("etag").GetString())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(reviewer, reviewerCsrf, secondDecision, approve, second.GetProperty("etag").GetString(), approvalKey)).StatusCode);
            var list = await Read(admin, requests + "?pageSize=1"); Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
            Assert.Equal(reviewerLabel, list.GetProperty("items")[0].GetProperty("decisionByLabel").GetString());
            var rejected = (await Read(admin, requests)).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("state").GetString() == "rejected");
            Assert.Equal(reviewerLabel, rejected.GetProperty("decisionByLabel").GetString());
            var cursor = Uri.EscapeDataString(list.GetProperty("nextCursor").GetString()!); var next = requests + "?pageSize=1&cursor=" + cursor;
            Assert.Equal(first.GetProperty("id").GetGuid(), (await Read(admin, next)).GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.GetAsync(next)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(other + "/permission-requests?pageSize=1&cursor=" + cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(requests + "?pageSize=2&cursor=" + cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(requests + "?agencyId=" + Guid.NewGuid())).StatusCode);
            var grant = (await Read(admin, path + "/permission-grants")).GetProperty("items")[0]; Assert.False(grant.TryGetProperty("revokedAt", out _));
            Assert.Equal(reviewerLabel, grant.GetProperty("grantedByLabel").GetString());
            Assert.False(grant.TryGetProperty("revokedByLabel", out _));
            Assert.False(grant.TryGetProperty("email", out _));
            var revokePath = path + "/permission-grants/" + grant.GetProperty("id").GetGuid() + "/revoke";
            Assert.Equal(HttpStatusCode.OK, (await Send(admin, csrf, revokePath, new { reason = "Access withdrawn" }, grant.GetProperty("etag").GetString())).StatusCode);
            var revoked = (await Read(admin, path + "/permission-grants")).GetProperty("items")[0];
            Assert.Equal(adminLabel, revoked.GetProperty("revokedByLabel").GetString());
            Assert.Equal(reviewerLabel, revoked.GetProperty("grantedByLabel").GetString());
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(next)).StatusCode); // Authority changed after cursor creation.
            Assert.Equal("Access withdrawn", (await Read(admin, path + "/permission-grants")).GetProperty("items")[0].GetProperty("revocationReason").GetString());
            Assert.Equal(0, (await Read(admin, other + "/permission-grants")).GetProperty("totalCount").GetInt32());
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.Equal(5, await db.Set<IdempotencyRecord>().CountAsync(x => x.Route.Contains("/permission-")));
                var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-reviewer@cover.example");
                var role = await db.Set<UserRole>().SingleAsync(x => x.UserId == user.Id); role.RoleId = await db.Set<Role>().Where(x => x.Code == "underwriter").Select(x => x.Id).SingleAsync(); await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.GetAsync(requests)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(reviewer, reviewerCsrf, secondDecision, approve, second.GetProperty("etag").GetString(), approvalKey)).StatusCode);
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static async Task<JsonElement> Read(HttpClient client, string path) { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task<string> Tag(HttpClient client, string path) { using var response = await client.GetAsync(path); response.EnsureSuccessStatusCode(); return response.Headers.ETag!.ToString(); }
    private static async Task<string> Login(HttpClient client, string role, string password)
    {
        var token = (await Read(client, "/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = role + "@cover.example", password }) }; request.Headers.Add("X-CSRF-Token", token);
        using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode(); return (await Read(client, "/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, string? csrf, string path, object body, string? etag, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) }; if (csrf != null) request.Headers.Add("X-CSRF-Token", csrf);
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N")); if (etag != null) request.Headers.Add("If-Match", etag); return client.SendAsync(request);
    }
}
