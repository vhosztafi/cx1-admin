using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class QuoteScopeTests
{
    [Fact]
    public async Task RealSqlQuoteAuthorityFencesRevocationAndRechecksBeforeReceiptReplay()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            Guid agencyId, relationshipId, userId, roleId;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, "Demo!" + Guid.NewGuid().ToString("N") + "a1");
                var agency = new Agency { Reference = "AG-QUOTE-SCOPE", LegalName = "Fictional quote scope", State = "active" };
                var client = new ClientAccount { Reference = "CL-QUOTE-SCOPE", LegalName = "Fictional scope client", NormalizedName = "FICTIONAL SCOPE CLIENT" };
                db.AddRange(agency, client); await db.SaveChangesAsync();
                var relationship = new ClientAgencyRelationship { AgencyId = agency.Id, ClientId = client.Id };
                db.Add(relationship); await db.SaveChangesAsync(); agencyId = agency.Id; relationshipId = relationship.Id;
                userId = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
                roleId = await db.Set<Role>().Where(x => x.Code == "underwriter").Select(x => x.Id).SingleAsync();
            }
            var actor = new ActorContext(userId, null, null, new HashSet<string> { "underwriter" });
            async Task Denied(ActorContext input, int status = 403, QuoteAccess access = QuoteAccess.Capture)
            {
                await using var db = new BackOfficeDbContext(options); await using var transaction = await db.Database.BeginTransactionAsync();
                Assert.Equal(status, (await Assert.ThrowsAsync<QuoteOperationException>(() => QuoteScope.ForRelationshipAsync(db, input, relationshipId, access))).Status);
            }
            await using (var db = new BackOfficeDbContext(options))
                await Assert.ThrowsAsync<InvalidOperationException>(() => QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Read));
            await Denied(actor with { AgencyId = agencyId });
            await Denied(actor with { UserId = Guid.NewGuid() });
            await Denied(actor with { Roles = new HashSet<string> { "underwriter", "system-admin" } });

            await using (var held = new BackOfficeDbContext(options))
            {
                await using var transaction = await held.Database.BeginTransactionAsync();
                var scope = await QuoteScope.ForRelationshipAsync(held, actor, relationshipId, QuoteAccess.Capture);
                Assert.Equal(agencyId, scope.Agency.Id); Assert.Equal(userId, scope.Actor.UserId);
                await using var writer = new BackOfficeDbContext(options);
                async Task Locked(FormattableString sql) => Assert.Equal(1222, (await Assert.ThrowsAsync<SqlException>(() => writer.Database.ExecuteSqlInterpolatedAsync(sql))).Number);
                await Locked($"SET LOCK_TIMEOUT 150; UPDATE Agency SET State=N'suspended' WHERE Id={agencyId}");
                await Locked($"SET LOCK_TIMEOUT 150; UPDATE [User] SET State=N'suspended' WHERE Id={userId}");
                await Locked($"SET LOCK_TIMEOUT 150; DELETE FROM UserRole WHERE UserId={userId} AND RoleId={roleId}");
                await Locked($"SET LOCK_TIMEOUT 150; UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={relationshipId}");
            }

            var boundary = new SqlCommandBoundary(new Factory(options), TimeProvider.System);
            var identity = new CommandIdentity(userId, "/api/v1/quotes/scope-test", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var effects = 0;
            Task<CommandOutcome> Execute() => boundary.ExecuteAuthorizedAsync(identity, new { relationshipId }, "quote.scope-test",
                async (db, token) => { await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Capture, token); },
                (_, _) => { effects++; return Task.FromResult(new CommandOutcome(relationshipId, 200, "{}")); });
            Assert.False((await Execute()).Replayed); Assert.True((await Execute()).Replayed); Assert.Equal(1, effects);
            await using (var db = new BackOfficeDbContext(options))
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agencyId}");
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(Execute)).Status); Assert.Equal(1, effects);
            await using (var db = new BackOfficeDbContext(options))
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                Assert.Equal(agencyId, (await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Read)).Agency.Id);
            }
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agencyId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM UserRole WHERE UserId={userId} AND RoleId={roleId}");
            }
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(Execute)).Status); Assert.Equal(1, effects);
            await Denied(actor, access: QuoteAccess.Read);
            await using (var db = new BackOfficeDbContext(options))
            {
                db.Add(new UserRole { UserId = userId, RoleId = roleId }); await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={userId}");
            }
            await Denied(actor);
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={userId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={relationshipId}");
            }
            await Denied(actor, 409);
            await using (var db = new BackOfficeDbContext(options))
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                Assert.Equal("inactive", (await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Read)).Relationship.State);
                Assert.Equal(userId, (await QuoteScope.AuthorizeAgencyAsync(db, actor, agencyId, QuoteAccess.Read)).UserId);
            }
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
