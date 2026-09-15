using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyPermissionStorageTests
{
    [Fact]
    public async Task RealSqlPermissionProvenanceRequiresIndependentDecisionAndCreatesGrantAtomically()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var owned = "CoverMGA_Test_" + Guid.NewGuid().ToString("N"); connection.InitialCatalog = owned; connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        try
        {
            var agency = PartyDemoSeed.FirstAgencyId; var other = PartyDemoSeed.SecondAgencyId;
            Guid requester, reviewer, servicing, requestId;
            var now = DateTimeOffset.UtcNow;
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync(); await DemoDatabase.SeedAsync(db, "Demo!" + Guid.NewGuid().ToString("N") + "a1");
                requester = await db.Set<StaffUser>().Where(x => x.Email == "agency-admin@cover.example").Select(x => x.Id).SingleAsync();
                reviewer = await db.Set<StaffUser>().Where(x => x.Email == "agency-reviewer@cover.example").Select(x => x.Id).SingleAsync();
                servicing = await db.Set<StaffUser>().Where(x => x.Email == "servicing@cover.example").Select(x => x.Id).SingleAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency} OR Id={other}");
                var request = new AgencyPermissionRequest { AgencyId = agency, RequestedBy = requester, CreatedBy = requester, Reason = "Fictional bordereau requirement", CreatedAt = now };
                db.Add(request); await db.SaveChangesAsync(); requestId = request.Id;
            }
            async Task Reject(FormattableString sql)
            {
                await using var db = new BackOfficeDbContext(options);
                var failure = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
                Assert.Contains(failure.Number, new[] { 51000, 547, 2601, 2627 });
            }
            await Reject($"UPDATE AgencyPermissionRequest SET Reason=N'rewritten' WHERE Id={requestId}");
            await Reject($"DELETE FROM AgencyPermissionRequest WHERE Id={requestId}");
            await Reject($"UPDATE AgencyPermissionRequest SET State=N'granted',DecisionBy={requester},DecisionReason=N'self',DecidedAt={now} WHERE Id={requestId}");
            await Reject($"UPDATE AgencyPermissionRequest SET State=N'granted',DecisionBy={servicing},DecisionReason=N'unauthorized',DecidedAt={now} WHERE Id={requestId}");
            await Reject($"INSERT AgencyPermissionRequest(Id,AgencyId,Permission,RequestedBy,Reason,State,CreatedBy,CreatedAt,UpdatedAt) VALUES(NEWID(),{agency},N'bordereau-download',{requester},N'duplicate',N'pending',{requester},{now},{now})");
            foreach (var permission in new[] { "system-admin", "BORDEREAU-DOWNLOAD", "bordereau-download " })
                await Reject($"INSERT AgencyPermissionRequest(Id,AgencyId,Permission,RequestedBy,Reason,State,CreatedBy,CreatedAt,UpdatedAt) VALUES(NEWID(),{other},{permission},{requester},N'bad permission',N'pending',{requester},{now},{now})");
            await Reject($"INSERT AgencyPermissionGrant(Id,AgencyId,RequestId,Permission,GrantedBy,GrantedAt,CreatedBy,CreatedAt,UpdatedAt) VALUES(NEWID(),{agency},{requestId},N'bordereau-download',{reviewer},{now},{reviewer},{now},{now})");
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agency}");
            }
            await Reject($"UPDATE AgencyPermissionRequest SET State=N'granted',DecisionBy={reviewer},DecisionReason=N'reviewed',DecidedAt={now} WHERE Id={requestId}");
            await using (var db = new BackOfficeDbContext(options))
            {
                Assert.Empty(await db.Set<AgencyPermissionGrant>().ToListAsync());
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency}");
                var request = await db.Set<AgencyPermissionRequest>().SingleAsync(x => x.Id == requestId);
                request.State = "granted"; request.DecisionBy = reviewer; request.DecisionReason = "Independently reviewed"; request.DecidedAt = now;
                await db.SaveChangesAsync();
            }
            Guid grantId;
            await using (var db = new BackOfficeDbContext(options))
            {
                var grant = Assert.Single(await db.Set<AgencyPermissionGrant>().ToListAsync()); grantId = grant.Id;
                Assert.Equal(requestId, grant.RequestId); Assert.Equal(agency, grant.AgencyId); Assert.Equal(reviewer, grant.GrantedBy); Assert.Equal(now, grant.GrantedAt);
                Assert.Null(grant.RevokedAt); Assert.Equal("granted", (await db.Set<AgencyPermissionRequest>().SingleAsync()).State);
            }
            await Reject($"UPDATE AgencyPermissionRequest SET State=N'rejected' WHERE Id={requestId}");
            await Reject($"UPDATE AgencyPermissionGrant SET AgencyId={other} WHERE Id={grantId}");
            await Reject($"DELETE FROM AgencyPermissionGrant WHERE Id={grantId}");
            await Reject($"UPDATE AgencyPermissionGrant SET RevokedBy={servicing},RevokedAt={now},RevocationReason=N'unauthorized' WHERE Id={grantId}");
            await Reject($"INSERT AgencyPermissionRequest(Id,AgencyId,Permission,RequestedBy,Reason,State,CreatedBy,CreatedAt,UpdatedAt) VALUES(NEWID(),{agency},N'bordereau-download',{requester},N'already granted',N'pending',{requester},{now},{now})");
            await using (var db = new BackOfficeDbContext(options))
            {
                // Revocation remains possible after suspension; it never deletes provenance.
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={agency}");
                var grant = await db.Set<AgencyPermissionGrant>().SingleAsync(); grant.RevokedBy = requester; grant.RevokedAt = now; grant.RevocationReason = "Fictional permission withdrawn"; await db.SaveChangesAsync();
            }
            await Reject($"UPDATE AgencyPermissionGrant SET RevokedBy=NULL,RevokedAt=NULL,RevocationReason=NULL WHERE Id={grantId}");
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agency}");
                var request = new AgencyPermissionRequest { AgencyId = agency, RequestedBy = requester, CreatedBy = requester, Reason = "Fresh request after revocation" };
                db.Add(request); await db.SaveChangesAsync(); request.State = "rejected"; request.DecisionBy = reviewer; request.DecisionReason = "Not required"; request.DecidedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
                Assert.Equal(2, await db.Set<AgencyPermissionRequest>().CountAsync()); Assert.Single(await db.Set<AgencyPermissionGrant>().ToListAsync());
                Assert.False(await db.Set<AgencyPermissionGrant>().AnyAsync(x => x.RevokedAt == null));
            }
        }
        finally
        {
            if (connection.InitialCatalog != owned) throw new InvalidOperationException("Cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options); await cleanup.Database.EnsureDeletedAsync();
        }
    }
}
