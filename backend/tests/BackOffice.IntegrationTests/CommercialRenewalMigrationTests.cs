using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialRenewalDowngradePreservesRetainedExperienceAndRating()=>CommercialRenewalScenario(false,verifyDowngrade:true);

    private static async Task VerifyUnusedCommercialRenewalMigration(BackOfficeDbContext db)
    {
        await db.GetService<IMigrator>().MigrateAsync("20260920095821_CommercialServicingAtomicIssue");
        Assert.DoesNotContain("20260920133214_CommercialRenewalPreparation",await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static async Task VerifyCommercialRenewalLapseDowngrade(BackOfficeDbContext db)
    {
        var before=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleAsync();
        var error=await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20260920095821_CommercialServicingAtomicIssue"));
        // The later operational-payload migration now protects the issued base first.
        Assert.Equal(51971,error.Number);
        Assert.Contains("20260920182749_CommercialOperationalPayloads",await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(before.Id,(await db.Set<RenewalLapseEvent>().AsNoTracking().SingleAsync()).Id);
        Assert.Contains("20260920133214_CommercialRenewalPreparation",await db.Database.GetAppliedMigrationsAsync());
    }

    private static async Task VerifyCommercialRenewalDowngrade(BackOfficeDbContext db)
    {
        var before=await db.Set<RenewalExperienceVersion>().AsNoTracking().SingleAsync();
        var cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync();
        var error=await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20260920095821_CommercialServicingAtomicIssue"));
        // The later operational-payload migration now protects the issued base first.
        Assert.Equal(51971,error.Number);
        Assert.Contains("20260920182749_CommercialOperationalPayloads",await db.Database.GetAppliedMigrationsAsync());
        var after=await db.Set<RenewalExperienceVersion>().AsNoTracking().SingleAsync();
        Assert.Equal(before.CommercialRevisionId,after.CommercialRevisionId);
        Assert.Equal(before.CommercialSubjectsJson,after.CommercialSubjectsJson);
        Assert.Equal(cycle.InputJson,(await db.Set<ServicingCycle>().AsNoTracking().SingleAsync()).InputJson);
        Assert.Contains("20260920133214_CommercialRenewalPreparation",await db.Database.GetAppliedMigrationsAsync());
    }
}
