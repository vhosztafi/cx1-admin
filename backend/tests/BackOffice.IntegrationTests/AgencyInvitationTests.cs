using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyInvitationTests
{
    [Fact]
    public async Task RealSqlAgencyInvitationStagesOwnedIdentityWithoutTokensAndRevokesOnAbandon()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            ActorContext actor;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());await DemoDatabase.SeedAsync(db,password);
                var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
                Assert.Equal(3,await db.Set<Role>().CountAsync(x=>x.Scope=="agency"));
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var boundary=new SqlCommandBoundary(factory,TimeProvider.System);
            var drafts=new AgencyDraftService(factory,boundary,TimeProvider.System);var service=new AgencyUserService(drafts,boundary,TimeProvider.System);
            using var details=JsonDocument.Parse("{\"legalName\":\"Fictional staged broker agency\"}");
            var agency=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(details.RootElement),2,null,default);
            var other=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(details.RootElement),2,null,default);
            var input=AgencyUserRules.Validate("Fictional.Broker@cover.example","Fictional Broker","broker-admin");var key=Key();
            var staged=await service.Stage(actor,agency.ResourceId,key,Version(agency),input);
            var replay=await service.Stage(actor,agency.ResourceId,key,Version(agency),input);Assert.True(replay.Replayed);Assert.Equal(staged,replay with{Replayed=false});
            Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Stage(actor,other.ResourceId,Key(),Version(other),input))).Status);
            Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Stage(actor,other.ResourceId,Key(),Version(other),AgencyUserRules.Validate("agency-admin@cover.example","Wrong staff reuse","broker-user")))).Status);
            Guid invitationId;
            await using(var db=new BackOfficeDbContext(options))
            {
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==staged.ResourceId);Assert.Equal(agency.ResourceId,user.AgencyId);Assert.Null(user.TeamId);Assert.Equal("invited",user.State);
                var invitation=await db.Set<AgencyInvitation>().SingleAsync();invitationId=invitation.Id;Assert.Equal("staged",invitation.State);Assert.Null(invitation.TokenHash);Assert.Null(invitation.IssuedAt);Assert.Null(invitation.ExpiresAt);Assert.Null(invitation.NotificationId);
                Assert.False(await db.Set<OutboxWork>().AnyAsync(x=>x.Kind==AgencyNotificationService.Kind));Assert.False(await db.Set<UserCredential>().AnyAsync(x=>x.UserId==user.Id));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [UserRole] WHERE UserId={user.Id}"));
                var internalRole=await db.Set<Role>().SingleAsync(x=>x.Code=="system-admin");var otherBroker=await db.Set<Role>().SingleAsync(x=>x.Code=="broker-user");
                db.Add(new UserRole{UserId=user.Id,RoleId=internalRole.Id});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                db.Add(new UserRole{UserId=user.Id,RoleId=otherBroker.Id});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                db.Add(new UserRole{UserId=actor.UserId,RoleId=otherBroker.Id});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                db.Add(new AgencyInvitation{AgencyId=other.ResourceId,UserId=user.Id});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET AgencyId={other.ResourceId} WHERE Id={user.Id}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Role] SET Scope='internal' WHERE Id={otherBroker.Id}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AgencyInvitation] WHERE Id={invitationId}"));
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [AgencyInvitation] SET State='pending' WHERE Id={invitationId}"));
                var separate=new StaffUser{AgencyId=other.ResourceId,Email="cross-scope@cover.example",NormalizedEmail="CROSS-SCOPE@COVER.EXAMPLE",DisplayName="Fictional cross-scope proof",State="invited"};db.Add(separate);await db.SaveChangesAsync();
                db.Add(new UserRole{UserId=separate.Id,RoleId=otherBroker.Id});await db.SaveChangesAsync();
                db.Add(new AgencyInvitation{AgencyId=agency.ResourceId,UserId=separate.Id});var ownership=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Contains("FK_AgencyInvitation_User_Agency",ownership.InnerException!.Message);db.ChangeTracker.Clear();
                await DemoDatabase.SeedAsync(db,password);Assert.Single(await db.Set<AgencyInvitation>().ToListAsync());Assert.Equal(3,await db.Set<Role>().CountAsync(x=>x.Scope=="agency"));
            }
            var third=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(details.RootElement),2,null,default);
            async Task<int> Race(CommandOutcome target)
            {try{await service.Stage(actor,target.ResourceId,Key(),Version(target),AgencyUserRules.Validate("race@cover.example","Fictional race","broker-user"));return 201;}catch(AgencyCommandException failure){return failure.Status;}}
            var raced=await Task.WhenAll(Race(other),Race(third));Assert.Equal(new[]{201,409},raced.Order().ToArray());
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(1,await db.Set<StaffUser>().CountAsync(x=>x.NormalizedEmail=="RACE@COVER.EXAMPLE"));
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==staged.ResourceId);user.State="active";await db.SaveChangesAsync();
                var link=await db.Set<UserRole>().SingleAsync(x=>x.UserId==user.Id);
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [UserRole] WHERE Id={link.Id}"));
                db.Add(new UserCredential{UserId=user.Id,ProviderSubject=user.NormalizedEmail,PasswordHash=new PasswordHasher<StaffUser>().HashPassword(user,password)});await db.SaveChangesAsync();
            }
            Assert.Null(await new LocalIdentityService(factory,TimeProvider.System).AuthenticateAsync(input.Email,password,default));
            await drafts.Abandon(actor,agency.ResourceId,Key(),Version(staged),"Fictional abandoned application",default);
            await using(var db=new BackOfficeDbContext(options))
            {
                var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId);Assert.Equal("revoked",invitation.State);Assert.NotNull(invitation.RevokedAt);Assert.Null(invitation.TokenHash);
                Assert.Equal("suspended",(await db.Set<StaffUser>().SingleAsync(x=>x.Id==staged.ResourceId)).State);
                await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [AgencyInvitation] SET State='staged',RevokedAt=NULL WHERE Id={invitationId}"));
            }
            Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>service.Stage(actor with{Roles=new HashSet<string>{"underwriter"}},other.ResourceId,Key(),Version(other),AgencyUserRules.Validate("denied@cover.example","Denied","broker-user")))).Status);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static string Key()=>Guid.NewGuid().ToString("N");
    private static byte[] Version(CommandOutcome result)=>Convert.FromBase64String(result.Etag!.Trim('"'));
}
