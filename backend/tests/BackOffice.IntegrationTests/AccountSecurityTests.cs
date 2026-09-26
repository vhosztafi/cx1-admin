using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(59L,"94287082")]
    [InlineData(1111111109L,"07081804")]
    [InlineData(1111111111L,"14050471")]
    [InlineData(1234567890L,"89005924")]
    [InlineData(2000000000L,"69279037")]
    [InlineData(20000000000L,"65353130")]
    public void AccountSecurityTotpMatchesRfc6238(long seconds,string expected)
    {
        var secret=LocalTotp.Base32(System.Text.Encoding.ASCII.GetBytes("12345678901234567890"));Assert.Equal(expected,LocalTotp.Code(secret,seconds/30,8));
        var now=DateTimeOffset.FromUnixTimeSeconds(seconds);var code=LocalTotp.Code(secret,seconds/30);Assert.Equal(seconds/30,LocalTotp.Verify(secret,code,now,null));Assert.Null(LocalTotp.Verify(secret,code,now,seconds/30));
    }
    private sealed class SecurityClock:TimeProvider{public DateTimeOffset Now{get;set;}=UnderwritingRuntimeTests.Now;public override DateTimeOffset GetUtcNow()=>Now;}
    [Fact]
    public async Task RealSqlAccountSecurityMfaChallengesRecoveryResetAndSessionOwnership()
    {
        await WithDatabase(async(db,password)=>
        {
            var factory=new AdministrationFactory(db.Database.GetConnectionString()!);var clock=new SecurityClock();var protection=new EphemeralDataProtectionProvider();var secrets=new IdentitySecrets(protection,clock);
            var service=new AccountSecurityService(factory,secrets,clock);var identity=new LocalIdentityService(factory,clock);var store=new SqlTicketStore(factory,protection,clock);
            var login=(await identity.AuthenticateAsync("servicing@cover.example",password,default))!;
            async Task<string> Ticket(LocalIdentity value)=>await store.StoreAsync(new(value.Principal,new AuthenticationProperties{ExpiresUtc=clock.Now.AddHours(8)},CookieAuthenticationDefaults.AuthenticationScheme));
            var original=await Ticket(login);var originalTicket=(await store.RetrieveAsync(original))!;var actor=AccountSecurityService.Identity(originalTicket.Principal);
            var enrol=await service.EnrolAsync(actor,password);Assert.Equal(32,enrol.Secret.Length);
            var code=LocalTotp.Code(enrol.Secret,clock.Now.ToUnixTimeSeconds()/30);var bad=code=="000000"?"000001":"000000";
            Assert.Null(await service.ConfirmAsync(actor,enrol.EnrolmentId,bad));var recovery=(await service.ConfirmAsync(actor,enrol.EnrolmentId,code))!;Assert.Equal(8,recovery.Length);Assert.Null(await store.RetrieveAsync(original));
            Assert.Null(await identity.AuthenticateAsync(login.View.Email,password,default));
            login=(await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,password,default))!;Assert.True(login.View.MfaEnabled);await Assert.ThrowsAsync<InvalidOperationException>(()=>Ticket(login));
            var challenge=await service.ChallengeAsync(login);Assert.Null(await service.CompleteLoginAsync(challenge,code));clock.Now=clock.Now.AddSeconds(30);code=LocalTotp.Code(enrol.Secret,clock.Now.ToUnixTimeSeconds()/30);
            var verified=(await service.CompleteLoginAsync(challenge,code))!;Assert.NotNull(verified);Assert.Null(await service.CompleteLoginAsync(challenge,code));var active=await Ticket(verified);Assert.NotNull(await store.RetrieveAsync(active));
            challenge=await service.ChallengeAsync((await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,password,default))!);Assert.Null(await service.CompleteLoginAsync(challenge,code));
            var attempts=await Task.WhenAll(service.CompleteLoginAsync(challenge,recovery[0]),service.CompleteLoginAsync(challenge,recovery[0]));Assert.Single(attempts,x=>x!=null);
            challenge=await service.ChallengeAsync((await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,password,default))!);Assert.Null(await service.CompleteLoginAsync(challenge,recovery[0]));
            clock.Now=clock.Now.AddMinutes(6);Assert.Null(await service.CompleteLoginAsync(challenge,recovery[1]));
            await service.ForgotAsync(login.View.Email);var reset=await db.Set<IdentityAction>().AsNoTracking().SingleAsync(x=>x.UserId==actor.UserId&&x.Kind=="password-reset"&&x.ConsumedAt==null);var token=secrets.Unprotect(reset.SecretCiphertext!);
            var newPassword="Updated!"+Guid.NewGuid().ToString("N");Assert.True(await service.ResetAsync(token,newPassword,recovery[1]));Assert.False(await service.ResetAsync(token,newPassword,recovery[2]));Assert.Null(await store.RetrieveAsync(active));
            Assert.Null(await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,password,default));login=(await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,newPassword,default))!;
            challenge=await service.ChallengeAsync(login);clock.Now=clock.Now.AddSeconds(30);verified=(await service.CompleteLoginAsync(challenge,LocalTotp.Code(enrol.Secret,clock.Now.ToUnixTimeSeconds()/30)))!;active=await Ticket(verified);
            var current=(await store.RetrieveAsync(active))!;actor=AccountSecurityService.Identity(current.Principal);Assert.NotNull(actor.SessionId);
            var otherLogin=(await identity.AuthenticateAsync("underwriter@cover.example",password,default))!;var otherKey=await Ticket(otherLogin);var otherTicket=(await store.RetrieveAsync(otherKey))!;var otherActor=AccountSecurityService.Identity(otherTicket.Principal);
            await service.SessionsAsync(actor,otherActor.SessionId,false,"Cannot revoke another user");Assert.NotNull(await store.RetrieveAsync(otherKey));
            await service.SessionsAsync(actor,actor.SessionId,false,"Revoke own session");Assert.Null(await store.RetrieveAsync(active));
            login=(await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,newPassword,default))!;
            challenge=await service.ChallengeAsync(login);
            for(var i=0;i<5;i++)Assert.Null(await service.CompleteLoginAsync(challenge,"not-a-factor"));
            Assert.Null(await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,newPassword,default));
            clock.Now=clock.Now.AddMinutes(16);
            login=(await identity.AuthenticatePasswordForChallengeAsync(login.View.Email,newPassword,default))!;challenge=await service.ChallengeAsync(login);
            verified=(await service.CompleteLoginAsync(challenge,LocalTotp.Code(enrol.Secret,clock.Now.ToUnixTimeSeconds()/30)))!;
            active=await Ticket(verified);actor=AccountSecurityService.Identity((await store.RetrieveAsync(active))!.Principal);
            var account=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==actor.UserId);
            await service.RequestIdentityAsync(actor,account.Email,account.TeamId!.Value,["servicing"],"Own protected change request");
            var admin=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="system-admin@cover.example");
            var adminActor=new BackOffice.Application.ActorContext(admin.Id,admin.TeamId,null,new HashSet<string>{"system-admin"});
            var users=new BackOffice.Infrastructure.Administration.UserAdministration(factory,new BackOffice.Infrastructure.Platform.SqlCommandBoundary(factory,clock),secrets,clock,new AdminEnvironment());
            var requestRow=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope.StartsWith("admin-request/")).SingleAsync();
            var request=System.Text.Json.JsonSerializer.Deserialize<BackOffice.Infrastructure.Administration.AdministrationRequest>(requestRow.Values,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            await users.DecideAsync(adminActor,request.Id,$"\"{requestRow.Id:N}-{requestRow.Version}\"",true,"Approve account owner request",Guid.NewGuid().ToString("N"));Assert.Null(await store.RetrieveAsync(active));
            var receipts=await db.Set<AuditEvent>().Select(x=>x.After).ToArrayAsync();Assert.DoesNotContain(receipts,x=>x!=null&&(x.Contains(enrol.Secret)||x.Contains(recovery[0])||x.Contains(token)));
        });
    }
}
