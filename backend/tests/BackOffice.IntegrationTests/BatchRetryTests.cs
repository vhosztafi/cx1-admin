using System.Net;
using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BackOffice.IntegrationTests.OperationalJobTests;

namespace BackOffice.IntegrationTests;

public sealed class BatchRetryTests
{
    [Fact]
    public async Task RealSqlBatchRollsBackStaleSelectionAndReplaysOneAtomicRecovery()
    {
        const string route = "/api/v1/admin/jobs/retry-batch";
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = ownedName; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        var keys = Path.GetFullPath(Path.Combine(".local", "batch-test-keys", ownedName));
        try
        {
            var selection = new List<Selection>();
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, password);
                var actor = await db.Set<StaffUser>().SingleAsync(x => x.Email == "system-admin@cover.example");
                var scenario = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "diagnostic-probe/success");
                for (var i = 1; i <= 2; i++) db.Add(new OutboxWork {Id = Guid.Parse("00000000-0000-0000-0000-00000000000" + i),
                    Kind = "diagnostic-probe", OperationKey = "batch-" + i, ScenarioVersionId = scenario.Id, CreatedBy = actor.Id,
                    State = "failed", Attempts = 6, ErrorCode = "provider-timeout", NextAttemptAt = DateTimeOffset.UtcNow});
                await db.SaveChangesAsync();
                foreach (var job in await db.Set<OutboxWork>().ToListAsync()) selection.Add(new Selection(job.Id, "\"" + Convert.ToBase64String(job.RowVersion) + "\""));
                selection = selection.OrderBy(x => x.JobId).ToList();
            }
            using var factory = Factory(connection.ConnectionString, keys);
            using var admin = factory.CreateClient(); using var servicing = factory.CreateClient();
            var csrf = await Login(admin, "system-admin", password); var deniedCsrf = await Login(servicing, "servicing", password);
            var key = Guid.NewGuid().ToString("N");
            var body = new {jobs = selection, reason = "Recover the selected demo jobs"};
            using (var response = await Post(servicing, deniedCsrf, key, body, route)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using (var response = await Post(admin, csrf, key, new {jobs = new[] {selection[0], selection[0]}, reason = body.reason}, route)) Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using (var response = await Post(admin, csrf, key, new {jobs = Array.Empty<Selection>(), reason = body.reason}, route)) Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            using (var response = await Post(admin, csrf, key, new {jobs = new[] {selection[0], selection[1] with {Etag = "\"AAAAAAAAAAA=\""}}, reason = body.reason}, route)) Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.All(await db.Set<OutboxWork>().ToListAsync(), x => {Assert.Equal("failed", x.State); Assert.Equal(6, x.AttemptLimit);});
                Assert.Equal(0, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "diagnostic.retry-authorized"));
                Assert.Equal(0, await db.Set<IdempotencyRecord>().CountAsync());
            }
            var responses = await Task.WhenAll(Post(admin, csrf, key, body, route), Post(admin, csrf, key, new {jobs = selection.AsEnumerable().Reverse().ToArray(), reason = body.reason}, route));
            try
            {
                foreach (var response in responses) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(await responses[0].Content.ReadAsStringAsync(), await responses[1].Content.ReadAsStringAsync());
            }
            finally {foreach (var response in responses) response.Dispose();}
            using (var response = await Post(admin, csrf, key, new {jobs = new[] {selection[0]}, reason = body.reason}, route)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using (var response = await Post(admin, csrf, Guid.NewGuid().ToString("N"), body, route)) Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.All(await db.Set<OutboxWork>().ToListAsync(), x => {Assert.Equal("pending", x.State); Assert.Equal(12, x.AttemptLimit); Assert.Equal(6, x.Attempts);});
                Assert.Equal(2, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "diagnostic.retry-authorized"));
                Assert.Equal(1, await db.Set<AuditEvent>().CountAsync(x => x.EventType == "diagnostic.batch-retry-requested"));
                Assert.Equal(1, await db.Set<IdempotencyRecord>().CountAsync());
            }
        }
        finally
        {
            if (connection.InitialCatalog != ownedName) throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed record Selection(Guid JobId, string Etag);
}
