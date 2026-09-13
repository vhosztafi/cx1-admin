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
                Assert.True(names.SetEquals(new[] { "id", "kind", "state", "attempts", "nextAttemptAt", "errorCode", "attemptLimit", "retryAllowed" }));
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

            await using (var db = new BackOfficeDbContext(options))
            {
                var scenario = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "diagnostic-probe/success");
                for (var index = 0; index < 3; index++) db.Add(new OutboxWork
                {
                    Kind = "diagnostic-probe", OperationKey = "list-" + index, ScenarioVersionId = scenario.Id, CreatedBy = servicingId,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), NextAttemptAt = DateTimeOffset.UtcNow,
                    State = index == 2 ? "failed" : "pending", Payload = "{\"internal\":\"must-not-be-returned\"}"
                });
                db.Add(new AuditEvent {ActorId = servicingId, SubjectRecordId = jobId, EventType = "diagnostic.completed", OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    CorrelationId = Guid.NewGuid(), Reason = "internal-sensitive-reason", After = "{\"secret\":\"must-not-be-returned\"}"});
                db.Add(new AuditEvent {EventType = "unreviewed-event", OccurredAt = DateTimeOffset.UtcNow, CorrelationId = Guid.NewGuid()});
                await db.SaveChangesAsync();
            }
            foreach (var path in new[] {"/api/v1/admin/jobs", "/api/v1/admin/audit"})
                using (var response = await servicing.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            string cursor;
            Guid firstId;
            using (var response = await admin.GetAsync("/api/v1/admin/jobs?pageSize=1&state=pending"))
            {
                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("must-not-be-returned", text);
                using var json = JsonDocument.Parse(text);
                Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
                firstId = json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
                cursor = Uri.EscapeDataString(json.RootElement.GetProperty("nextCursor").GetString()!);
            }
            using (var response = await admin.GetAsync("/api/v1/admin/jobs?pageSize=1&state=pending&cursor=" + cursor))
            {
                response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.NotEqual(firstId, json.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid());
                Assert.False(json.RootElement.TryGetProperty("nextCursor", out _));
            }
            foreach (var path in new[] {"/api/v1/admin/jobs?cursor=broken", "/api/v1/admin/jobs?pageSize=101", "/api/v1/admin/jobs?state=unknown", "/api/v1/admin/jobs?state=", "/api/v1/admin/audit?eventType=", "/api/v1/admin/jobs?unknown=1",
                "/api/v1/admin/jobs?state=pending&state=failed", "/api/v1/admin/jobs?pageSize=1&state=failed&cursor=" + cursor,
                "/api/v1/admin/audit?pageSize=1&cursor=" + cursor, "/api/v1/admin/audit?from=not-a-date", "/api/v1/admin/audit?from=2026-09-13T00:00:00", "/api/v1/admin/audit?actorId=invalid"})
                using (var response = await admin.GetAsync(path)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using (var response = await admin.GetAsync("/api/v1/admin/jobs?kind=email"))
            {
                response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(0, json.RootElement.GetProperty("totalCount").GetInt32());
            }
            using (var response = await admin.GetAsync("/api/v1/admin/audit?eventType=diagnostic.completed&from=2020-01-01T00:00:00Z&to=2099-01-01T00:00:00Z&subjectRecordId=" + jobId))
            {
                response.EnsureSuccessStatusCode(); var text = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("internal-sensitive", text); Assert.DoesNotContain("must-not-be-returned", text);
                using var json = JsonDocument.Parse(text); Assert.Equal(1, json.RootElement.GetProperty("totalCount").GetInt32());
                Assert.Equal("Demo probe completed.", json.RootElement.GetProperty("items")[0].GetProperty("summary").GetString());
            }
            using (var response = await admin.GetAsync("/api/v1/admin/audit?eventType=unreviewed-event"))
            {
                response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(0, json.RootElement.GetProperty("totalCount").GetInt32());
            }
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.Equal(3, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "operations.jobs-read"));
                Assert.Equal(2, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "operations.audit-read"));
            }

            using var production = Factory(connection.ConnectionString, keys, "Production");
            Guid retryId;
            string originalKey;
            await using (var db = new BackOfficeDbContext(options))
            {
                var job = await db.Set<OutboxWork>().SingleAsync(x => x.OperationKey == "list-0");
                retryId = job.Id; originalKey = job.OperationKey;
                job.State = "failed"; job.Attempts = 6; job.ErrorCode = "provider-timeout"; job.CompletedAt = DateTimeOffset.UtcNow;
                for (var attempt = 1; attempt <= 6; attempt++) db.Add(new AdapterAttempt {WorkId = job.Id, AttemptNumber = attempt,
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1), EndedAt = DateTimeOffset.UtcNow, Outcome = "transient-failure", ErrorCode = "provider-timeout"});
                await db.SaveChangesAsync();
            }
            string etag;
            using (var response = await admin.GetAsync("/api/v1/jobs/" + retryId)) {response.EnsureSuccessStatusCode(); etag = response.Headers.ETag!.ToString();}
            var retryPath = "/api/v1/jobs/" + retryId + "/retry";
            var retryKey = Guid.NewGuid().ToString("N");
            using (var response = await Post(servicing, servicingCsrf, retryKey, new {reason = "Recover demo timeout"}, retryPath, etag)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await Post(admin, adminCsrf, retryKey, new {reason = "Recover demo timeout"}, retryPath)) Assert.Equal((HttpStatusCode)428, response.StatusCode);
            using (var response = await Post(admin, adminCsrf, retryKey, new {reason = "Recover demo timeout"}, retryPath, "\"AAAAAAAAAAA=\"")) Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            string accepted;
            using (var response = await Post(admin, adminCsrf, retryKey, new {reason = "Recover demo timeout"}, retryPath, etag))
            {Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); accepted = await response.Content.ReadAsStringAsync();}
            using (var response = await Post(admin, adminCsrf, retryKey, new {reason = "Recover demo timeout"}, retryPath, etag))
            {Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.Equal(accepted, await response.Content.ReadAsStringAsync());}
            using (var response = await Post(admin, adminCsrf, retryKey, new {reason = "Changed intent"}, retryPath, etag)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await using (var db = new BackOfficeDbContext(options))
            {
                var job = await db.Set<OutboxWork>().SingleAsync(x => x.Id == retryId);
                Assert.Equal(12, job.AttemptLimit); Assert.Equal(6, job.Attempts); Assert.Equal(originalKey, job.OperationKey); Assert.Equal("pending", job.State);
                Assert.Null(job.CompletedAt); Assert.Null(job.ErrorCode);
                Assert.Equal(6, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == retryId));
                var audit = await db.Set<AuditEvent>().SingleAsync(x => x.EventType == "diagnostic.retry-authorized");
                Assert.Equal("Recover demo timeout", audit.Reason);
                // A definitive provider rejection never becomes a new effect via recovery.
                job.State = "failed"; job.Attempts = 12; job.ErrorCode = "provider-rejected"; await db.SaveChangesAsync();
            }
            using (var response = await admin.GetAsync("/api/v1/jobs/" + retryId)) {response.EnsureSuccessStatusCode(); etag = response.Headers.ETag!.ToString();}
            using (var response = await Post(admin, adminCsrf, Guid.NewGuid().ToString("N"), new {reason = "Try rejected work"}, retryPath, etag)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

    internal static WebApplicationFactory<Program> Factory(string connection, string keys, string environment = "Development") => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment(environment).UseSetting("Cover:SqlConnection", connection)
            .UseSetting("Cover:DataProtectionPath", keys).UseSetting("Cover:DiagnosticWorkerEnabled", "false"));

    private static async Task<string> Csrf(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf"); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("requestToken").GetString()!;
    }

    internal static async Task<string> Login(HttpClient client, string role, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = role + "@cover.example", password }) };
        request.Headers.Add("X-CSRF-Token", await Csrf(client));
        using var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        return await Csrf(client);
    }

    internal static async Task<HttpResponseMessage> Post(HttpClient client, string? csrf, string? key, object body, string path = Probe, string? etag = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (etag is not null) request.Headers.Add("If-Match", etag);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
}
