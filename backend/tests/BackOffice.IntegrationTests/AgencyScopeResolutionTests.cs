using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed class AgencyScopeResolutionTests
{
    [Fact]
    public async Task RealSqlAgencyScopeRechecksStoredIdentityAndHoldsSuspensionFence()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            Guid agencyId,userId,readerRoleId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Guid.NewGuid().ToString("N")+"a1");
                var agency=new Agency{Reference="AG-SCOPE-RESOLVE",LegalName="Fictional scoped agency",State="active"};db.Add(agency);await db.SaveChangesAsync();agencyId=agency.Id;
                var user=new StaffUser{State="invited",AgencyId=agency.Id,Email="scope-broker@example.test",NormalizedEmail="SCOPE-BROKER@EXAMPLE.TEST",DisplayName="Fictional scoped broker"};db.Add(user);await db.SaveChangesAsync();userId=user.Id;
                db.Add(new UserRole{UserId=user.Id,RoleId=await db.Set<Role>().Where(x=>x.Code=="broker-admin").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();user.State="active";await db.SaveChangesAsync();readerRoleId=await db.Set<Role>().Where(x=>x.Code=="broker-readonly").Select(x=>x.Id).SingleAsync();
            }
            var actor=new ActorContext(userId,null,agencyId,new HashSet<string>{"broker-admin"});
            async Task Denied(ActorContext input,Guid? target=null,string capability="agency-sharing-read")
            {
                await using var db=new BackOfficeDbContext(options);await using var transaction=await db.Database.BeginTransactionAsync();
                Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>AgencyScope.Resolve(db,input,target??agencyId,capability))).Status);
            }
            await using(var db=new BackOfficeDbContext(options))await Assert.ThrowsAsync<InvalidOperationException>(()=>AgencyScope.Resolve(db,actor,agencyId,"agency-sharing-read"));
            await Denied(actor,Guid.NewGuid());await Denied(actor with{AgencyId=Guid.NewGuid()});await Denied(actor with{AgencyId=null});await Denied(actor with{UserId=Guid.NewGuid()});
            await Denied(actor with{Roles=new HashSet<string>{"broker-admin","system-admin"}});await Denied(actor,capability:"platform-admin");await Denied(actor,capability:"bordereau-download");
            await using(var held=new BackOfficeDbContext(options))
            {
                await using var transaction=await held.Database.BeginTransactionAsync();var resolved=await AgencyScope.Resolve(held,actor,agencyId,"agency-user-manage");Assert.Equal(agencyId,resolved.AgencyId);Assert.Equal("broker-admin",resolved.Role);
                await using var writer=new BackOfficeDbContext(options);
                var locked=await Assert.ThrowsAsync<SqlException>(()=>writer.Database.ExecuteSqlInterpolatedAsync($"SET LOCK_TIMEOUT 150; UPDATE Agency SET State=N'suspended' WHERE Id={agencyId}"));Assert.Equal(1222,locked.Number);
                await transaction.CommitAsync();await writer.Database.ExecuteSqlInterpolatedAsync($"SET LOCK_TIMEOUT -1; UPDATE Agency SET State=N'suspended' WHERE Id={agencyId}");
            }
            await Denied(actor);
            await using(var db=new BackOfficeDbContext(options)){await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={agencyId}");await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={userId}");}
            await Denied(actor);
            await using(var db=new BackOfficeDbContext(options)){await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'active' WHERE Id={userId}");var link=await db.Set<UserRole>().SingleAsync(x=>x.UserId==userId);link.RoleId=readerRoleId;await db.SaveChangesAsync();}
            await Denied(actor); // Old role claims cannot retain administrator authority.
            var reader=actor with{Roles=new HashSet<string>{"broker-readonly"}};await Denied(reader,capability:"agency-user-manage");
            await using(var db=new BackOfficeDbContext(options)){await using var transaction=await db.Database.BeginTransactionAsync();Assert.Equal("broker-readonly",(await AgencyScope.Resolve(db,reader,agencyId,"agency-sharing-read")).Role);}
            await using(var db=new BackOfficeDbContext(options)){db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==userId).ToListAsync());await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={userId}");db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==userId).ToListAsync());await db.SaveChangesAsync();}
            await Denied(reader);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
}
