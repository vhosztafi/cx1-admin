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

public sealed class AgencyInvitationCommandTests
{
    [Fact]
    public async Task RealSqlAgencyInvitationCommandsPreserveSingleLiveTokenAndCurrentAuthority()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext actor;Guid activeId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
                var active=new Agency{Reference="AG-INV-COMMAND",LegalName="Fictional active invitation agency",State="active"};db.Add(active);
                db.Add(new SettingVersion{Scope="agency-notification",Version=100,EffectiveFrom=DateTimeOffset.UtcNow,Values="{\"demo\":true,\"scenario\":\"unavailable\"}"});await db.SaveChangesAsync();activeId=active.Id;
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=new Clock();var boundary=new SqlCommandBoundary(factory,clock);
            var drafts=new AgencyDraftService(factory,boundary,clock);var payload=new AgencyNotificationPayload(new EphemeralDataProtectionProvider());
            var notices=new AgencyNotificationService(payload,clock);var issuer=new InvitationService(notices,clock);var users=new AgencyUserService(drafts,boundary,clock,issuer);
            var commands=new AgencyInvitationCommands(factory,drafts,boundary,issuer,clock);
            using var details=JsonDocument.Parse("{\"legalName\":\"Fictional draft invitation\"}");var draft=await drafts.Save(actor,null,Key(),null,AgencyDraftRules.Validate(details.RootElement),2,null,default);
            var draftUser=await users.Invite(actor,draft.ResourceId,Key(),Version(draft),AgencyUserRules.Validate("draft-mode@cover.example","Fictional staged","broker-user"));
            Guid stagedId;byte[] stagedVersion;
            await using(var db=new BackOfficeDbContext(options)){var staged=await db.Set<AgencyInvitation>().SingleAsync(x=>x.UserId==draftUser.ResourceId);stagedId=staged.Id;stagedVersion=staged.RowVersion;Assert.Null(staged.TokenHash);Assert.False(await db.Set<AgencyNotification>().AnyAsync(x=>x.AgencyId==draft.ResourceId));}
            var revokeKey=Key();Assert.Equal(200,(await commands.Revoke(actor,stagedId,revokeKey,stagedVersion,"Fictional staged removal")).Status);
            Assert.True((await commands.Revoke(actor,stagedId,revokeKey,stagedVersion,"Fictional staged removal")).Replayed);
            await using(var db=new BackOfficeDbContext(options)){var removed=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==stagedId);Assert.Equal("revoked",removed.State);Assert.Null(removed.TokenHash);Assert.False(await db.Set<AgencyNotification>().AnyAsync(x=>x.AgencyId==draft.ResourceId));}
            byte[] agencyVersion;await using(var db=new BackOfficeDbContext(options)){agencyVersion=(await db.Set<Agency>().SingleAsync(x=>x.Id==activeId)).RowVersion;}
            var createKey=Key();var input=AgencyUserRules.Validate("active-mode@cover.example","Fictional active invite","broker-admin");
            var created=await users.Invite(actor,activeId,createKey,agencyVersion,input);Assert.True((await users.Invite(actor,activeId,createKey,agencyVersion,input)).Replayed);
            var firstId=JsonDocument.Parse(created.Body).RootElement.GetProperty("invitationId").GetGuid();byte[] firstVersion,firstHash;Guid firstJob;
            await using(var db=new BackOfficeDbContext(options))
            {
                var first=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==firstId);firstVersion=first.RowVersion;firstHash=first.TokenHash!;Assert.Equal("pending",first.State);
                firstJob=(await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==first.NotificationId)).WorkId;
                var job=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==firstJob);Assert.Equal("agency-invitation-delivery",(await db.Set<SettingVersion>().SingleAsync(x=>x.Id==job.ScenarioVersionId)).Scope);
                db.Add(new SettingVersion{Scope="agency-invitation-delivery",Version=2,EffectiveFrom=clock.GetUtcNow(),Values="{\"demo\":false,\"scenario\":\"pass\"}"});await db.SaveChangesAsync();
            }
            await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>commands.Resend(actor,firstId,Key(),firstVersion,"Fictional invalid-config rollback"));
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("pending",(await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==firstId)).State);Assert.Single(await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==activeId).ToListAsync());
                db.Add(new SettingVersion{Scope="agency-invitation-delivery",Version=3,EffectiveFrom=clock.GetUtcNow(),Values="{\"demo\":true,\"scenario\":\"pass\"}"});await db.SaveChangesAsync();
            }
            clock.Advance();var resendKey=Key();var resent=await commands.Resend(actor,firstId,resendKey,firstVersion,"Fictional resend requested");Assert.Equal(202,resent.Status);Assert.NotEqual(firstId,resent.ResourceId);
            Assert.True((await commands.Resend(actor,firstId,resendKey,firstVersion,"Fictional resend requested")).Replayed);
            byte[] currentVersion;Guid secondJob;
            await using(var db=new BackOfficeDbContext(options))
            {
                var first=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==firstId);Assert.Equal("revoked",first.State);Assert.Equal(firstHash,first.TokenHash);
                var second=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==resent.ResourceId);Assert.False(CryptographicOperations.FixedTimeEquals(firstHash,second.TokenHash!));Assert.Equal(clock.GetUtcNow().AddDays(14),second.ExpiresAt);currentVersion=second.RowVersion;
                secondJob=(await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==second.NotificationId)).WorkId;
                Assert.Equal(2,await db.Set<AgencyInvitation>().CountAsync(x=>x.AgencyId==activeId));
                Assert.Equal(409,(await Assert.ThrowsAsync<AgencyCommandException>(()=>commands.Resend(actor,firstId,Key(),first.RowVersion,"Old invitation is replaced"))).Status);
            }
            var leases=new SqlJobLeases(factory,clock);var worker=new AgencyNotificationWorker(factory,payload,clock);
            var currentLease=(await leases.ClaimWorkAsync(AgencyNotificationService.Kind,secondJob))!;
            var receipt=(await worker.Deliver(currentLease))!.Value;Assert.True(await worker.Apply(currentLease,receipt));
            await using(var db=new BackOfficeDbContext(options)){Assert.Equal("succeeded",(await db.Set<OutboxWork>().SingleAsync(x=>x.Id==secondJob)).State);}
            async Task<int> Race(bool resend)
            {try{var result=resend?await commands.Resend(actor,resent.ResourceId,Key(),currentVersion,"Fictional concurrent resend"):await commands.Revoke(actor,resent.ResourceId,Key(),currentVersion,"Fictional concurrent revoke");return result.Status;}catch(AgencyCommandException failure){return failure.Status;}}
            var raced=await Task.WhenAll(Race(true),Race(false));Assert.Single(raced,x=>x is 200 or 202);Assert.Contains(412,raced);
            var lease=(await leases.ClaimWorkAsync(AgencyNotificationService.Kind,firstJob))!;
            var superseded=await Assert.ThrowsAsync<AgencyNotificationProviderException>(()=>worker.Deliver(lease));Assert.Equal(JobFailure.Superseded,superseded.Failure);await leases.FailAsync(lease,superseded.Failure);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.True(await db.Set<AgencyInvitation>().CountAsync(x=>x.UserId==created.ResourceId&&x.State=="pending")<=1);
                Assert.False(await db.Set<UserCredential>().AnyAsync(x=>x.UserId==created.ResourceId));
                var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==actor.UserId&&x.RoleId==role.Id));await db.SaveChangesAsync();
            }
            Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>commands.Resend(actor,firstId,resendKey,firstVersion,"Fictional resend requested"))).Status);
            Assert.Equal(403,(await Assert.ThrowsAsync<AgencyCommandException>(()=>users.Invite(actor,activeId,createKey,agencyVersion,input))).Status);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static string Key()=>Guid.NewGuid().ToString("N");
    private static byte[] Version(CommandOutcome outcome)=>Convert.FromBase64String(outcome.Etag!.Trim('"'));
    private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance()=>now=now.AddMinutes(10);}
}
