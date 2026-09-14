using System.Security.Cryptography;
using BackOffice.Application;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class SupportFlagSeedTests
{
    [Fact]
    public async Task RealSqlSupportDemoSeedsSharedAndInternalFlagsWithoutReplacingEditsOrHistory()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";Guid[] ids=[];
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password,includeSupportFlags:true);
                var flags=await db.Set<SupportFlag>().ToListAsync();Assert.Equal(2,flags.Count);ids=flags.Select(x=>x.Id).Order().ToArray();
                Assert.Equal(2,await db.Set<SupportFlagHistory>().CountAsync());Assert.Single(await db.Set<FlagVisibility>().ToListAsync());
                var actor=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync();
                var scope=new SupportFlagScope(new ActorContext(actor,null,null,new HashSet<string>{"servicing"}));
                Assert.Single(await scope.SafeInstructions(db,PartyDemoSeed.RelationshipId(3,2)).ToListAsync());Assert.Empty(await scope.SafeInstructions(db,PartyDemoSeed.RelationshipId(3,1)).ToListAsync());
                var flag=flags.Single(x=>x.AgencyInstruction!=null);
                await using(var tx=await db.Database.BeginTransactionAsync())
                {await SupportFlagService.EndAsync(db,new ActorContext(actor,null,null,new HashSet<string>{"servicing"}),flag.Id,flag.RowVersion,"Fictional seeded instruction resolved",DateTimeOffset.UtcNow);await tx.CommitAsync();}
                await DemoDatabase.SeedAsync(db,password,includeSupportFlags:true);
            }
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(ids,(await db.Set<SupportFlag>().Select(x=>x.Id).ToListAsync()).Order().ToArray());Assert.Equal(3,await db.Set<SupportFlagHistory>().CountAsync());
                var ended=Assert.Single(await db.Set<SupportFlag>().Where(x=>x.EndedAt!=null).ToListAsync());Assert.Equal("Fictional seeded instruction resolved",ended.Reason);
            }
        }
        finally {if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
}
