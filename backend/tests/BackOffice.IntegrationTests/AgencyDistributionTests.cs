using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyDistributionTests
{
    [Fact]public async Task RealSqlDistributionUsesCurrentExplicitVersionAndPreservesReviewerOnReseed()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            await using var db=new BackOfficeDbContext(options);await db.Database.MigrateAsync();
            await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
            var reviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-reviewer@cover.example");
            var hash=await db.Set<UserCredential>().Where(x=>x.UserId==reviewer.Id).Select(x=>x.PasswordHash).SingleAsync();
            Assert.Null(reviewer.AgencyId);Assert.NotEqual(reviewer.Id,(await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example")).Id);
            Assert.Equal("agency-admin",await(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==reviewer.Id select role.Code).SingleAsync());
            var now=new DateTimeOffset(2026,9,14,12,0,0,TimeSpan.Zero);
            var rule=await AgencyDistributionService.Configuration(db,now,default);Assert.NotNull(rule);Assert.Equal(3,rule.ProductVersionIds.Count);
            await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"b2");db.ChangeTracker.Clear();
            Assert.Equal(hash,await db.Set<UserCredential>().Where(x=>x.UserId==reviewer.Id).Select(x=>x.PasswordHash).SingleAsync());
            Assert.Equal(rule.RuleVersionId,(await AgencyDistributionService.Configuration(db,now,default))!.RuleVersionId);
            Assert.All(await db.Set<ProductVersion>().ToListAsync(),x=>{Assert.Equal("draft",x.State);Assert.Contains("\"ratingAvailable\":false",x.Definition);});
            var version=await db.Set<ProductVersion>().FirstAsync();var provider=await db.Set<CapacityProvider>().SingleAsync(x=>x.Id==version.ProviderId);
            var agency=new Agency{Reference="AG-DISTRIBUTION",LegalName="Fictional eligibility"};db.Add(agency);await db.SaveChangesAsync();
            async Task<bool?> Eligible()=>await AgencyDistributionService.DraftEligible(db,agency.Id,now,default);
            Assert.False(await Eligible());
            var grant=new AgencyDraftProduct{AgencyId=agency.Id,ProductVersionId=version.Id,EffectiveFrom=new(2026,9,14),BrokerCommissionBasisPoints=1250};db.Add(grant);await db.SaveChangesAsync();Assert.True(await Eligible());
            var onboarding=new AgencyOnboarding{AgencyId=agency.Id,Details="""
                {"legalName":"Fictional eligibility","commercialTerms":{"effectiveFrom":"2026-09-14","commissionBasis":"per-product","feeSharing":"none","volumeCommitmentMode":"none","minimumPremiumOverrideMode":"none","referralRouting":"standard-internal-underwriting"},"settlement":{"statementCycle":"monthly","method":"bank-transfer","premiumCollection":"agency","commissionSettlement":"net-remittance"},"paymentTermsDays":30,"creditLimit":"1000.00"}
                """};db.Add(onboarding);await db.SaveChangesAsync();
            async Task<string> InitialReadiness()
            {
                await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
                var assessment=await new AgencyEvidenceService(null!,null!,new FixedClock(now)).Validate(db,agency,default);
                Assert.False(assessment.Valid);return assessment.Items.Single(x=>x.Code=="initial-terms").State;
            }
            Assert.Equal("satisfied",await InitialReadiness());
            onboarding.Details=onboarding.Details.Replace("2026-09-14","2026-09-13");await db.SaveChangesAsync();
            Assert.Equal("failed",await InitialReadiness());Assert.Equal(new DateOnly(2026,9,14),grant.EffectiveFrom);
            provider.State="inactive";await db.SaveChangesAsync();Assert.False(await Eligible());provider.State="active";
            version.EffectiveTo=now;await db.SaveChangesAsync();Assert.False(await Eligible());version.EffectiveTo=null;
            grant.EffectiveFrom=new(2026,9,15);await db.SaveChangesAsync();Assert.False(await Eligible());grant.EffectiveFrom=new(2026,9,14);await db.SaveChangesAsync();
            db.Add(new SettingVersion{Scope="agency-distribution",Version=2,EffectiveFrom=now.AddDays(1),Values=JsonSerializer.Serialize(new{demo=true,kind="agency-distribution",productVersionIds=Array.Empty<Guid>()})});await db.SaveChangesAsync();Assert.True(await Eligible());
            now=now.AddDays(1);Assert.False(await Eligible());
            db.Add(new SettingVersion{Scope="agency-distribution",Version=3,EffectiveFrom=now,Values="{}"});await db.SaveChangesAsync();Assert.Null(await Eligible());
            // Current invalid rule does not revive the original immutable grant.
            Assert.Equal(3,await db.Set<SettingVersion>().CountAsync(x=>x.Scope=="agency-distribution"));
            Assert.Equal("draft",(await db.Set<Agency>().SingleAsync(x=>x.Id==agency.Id)).State);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    private sealed class FixedClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}

}
