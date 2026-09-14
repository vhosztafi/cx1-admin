using System.Security.Cryptography;
using System.Text.Json;
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

public sealed class AgencyInvitationUserLifecycleTests
{
    [Fact]
    public async Task RealSqlAgencyInvitationUserLifecycleProtectsLastAdminAndRevokesAccess()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext actor;Guid agencyId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
                var agency=new Agency{Reference="AG-LIFECYCLE",LegalName="Fictional lifecycle agency",State="active"};db.Add(agency);await db.SaveChangesAsync();agencyId=agency.Id;
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=TimeProvider.System;var boundary=new SqlCommandBoundary(factory,clock);
            var drafts=new AgencyDraftService(factory,boundary,clock);var issuer=new InvitationService(new AgencyNotificationService(new AgencyNotificationPayload(new EphemeralDataProtectionProvider()),clock),clock);
            var users=new AgencyUserService(drafts,boundary,clock,issuer);var lifecycle=new AgencyUserLifecycle(drafts,boundary,issuer,clock);
            async Task<byte[]> Version(Guid id){await using var db=new BackOfficeDbContext(options);return(await db.Set<StaffUser>().SingleAsync(x=>x.Id==id)).RowVersion;}
            async Task<Guid> Invite(string email,string role)
            {await using var db=new BackOfficeDbContext(options);var parent=await db.Set<Agency>().SingleAsync(x=>x.Id==agencyId);return(await users.Invite(actor,agencyId,Key(),parent.RowVersion,AgencyUserRules.Validate(email,"Fictional user",role))).ResourceId;}
            async Task ActivateFixture(Guid id)
            {
                // Simulate a previously accepted user; public acceptance is a separate plan slice.
                await using var db=new BackOfficeDbContext(options);await using var tx=await db.Database.BeginTransactionAsync();
                var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.UserId==id);invitation.State="accepted";invitation.AcceptedAt=clock.GetUtcNow();
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==id);user.State="active";
                db.Add(new UserCredential{UserId=id,ProviderSubject=user.Email,PasswordHash=new Microsoft.AspNetCore.Identity.PasswordHasher<StaffUser>().HashPassword(user,"Fictional!Password123")});
                db.Add(new UserSession{UserId=id,TokenHash=RandomNumberGenerator.GetBytes(32),ExpiresAt=clock.GetUtcNow().AddDays(1),LastSeenAt=clock.GetUtcNow(),SecurityStamp=user.SecurityStamp});
                await db.SaveChangesAsync();await tx.CommitAsync();
            }
            var first=await Invite("lifecycle-admin-1@cover.example","broker-admin");await ActivateFixture(first);var onlyAdminVersion=await Version(first);
            Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>lifecycle.Deactivate(actor,agencyId,first,Key(),onlyAdminVersion,"Last admin denial"))).Status);
            Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>lifecycle.Edit(actor,agencyId,first,Key(),onlyAdminVersion,"Fictional demotion","broker-user","Last admin denial"))).Status);
            var second=await Invite("lifecycle-admin-2@cover.example","broker-admin");await ActivateFixture(second);
            var firstVersion=await Version(first);var secondVersion=await Version(second);
            async Task<int> Demote(Guid id,byte[] version)
            {try{return(await lifecycle.Edit(actor,agencyId,id,Key(),version,"Fictional updated","broker-readonly","Concurrent demotion")).Status;}catch(AgencyCommandException failure){return failure.Status;}}
            var raced=await Task.WhenAll(Demote(first,firstVersion),Demote(second,secondVersion));Assert.Single(raced,x=>x==200);Assert.Contains(409,raced);
            Guid remaining,demoted;string stamp;
            await using(var db=new BackOfficeDbContext(options))
            {
                remaining=await(from u in db.Set<StaffUser>() join grant in db.Set<UserRole>() on u.Id equals grant.UserId join role in db.Set<Role>() on grant.RoleId equals role.Id where u.AgencyId==agencyId&&role.Code=="broker-admin" select u.Id).SingleAsync();demoted=remaining==first?second:first;
                Assert.All(await db.Set<UserSession>().Where(x=>x.UserId==demoted).ToListAsync(),x=>Assert.NotNull(x.RevokedAt));stamp=(await db.Set<StaffUser>().SingleAsync(x=>x.Id==demoted)).SecurityStamp;
            }
            var beforeDeactivate=await Version(demoted);var deactivateKey=Key();await lifecycle.Deactivate(actor,agencyId,demoted,deactivateKey,beforeDeactivate,"Fictional staff departure");
            Assert.True((await lifecycle.Deactivate(actor,agencyId,demoted,deactivateKey,beforeDeactivate,"Fictional staff departure")).Replayed);
            Assert.Equal(412,(await Assert.ThrowsAsync<AgencyCommandException>(()=>lifecycle.Edit(actor,agencyId,demoted,Key(),beforeDeactivate,"Stale name","broker-user","Stale denial"))).Status);
            await lifecycle.Reactivate(actor,agencyId,demoted,Key(),await Version(demoted),"Fictional return");
            await using(var db=new BackOfficeDbContext(options))
            {
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==demoted);Assert.Equal("active",user.State);Assert.NotEqual(stamp,user.SecurityStamp);
                Assert.All(await db.Set<UserSession>().Where(x=>x.UserId==demoted).ToListAsync(),x=>Assert.NotNull(x.RevokedAt));Assert.Single(await db.Set<AgencyInvitation>().Where(x=>x.UserId==demoted).ToListAsync());
            }
            var pending=await Invite("lifecycle-pending@cover.example","broker-user");Guid oldId;byte[] oldHash;
            await using(var db=new BackOfficeDbContext(options)){var old=await db.Set<AgencyInvitation>().SingleAsync(x=>x.UserId==pending);oldId=old.Id;oldHash=old.TokenHash!;}
            await lifecycle.Deactivate(actor,agencyId,pending,Key(),await Version(pending),"Fictional pending withdrawal");
            var reactivateKey=Key();var pendingVersion=await Version(pending);await lifecycle.Reactivate(actor,agencyId,pending,reactivateKey,pendingVersion,"Fictional pending return");
            Assert.True((await lifecycle.Reactivate(actor,agencyId,pending,reactivateKey,pendingVersion,"Fictional pending return")).Replayed);
            await using(var db=new BackOfficeDbContext(options))
            {
                var history=await db.Set<AgencyInvitation>().Where(x=>x.UserId==pending).ToListAsync();Assert.Equal(2,history.Count);Assert.Equal("revoked",history.Single(x=>x.Id==oldId).State);
                Assert.False(CryptographicOperations.FixedTimeEquals(oldHash,history.Single(x=>x.State=="pending").TokenHash!));Assert.False(await db.Set<UserCredential>().AnyAsync(x=>x.UserId==pending));
                Assert.Equal("invited",(await db.Set<StaffUser>().SingleAsync(x=>x.Id==pending)).State);
            }
            await lifecycle.Deactivate(actor,agencyId,pending,Key(),await Version(pending),"Fictional rollback setup");
            var rollbackVersion=await Version(pending);
            await using(var db=new BackOfficeDbContext(options))
            {db.Add(new SettingVersion{Scope="agency-invitation-delivery",Version=2,EffectiveFrom=clock.GetUtcNow(),Values="{\"demo\":false,\"scenario\":\"pass\"}"});await db.SaveChangesAsync();}
            await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>lifecycle.Reactivate(actor,agencyId,pending,Key(),rollbackVersion,"Fictional failed reactivation"));
            await using(var db=new BackOfficeDbContext(options))
            {
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==pending);Assert.Equal("suspended",user.State);Assert.Equal(rollbackVersion,user.RowVersion);
                Assert.Equal(2,await db.Set<AgencyInvitation>().CountAsync(x=>x.UserId==pending));Assert.False(await db.Set<AgencyInvitation>().AnyAsync(x=>x.UserId==pending&&x.State=="pending"));
            }
            using var details=JsonDocument.Parse("{\"legalName\":\"Fictional draft lifecycle\"}");
            var draft=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(details.RootElement),2,null,default);
            var staged=await users.Invite(actor,draft.ResourceId,Key(),Convert.FromBase64String(draft.Etag!.Trim('"')),AgencyUserRules.Validate("lifecycle-draft@cover.example","Fictional draft user","broker-user"));
            await lifecycle.Deactivate(actor,draft.ResourceId,staged.ResourceId,Key(),await Version(staged.ResourceId),"Fictional draft removal");
            await lifecycle.Reactivate(actor,draft.ResourceId,staged.ResourceId,Key(),await Version(staged.ResourceId),"Fictional draft restoration");
            await using(var db=new BackOfficeDbContext(options))
            {
                var stagedHistory=await db.Set<AgencyInvitation>().Where(x=>x.UserId==staged.ResourceId).ToListAsync();Assert.Equal(2,stagedHistory.Count);Assert.Single(stagedHistory,x=>x.State=="staged");
                Assert.All(stagedHistory,x=>Assert.Null(x.TokenHash));Assert.False(await db.Set<AgencyNotification>().AnyAsync(x=>x.AgencyId==draft.ResourceId));
            }
            Assert.Equal(404,(await Assert.ThrowsAsync<AgencyCommandException>(()=>lifecycle.Deactivate(actor,agencyId,staged.ResourceId,Key(),Array.Empty<byte>(),"Other agency denial"))).Status);
            Assert.Equal(404,(await Assert.ThrowsAsync<AgencyCommandException>(()=>lifecycle.Deactivate(actor,agencyId,actor.UserId,Key(),Array.Empty<byte>(),"Internal identity denial"))).Status);
            await using(var db=new BackOfficeDbContext(options))
            {var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==actor.UserId&&x.RoleId==role.Id));await db.SaveChangesAsync();}
            Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>lifecycle.Reactivate(actor,agencyId,pending,reactivateKey,pendingVersion,"Fictional pending return"))).Status);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static string Key()=>Guid.NewGuid().ToString("N");
}

