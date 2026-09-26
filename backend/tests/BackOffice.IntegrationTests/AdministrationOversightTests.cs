using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public void AdministrationAuditDisclosureRemovesSecretsAndHandlesDuplicateKeys()
    {
        var value=JsonSerializer.Serialize(AuditDisclosure.Read("""{"id":"safe-id","id":"safe-id","password":"never","token":"never","resetToken":"never","recoveryCodes":["never"],"code":"never","secret":"never","values":{"state":"active","mfaSecretCiphertext":"never","sessionToken":"never"}}"""));
        Assert.Contains("safe-id",value);Assert.Contains("active",value);Assert.DoesNotContain("never",value);
        Assert.Null(AuditDisclosure.Read("invalid-json"));
    }
    [Fact]
    public async Task RealSqlAdministrationOversightPinsScenariosAndProtectsReplay()
    {
        await WithDatabase(async(db,password)=>
        {
            await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true);
            var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
            var actor=new ActorContext(admin.Id,admin.TeamId,null,new HashSet<string>{"system-admin"});
            var factory=new AdministrationFactory(db.Database.GetConnectionString()!);var clock=new AdministrationClock(Now);
            var service=new IntegrationAdministration(factory,new SqlCommandBoundary(factory,clock),clock,new AdminEnvironment());
            var source=await db.Set<SettingVersion>().OrderByDescending(x=>x.Version).FirstAsync(x=>x.Scope=="quote-delivery");var original=source.Values;
            var job=new OutboxWork{Kind="quote-delivery",OperationKey=Guid.NewGuid().ToString("N"),ScenarioVersionId=source.Id,Payload="{\"token\":\"never-disclose\"}",CreatedAt=Now,NextAttemptAt=Now};db.Add(job);await db.SaveChangesAsync();
            var key=Guid.NewGuid().ToString("N");var edit=new IntegrationScenarioEdit(source.Scope,"timeout-after-success","Test future scenario");var etag=$"\"{source.Id:N}-{source.Version}\"";
            var saved=await service.ScenarioAsync(actor,edit,etag,key);Assert.Equal(saved.ResourceId,(await service.ScenarioAsync(actor,edit,etag,key)).ResourceId);
            Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ScenarioAsync(actor,edit,etag,Guid.NewGuid().ToString("N")))).Status);
            Assert.Equal(source.Id,(await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==job.Id)).ScenarioVersionId);
            Assert.Equal(original,(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==source.Id)).Values);
            var health=JsonSerializer.Serialize(await service.HealthAsync(actor));Assert.Contains("timeout-after-success",health);
            var detail=JsonSerializer.Serialize(await service.DetailAsync(actor,job.Id));Assert.DoesNotContain("never-disclose",detail);Assert.Contains(source.Id.ToString(),detail);
            var list=JsonSerializer.Serialize(await service.JobsAsync(actor,"quote-delivery",null,0,Now));Assert.Contains(job.Id.ToString(),list);Assert.DoesNotContain("never-disclose",list);
            var roles=await db.Set<UserRole>().Where(x=>x.UserId==admin.Id).ToArrayAsync();db.RemoveRange(roles);await db.SaveChangesAsync();
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ScenarioAsync(actor,edit,etag,key))).Status);
            await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DetailAsync(actor,job.Id));
        });
    }
    [Fact]
    public async Task RealSqlAccountPreferencesPersistUseFreshVersionAndRevokeAllOwnSessions()
    {
        await WithDatabase(async(db,password)=>
        {
            var factory=new AdministrationFactory(db.Database.GetConnectionString()!);var clock=new AdministrationClock(Now);
            var security=new AccountSecurityService(factory,new IdentitySecrets(new EphemeralDataProtectionProvider(),clock),clock);
            var identity=await new LocalIdentityService(factory,clock).AuthenticateAsync("servicing@cover.example",password,default);var actor=AccountSecurityService.Identity(identity!.Principal);
            var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==actor.UserId);
            var profile=new AccountProfile(user.DisplayName,AdminAccess.Etag(user.RowVersion),new("Fictional staff member","020 7946 0000","Service specialist",true,true));
            await security.ProfileAsync(actor,profile);
            var json=JsonSerializer.Serialize(await security.ReadAsync(actor));Assert.Contains("Service specialist",json);Assert.Contains("taskDigest",json);
            Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>security.ProfileAsync(actor,profile))).Status);
            var own=new UserSession{UserId=user.Id,TokenHash=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),TicketCiphertext=[1],SecurityStamp=user.SecurityStamp,CreatedAt=Now,UpdatedAt=Now,ExpiresAt=Now.AddHours(1),LastSeenAt=Now};db.Add(own);await db.SaveChangesAsync();
            await security.SessionsAsync(actor,null,false,"Sign out everywhere",default,true);
            Assert.NotNull((await db.Set<UserSession>().AsNoTracking().SingleAsync(x=>x.Id==own.Id)).RevokedAt);
        });
    }
}
