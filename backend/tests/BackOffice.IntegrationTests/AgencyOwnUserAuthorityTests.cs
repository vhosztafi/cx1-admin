using System.Security.Cryptography;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyOwnUserAuthorityTests
{
    [Fact]
    public async Task RealSqlOwnUserCommandsRecheckAgencyAndStoredRoleBeforeReceiptReplay()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var agencyId = PartyDemoSeed.FirstAgencyId; var foreignId = PartyDemoSeed.SecondAgencyId;
        try
        {
            ActorContext staff;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, "Demo!" + Guid.NewGuid().ToString("N") + "a1");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agencyId} OR Id={foreignId}");
                var user = await db.Set<StaffUser>().SingleAsync(x => x.Email == "agency-admin@cover.example");
                staff = new(user.Id, user.TeamId, null, new HashSet<string> { "agency-admin" });
            }
            var factory = new PooledDbContextFactory<BackOfficeDbContext>(options); var clock = TimeProvider.System;
            var boundary = new SqlCommandBoundary(factory, clock); var drafts = new AgencyDraftService(factory, boundary, clock);
            var issuer = new InvitationService(new AgencyNotificationService(new AgencyNotificationPayload(new EphemeralDataProtectionProvider()), clock), clock);
            var users = new AgencyUserService(drafts, boundary, clock, issuer); var lifecycle = new AgencyUserLifecycle(drafts, boundary, issuer, clock);
            async Task<Guid> Fixture(string name, Guid agency)
            {
                // Stored service identity fixture only: this does not verify external login/acceptance.
                await using var db = new BackOfficeDbContext(options); await using var tx = await db.Database.BeginTransactionAsync();
                var user = new StaffUser { AgencyId = agency, Email = name + "@example.test", NormalizedEmail = (name + "@example.test").ToUpperInvariant(), DisplayName = name, State = "invited" };
                db.Add(user); await db.SaveChangesAsync();
                db.Add(new UserRole { UserId = user.Id, RoleId = await db.Set<Role>().Where(x => x.Code == "broker-admin").Select(x => x.Id).SingleAsync() }); await db.SaveChangesAsync();
                user.State = "active";
                db.Add(new UserCredential { UserId = user.Id, ProviderSubject = user.Email, PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<StaffUser>().HashPassword(user, "Fictional!Password123") });
                db.Add(new UserSession { UserId = user.Id, TokenHash = RandomNumberGenerator.GetBytes(32), SecurityStamp = user.SecurityStamp, ExpiresAt = clock.GetUtcNow().AddDays(1), LastSeenAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(); await tx.CommitAsync(); return user.Id;
            }
            async Task<byte[]> AgencyVersion() { await using var db = new BackOfficeDbContext(options); return (await db.Set<Agency>().SingleAsync(x => x.Id == agencyId)).RowVersion; }
            async Task<byte[]> UserVersion(Guid id) { await using var db = new BackOfficeDbContext(options); return (await db.Set<StaffUser>().SingleAsync(x => x.Id == id)).RowVersion; }
            var first = await Fixture("Fictional own admin", agencyId); var second = await Fixture("Fictional other admin", agencyId); var foreign = await Fixture("Fictional foreign admin", foreignId);
            var broker = new ActorContext(first, null, agencyId, new HashSet<string> { "broker-admin" });
            await using (var db = new BackOfficeDbContext(options))
                await Assert.ThrowsAsync<InvalidOperationException>(() => AgencyUserAuthority.Authorize(db, broker, agencyId, true, default));
            var input = AgencyUserRules.Validate("own-invited@example.test", "Fictional own invite", "broker-user");
            var version = await AgencyVersion(); var key = Guid.NewGuid().ToString("N");
            var result = await users.Invite(broker, agencyId, key, version, input);
            Assert.True((await users.Invite(broker, agencyId, key, version, input)).Replayed);
            Assert.Equal(403, (await Assert.ThrowsAsync<AgencyCommandException>(() => users.Stage(broker, agencyId, Guid.NewGuid().ToString(), version, input))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<AgencyCommandException>(() => users.Invite(broker, foreignId, key, version, input))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<AgencyCommandException>(() => lifecycle.Deactivate(broker, agencyId, foreign, Guid.NewGuid().ToString(), Array.Empty<byte>(), "Foreign target"))).Status);
            await lifecycle.Edit(broker, agencyId, result.ResourceId, Guid.NewGuid().ToString(), await UserVersion(result.ResourceId), "Fictional edited invite", "broker-readonly", "Own role change");
            await lifecycle.Deactivate(broker, agencyId, result.ResourceId, Guid.NewGuid().ToString(), await UserVersion(result.ResourceId), "Own deactivation");
            await lifecycle.Reactivate(broker, agencyId, result.ResourceId, Guid.NewGuid().ToString(), await UserVersion(result.ResourceId), "Own reinvitation");
            await lifecycle.Deactivate(broker, agencyId, second, Guid.NewGuid().ToString(), await UserVersion(second), "Second admin paused");
            var firstVersion = await UserVersion(first);
            Assert.Equal(409, (await Assert.ThrowsAsync<AgencyCommandException>(() => lifecycle.Deactivate(broker, agencyId, first, Guid.NewGuid().ToString(), firstVersion, "Last admin"))).Status);
            await lifecycle.Reactivate(broker, agencyId, second, Guid.NewGuid().ToString(), await UserVersion(second), "Second admin restored");
            await lifecycle.Edit(broker, agencyId, first, Guid.NewGuid().ToString(), await UserVersion(first), "Fictional demoted admin", "broker-user", "Self demotion with another admin");
            Assert.Equal(403, (await Assert.ThrowsAsync<AgencyCommandException>(() => users.Invite(broker, agencyId, key, version, input))).Status);
            var demoted = new ActorContext(first, null, agencyId, new HashSet<string> { "broker-user" });
            Assert.Equal(403, (await Assert.ThrowsAsync<AgencyCommandException>(() => users.Invite(demoted, agencyId, key, version, input))).Status);
            await using (var db = new BackOfficeDbContext(options)) Assert.All(await db.Set<UserSession>().Where(x => x.UserId == first || x.UserId == second).ToListAsync(), session => Assert.NotNull(session.RevokedAt));
            await lifecycle.Edit(staff, agencyId, first, Guid.NewGuid().ToString(), await UserVersion(first), "Fictional restored admin", "broker-admin", "Internal restoration");
            Assert.True((await users.Invite(broker, agencyId, key, version, input)).Replayed);
            await using (var db = new BackOfficeDbContext(options)) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agencyId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<AgencyCommandException>(() => users.Invite(broker, agencyId, key, version, input))).Status);
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.Equal(1, await db.Set<StaffUser>().CountAsync(x => x.Email == input.Email));
                Assert.Equal(1, await db.Set<IdempotencyRecord>().CountAsync(x => x.Key == key));
                Assert.Equal(2, await db.Set<AgencyInvitation>().CountAsync(x => x.UserId == result.ResourceId));
            }
        }
        finally { if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed."); await using var db = new BackOfficeDbContext(options); await db.Database.EnsureDeletedAsync(); }
    }
}
