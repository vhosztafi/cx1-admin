using System.Security.Cryptography;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyPermissionCommandTests
{
    [Fact]
    public async Task RealSqlPermissionCommandsReauthorizeReplayAndRevokeAgencySessionsAtomically()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var service = new AgencyPermissionService(new SqlCommandBoundary(new Factory(options), TimeProvider.System), TimeProvider.System);
        try
        {
            var agency = PartyDemoSeed.FirstAgencyId; var other = PartyDemoSeed.SecondAgencyId;
            Guid brokerId, adminId, reviewerId; string originalStamp; byte[] agencyVersion;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, "Demo!" + Guid.NewGuid().ToString("N") + "a1");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency} OR Id={other}");
                adminId = await db.Set<StaffUser>().Where(x => x.Email == "agency-admin@cover.example").Select(x => x.Id).SingleAsync();
                reviewerId = await db.Set<StaffUser>().Where(x => x.Email == "agency-reviewer@cover.example").Select(x => x.Id).SingleAsync();
                var user = new StaffUser { AgencyId = agency, State = "invited", Email = "permission-broker@example.test", NormalizedEmail = "PERMISSION-BROKER@EXAMPLE.TEST", DisplayName = "Fictional permission broker" };
                db.Add(user); await db.SaveChangesAsync(); brokerId = user.Id;
                db.Add(new UserRole { UserId = user.Id, RoleId = await db.Set<Role>().Where(x => x.Code == "broker-admin").Select(x => x.Id).SingleAsync() }); await db.SaveChangesAsync(); user.State = "active"; await db.SaveChangesAsync(); originalStamp = user.SecurityStamp;
                db.Add(new UserSession { UserId = user.Id, TokenHash = RandomNumberGenerator.GetBytes(32), SecurityStamp = originalStamp, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), LastSeenAt = DateTimeOffset.UtcNow, TicketCiphertext = [1] }); await db.SaveChangesAsync();
                agencyVersion = (await db.Set<Agency>().AsNoTracking().SingleAsync(x => x.Id == agency)).RowVersion;
            }
            var broker = new ActorContext(brokerId, null, agency, new HashSet<string> { "broker-admin" });
            var admin = new ActorContext(adminId, null, null, new HashSet<string> { "agency-admin" });
            var reviewer = admin with { UserId = reviewerId };
            async Task Denied(Func<Task<CommandOutcome>> command, int status = 403) => Assert.Equal(status, (await Assert.ThrowsAsync<AgencyCommandException>(command)).Status);
            Task<CommandOutcome> Request() => service.Request(broker, agency, "request-once", agencyVersion, "bordereau-download", "Fictional reporting need");
            await Denied(() => service.Request(broker, other, "wrong-agency", agencyVersion, "bordereau-download", "Reason"));
            await Denied(() => service.Request(broker with { Roles = new HashSet<string> { "broker-readonly" } }, agency, "wrong-role", agencyVersion, "bordereau-download", "Reason"));
            var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Request()));
            Assert.Equal(2, responses.Count(x => x.Replayed)); Assert.Single(responses.Select(x => x.ResourceId).Distinct());
            var requestId = responses[0].ResourceId;
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => service.Request(broker, agency, "request-once", agencyVersion, "bordereau-download", "Changed intent"));
            // The pre-replay authorization callback must run even with the exact saved key.
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={brokerId}");
            await Denied(Request);
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={brokerId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agency}");
            }
            await Denied(Request);
            byte[] requestVersion;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency}");
                requestVersion = (await db.Set<AgencyPermissionRequest>().SingleAsync(x => x.Id == requestId)).RowVersion;
                Assert.Single(await db.Set<IdempotencyRecord>().ToListAsync());
            }
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId=(SELECT Id FROM Role WHERE Code=N'broker-readonly') WHERE UserId={brokerId}");
            await Denied(Request);
            await Denied(() => service.Request(broker with { Roles = new HashSet<string> { "broker-readonly" } }, agency, "readonly-current", agencyVersion, "bordereau-download", "Reason"));
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId=(SELECT Id FROM Role WHERE Code=N'broker-admin') WHERE UserId={brokerId}");
            await Denied(() => service.Decide(broker, agency, requestId, "self", requestVersion, "approve", "Reason"));
            await Denied(() => service.Decide(reviewer, other, requestId, "cross-agency", requestVersion, "approve", "Reason"), 404);
            await Denied(() => service.Decide(reviewer, agency, requestId, "stale", new byte[8], "approve", "Reason"), 412);
            var approved = await service.Decide(reviewer, agency, requestId, "approve-once", requestVersion, "approve", "Reviewed independently");
            Assert.False(approved.Replayed);
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={reviewerId}");
            await Denied(() => service.Decide(reviewer, agency, requestId, "approve-once", requestVersion, "approve", "Reviewed independently"));
            Guid grantId; byte[] grantVersion;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={reviewerId}");
                var grant = Assert.Single(await db.Set<AgencyPermissionGrant>().ToListAsync()); grantId = grant.Id; grantVersion = grant.RowVersion;
                Assert.Equal(requestId, grant.RequestId); Assert.Equal(reviewerId, grant.GrantedBy);
            }
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId=(SELECT Id FROM Role WHERE Code=N'servicing') WHERE UserId={reviewerId}");
            await Denied(() => service.Decide(reviewer, agency, requestId, "approve-once", requestVersion, "approve", "Reviewed independently"));
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId=(SELECT Id FROM Role WHERE Code=N'agency-admin') WHERE UserId={reviewerId}");
            Assert.True((await service.Decide(reviewer, agency, requestId, "approve-once", requestVersion, "approve", "Reviewed independently")).Replayed);
            await Denied(() => service.Revoke(broker, agency, grantId, "broker-revoke", grantVersion, "Reason"));
            await Denied(() => service.Revoke(admin, agency, grantId, "stale-revoke", new byte[8], "Reason"), 412);
            var revoked = await service.Revoke(admin, agency, grantId, "revoke-once", grantVersion, "Permission withdrawn");
            Assert.True((await service.Revoke(admin, agency, grantId, "revoke-once", grantVersion, "Permission withdrawn")).Replayed);
            Assert.Equal(200, revoked.Status);
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.NotNull((await db.Set<UserSession>().SingleAsync(x => x.UserId == brokerId)).RevokedAt);
                Assert.NotEqual(originalStamp, (await db.Set<StaffUser>().SingleAsync(x => x.Id == brokerId)).SecurityStamp);
                var grant = await db.Set<AgencyPermissionGrant>().SingleAsync(); Assert.Equal(adminId, grant.RevokedBy); Assert.Equal("Permission withdrawn", grant.RevocationReason);
                Assert.Equal(3, await db.Set<IdempotencyRecord>().CountAsync());
                Assert.Equal(3, await db.Set<AuditEvent>().CountAsync(x => x.EventType.StartsWith("agency.permission-")));
                Assert.Equal(3, await db.Set<AgencyActivity>().CountAsync(x => x.AgencyId == agency && x.Action.StartsWith("agency.permission-")));
            }
            // Internal requesters also need a different decision maker.
            byte[] otherVersion;
            await using (var db = new BackOfficeDbContext(options)) otherVersion = (await db.Set<Agency>().SingleAsync(x => x.Id == other)).RowVersion;
            var internalRequest = await service.Request(admin, other, "internal-request", otherVersion, "bordereau-download", "Fictional need");
            await using (var db = new BackOfficeDbContext(options)) requestVersion = (await db.Set<AgencyPermissionRequest>().SingleAsync(x => x.Id == internalRequest.ResourceId)).RowVersion;
            await Denied(() => service.Decide(admin, other, internalRequest.ResourceId, "internal-self", requestVersion, "approve", "Self approval"));
            await service.Decide(reviewer, other, internalRequest.ResourceId, "reject-once", requestVersion, "reject", "Not needed");
            await using (var db = new BackOfficeDbContext(options)) Assert.False(await db.Set<AgencyPermissionGrant>().AnyAsync(x => x.AgencyId == other));
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(options);
    }
}
