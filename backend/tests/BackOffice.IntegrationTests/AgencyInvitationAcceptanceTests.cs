using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyInvitationAcceptanceTests
{
    [Fact]
    public async Task RealSqlAgencyInvitationAcceptanceIsOneTimeAndSerializesRevocation()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext actor;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);
            foreach(var scenario in new[]{"valid","concurrent","expired","future","revoked","suspended","inactive-user","resend-race","revoke-race"})
            {
                var clock=new Clock();var boundary=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,boundary,clock);
                var payload=new AgencyNotificationPayload(new EphemeralDataProtectionProvider());var issuer=new InvitationService(new AgencyNotificationService(payload,clock),clock);
                var users=new AgencyUserService(drafts,boundary,clock,issuer);var commands=new AgencyInvitationCommands(factory,drafts,boundary,issuer,clock);var acceptance=new InvitationAcceptance(factory,clock);
                Guid agencyId;byte[] agencyVersion;
                await using(var db=new BackOfficeDbContext(options)){var agency=new Agency{Reference="AG-ACCEPT-"+scenario,LegalName="Fictional acceptance test",State="active"};db.Add(agency);await db.SaveChangesAsync();agencyId=agency.Id;agencyVersion=agency.RowVersion;}
                var created=await users.Invite(actor,agencyId,Key(),agencyVersion,AgencyUserRules.Validate(scenario+"-accept@cover.example","Fictional acceptance user","broker-user"));
                var invitationId=JsonDocument.Parse(created.Body).RootElement.GetProperty("invitationId").GetGuid();string raw;byte[] invitationVersion;string originalStamp;
                await using(var db=new BackOfficeDbContext(options))
                {
                    var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId);invitationVersion=invitation.RowVersion;
                    var notification=await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==invitation.NotificationId);raw=payload.Unprotect(agencyId,notification.Id,notification.ProtectedPayload).Content;
                    originalStamp=(await db.Set<StaffUser>().SingleAsync(x=>x.Id==created.ResourceId)).SecurityStamp;
                }
                const string password="Fictional acceptance password!";
                if(scenario=="expired")clock.Move(TimeSpan.FromDays(14));
                if(scenario=="future")clock.Move(TimeSpan.FromMinutes(-1));
                if(scenario=="revoked")await commands.Revoke(actor,invitationId,Key(),invitationVersion,"Fictional revoked link");
                if(scenario=="suspended")
                {await using var db=new BackOfficeDbContext(options);var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==agencyId);agency.State="suspended";await db.SaveChangesAsync();}
                if(scenario=="inactive-user")
                {var lifecycle=new AgencyUserLifecycle(drafts,boundary,issuer,clock);byte[] version;await using(var db=new BackOfficeDbContext(options)){version=(await db.Set<StaffUser>().SingleAsync(x=>x.Id==created.ResourceId)).RowVersion;}await lifecycle.Deactivate(actor,agencyId,created.ResourceId,Key(),version,"Fictional inactive user");}
                bool accepted;
                if(scenario=="concurrent")
                {var results=await Task.WhenAll(acceptance.Accept(raw,password),acceptance.Accept(raw,"Different fictional password!"));Assert.Single(results,x=>x);accepted=true;}
                else if(scenario is "resend-race" or "revoke-race")
                {
                    async Task<int> Change(){try{return(scenario=="resend-race"?await commands.Resend(actor,invitationId,Key(),invitationVersion,"Fictional resend race"):await commands.Revoke(actor,invitationId,Key(),invitationVersion,"Fictional revoke race")).Status;}catch(AgencyCommandException ex){return ex.Status;}}
                    var consume=acceptance.Accept(raw,password);var change=Change();await Task.WhenAll(consume,change);accepted=await consume;
                    Assert.True(accepted?(await change is 409 or 412):(await change is 200 or 202));
                }
                else{accepted=await acceptance.Accept(raw,password);Assert.Equal(scenario=="valid",accepted);}
                Assert.False(await acceptance.Accept(raw,password));
                await using(var db=new BackOfficeDbContext(options))
                {
                    var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==created.ResourceId);var credentials=await db.Set<UserCredential>().Where(x=>x.UserId==user.Id).ToListAsync();
                    Assert.Equal(accepted?1:0,credentials.Count);
                    if(accepted)
                    {
                        Assert.Equal("active",user.State);Assert.NotEqual(originalStamp,user.SecurityStamp);Assert.Equal("accepted",(await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==invitationId)).State);
                        var hasher=new PasswordHasher<StaffUser>();Assert.True(hasher.VerifyHashedPassword(user,credentials[0].PasswordHash!,password)!=PasswordVerificationResult.Failed||scenario=="concurrent"&&hasher.VerifyHashedPassword(user,credentials[0].PasswordHash!,"Different fictional password!")!=PasswordVerificationResult.Failed);
                        Assert.False(await db.Set<UserSession>().AnyAsync(x=>x.UserId==user.Id));
                    }
                    Assert.DoesNotContain(await db.Set<IdempotencyRecord>().ToListAsync(),x=>x.Route.Contains("/auth/")||x.ResultBody.Contains(raw)||x.ResultBody.Contains(password));
                    Assert.DoesNotContain(await db.Set<AuditEvent>().Where(x=>x.SubjectRecordId==invitationId).ToListAsync(),x=>(x.After??"").Contains(raw)||(x.After??"").Contains(password));
                }
                Assert.False(await acceptance.Accept("malformed",password));Assert.False(await acceptance.Accept(InvitationToken.Create().Value,password));
                Assert.Equal(422,(await Assert.ThrowsAsync<AgencyCommandException>(()=>acceptance.Accept(raw,"short"))).Status);
            }
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static string Key()=>Guid.NewGuid().ToString("N");
    private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Move(TimeSpan value)=>now+=value;}
}
