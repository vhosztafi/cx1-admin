using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalJobTests
{
    private const string Probe = "/api/v1/admin/diagnostic-probes";

    [Fact]
    public async Task RealSqlProbeApiAuthorizesBeforeReplayAndRedactsJobReads()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = ownedName; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        var keys = Path.GetFullPath(Path.Combine(".local", "operational-test-keys", ownedName));
        try
        {
            await using (var db = new BackOfficeDbContext(options)) { await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password); }
            using var factory = Factory(connection.ConnectionString, keys);
            using var anonymous = factory.CreateClient();
            using var denied = await anonymous.GetAsync("/api/v1/jobs/" + Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var admin = factory.CreateClient();
            using var servicing = factory.CreateClient();
            var adminCsrf = await Login(admin, "system-admin", password);
            var servicingCsrf = await Login(servicing, "servicing", password);
            var key = Guid.NewGuid().ToString("N");
            using (var response = await Post(servicing, servicingCsrf, key, new { scenario = "success" })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await Post(admin, null, key, new { scenario = "success" })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await Post(admin, adminCsrf, null, new { scenario = "success" })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await Post(admin, adminCsrf, key, new { scenario = "unsupported" })) Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using (var response = await Post(admin, adminCsrf, key, new { scenario = "success", actorId = Guid.NewGuid() })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var responses = await Task.WhenAll(Post(admin, adminCsrf, key, new { scenario = "success" }), Post(admin, adminCsrf, key, new { scenario = "success" }));
            Guid jobId;
            try
            {
                foreach (var response in responses) Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                var body = await responses[0].Content.ReadAsStringAsync();
                Assert.Equal(body, await responses[1].Content.ReadAsStringAsync());
                jobId = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
                Assert.Equal("/api/v1/jobs/" + jobId, responses[0].Headers.Location!.ToString());
            }
            finally { foreach (var response in responses) response.Dispose(); }
            using (var response = await Post(admin, adminCsrf, key, new { scenario = "reject" })) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var route = "/api/v1/jobs/" + jobId;
            using (var response = await servicing.GetAsync(route)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Guid adminId, servicingId, roleId;
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.Equal(1, await db.Set<OutboxWork>().CountAsync());
                Assert.Equal(1, await db.Set<IdempotencyRecord>().CountAsync());
                Assert.Equal(1, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "diagnostic.requested"));
                adminId = (await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example")).Id;
                servicingId = (await db.Set<StaffUser>().SingleAsync(x => x.Email == "servicing@cover.example")).Id;
                roleId = (await db.Set<Role>().SingleAsync(x => x.Code == "system-admin")).Id;
                db.Remove(await db.Set<UserRole>().SingleAsync(x => x.UserId == adminId && x.RoleId == roleId));
                await db.SaveChangesAsync();
            }
            // Live role revalidation must also protect already-recorded command responses.
            using (var response = await Post(admin, adminCsrf, key, new { scenario = "success" })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await using (var db = new BackOfficeDbContext(options))
            {
                db.Add(new UserRole { UserId = adminId, RoleId = roleId });
                var job = await db.Set<OutboxWork>().SingleAsync();
                job.CreatedBy = servicingId; job.ErrorCode = "internal-sensitive-error";
                await db.SaveChangesAsync();
            }
            using (var response = await servicing.GetAsync(route))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.NotNull(response.Headers.ETag);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("job-failed", json.RootElement.GetProperty("errorCode").GetString());
                var names = json.RootElement.EnumerateObject().Select(x => x.Name).ToHashSet();
                Assert.True(names.SetEquals(new[] { "id", "kind", "state", "attempts", "nextAttemptAt", "errorCode" }));
            }
            using (var response = await admin.GetAsync(route)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using (var db = new BackOfficeDbContext(options))
            {
                var audit = await db.Set<AuditEvent>().SingleAsync(x => x.EventType == "diagnostic.inspected");
                Assert.Equal(adminId, audit.ActorId); Assert.Equal(jobId, audit.SubjectRecordId); Assert.Null(audit.After);
                var job = await db.Set<OutboxWork>().SingleAsync(); job.Kind = "email"; await db.SaveChangesAsync();
            }
            // Future business adapters require their own subject authorization implementation.
            using (var response = await admin.GetAsync(route)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            using (var response = await servicing.GetAsync(route)) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            using var production = Factory(connection.ConnectionString, keys, "Production");
            using var productionAdmin = production.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var productionCsrf = await Login(productionAdmin, "system-admin", password);
            using (var response = await Post(productionAdmin, productionCsrf, Guid.NewGuid().ToString("N"), new { scenario = "success" })) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            if (connection.InitialCatalog != ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static WebApplicationFactory<Program> Factory(string connection, string keys, string environment = "Development") => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment(environment).UseSetting("Cover:SqlConnection", connection)
            .UseSetting("Cover:DataProtectionPath", keys).UseSetting("Cover:DiagnosticWorkerEnabled", "false"));

    private static async Task<string> Csrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    private static async Task<string> Login(HttpClient client, string role, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = role + "@cover.example", password }) };
        request.Headers.Add("X-CSRF-Token", await Csrf(client));
        using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        return await Csrf(client);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string? csrf, string? key, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Probe) { Content = JsonContent.Create(body) };
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
}
