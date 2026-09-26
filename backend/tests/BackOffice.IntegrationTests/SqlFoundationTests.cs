using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class SqlFoundationTests
{
    [Theory]
    [InlineData("Server=.;Database=master;Integrated Security=true")]
    [InlineData("Server=.;Database=OtherBusiness;Integrated Security=true")]
    [InlineData("Server=.;Database=CoverMGA_Demo_Copy;Integrated Security=true")]
    [InlineData("Server=.;Database=CoverMGA_Demo;AttachDbFilename=C:\\unrelated.mdf;Integrated Security=true")]
    public void DemoGuardRejectsUnownedTargetsBeforeConnecting(string connection) =>
        Assert.Throws<InvalidOperationException>(() => DemoDatabase.ValidateDemoTarget(connection));

    [Fact]
    public async Task RealSqlMigrationsSeedsConstraintsAndConcurrency()
    {
        // No fallback or skip: unavailable SQL is a failed integration run.
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedDatabase = "CoverMGA_Test_" + Guid.NewGuid().ToString("N");
        connection.InitialCatalog = ownedDatabase;
        connection.AttachDBFilename = "";
        var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString, sql => sql.UseCompatibilityLevel(160)).Options;
        var password = "Demo!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "a1";
        try
        {
            await using (var db = new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();
                await DemoDatabase.SeedAsync(db,password);
                await DemoDatabase.SeedAsync(db,password);
                await db.Database.MigrateAsync();
                Assert.False(db.Database.HasPendingModelChanges());
            }
            // Independent context reload, with no tracked seed entities.
            await using (var reloaded = new BackOfficeDbContext(options))
            {
                Assert.Equal(9,await reloaded.Set<StaffUser>().CountAsync());
                Assert.Equal(9,await reloaded.Set<UserRole>().CountAsync());
                Assert.Contains(await reloaded.Set<StaffUser>().ToArrayAsync(),x => x.Email == "admin-reviewer@cover.example");
                Assert.Contains(await reloaded.Set<StaffUser>().ToArrayAsync(),x => x.Email == "finance-reviewer@cover.example");
                Assert.Equal(3,await reloaded.Set<ProductVersion>().CountAsync());
                Assert.Equal(4,await reloaded.Set<SettingVersion>().CountAsync(x => x.Scope.StartsWith("diagnostic-probe/")));
                Assert.Single(await reloaded.Set<DemoClock>().ToListAsync());
                var credential = await reloaded.Set<UserCredential>().FirstAsync();
                var user = await reloaded.Set<StaffUser>().SingleAsync(x => x.Id == credential.UserId);
                Assert.NotEqual(password,credential.PasswordHash);
                Assert.Equal(PasswordVerificationResult.Success,new PasswordHasher<StaffUser>().VerifyHashedPassword(user,credential.PasswordHash!,password));
            }
            await RejectConstraintAsync(options,db => db.Add(new Team {Name="Demo operations"}),2601,2627);
            await RejectConstraintAsync(options,db => db.Add(new SettingVersion {Scope="invalid-json",Version=1,EffectiveFrom=DateTimeOffset.UtcNow,Values="not JSON"}),547);
            await RejectConstraintAsync(options,db => db.Add(new UserRole {UserId=Guid.NewGuid(),RoleId=Guid.NewGuid()}),547);
            await RejectConstraintAsync(options,db => db.Add(new StaffUser {Email="bad@cover.example",NormalizedEmail="BAD@COVER.EXAMPLE",State="unknown"}),547);
            await RejectConstraintAsync(options,db => db.Add(new Team {Name="Non-UTC",CreatedAt=DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(1))}),547);
            await using (var ranges = new BackOfficeDbContext(options))
            {
                var version = await ranges.Set<ProductVersion>().FirstAsync();
                version.State="published";
                version.EffectiveTo=version.EffectiveFrom.AddDays(30);
                await ranges.SaveChangesAsync();
                await RejectConstraintAsync(options,db => db.Add(new ProductVersion {ProductId=version.ProductId,ProviderId=version.ProviderId,
                    Version=2,State="published",EffectiveFrom=version.EffectiveFrom.AddDays(1),JsonSchemaVersion="1.0",QuestionSetVersion="demo-1"}),51001);
                ranges.Add(new ProductVersion {ProductId=version.ProductId,ProviderId=version.ProviderId,Version=2,State="published",
                    EffectiveFrom=version.EffectiveTo.Value,JsonSchemaVersion="1.0",QuestionSetVersion="demo-1"});
                await ranges.SaveChangesAsync(); // Adjacent half-open validity ranges are legal.
            }
            await using var first = new BackOfficeDbContext(options);
            await using var second = new BackOfficeDbContext(options);
            var original = await first.Set<Team>().SingleAsync();
            var stale = await second.Set<Team>().SingleAsync();
            original.Name="Updated operations";
            await first.SaveChangesAsync();
            stale.Name="Stale overwrite";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
        finally
        {
            // Drop only this invocation's generated database, never an environment-supplied name.
            if (connection.InitialCatalog != ownedDatabase || !ownedDatabase.StartsWith("CoverMGA_Test_",StringComparison.Ordinal))
                throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup = new BackOfficeDbContext(options);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task RejectConstraintAsync(DbContextOptions<BackOfficeDbContext> options,Action<BackOfficeDbContext> arrange,params int[] expectedNumbers)
    {
        await using var db = new BackOfficeDbContext(options);
        arrange(db);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sql = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains(sql.Number,expectedNumbers);
    }
}
