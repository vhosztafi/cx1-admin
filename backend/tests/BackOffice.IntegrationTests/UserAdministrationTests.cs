using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    private sealed class AdminEnvironment:IHostEnvironment
    {
        public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="Tests";public string ContentRootPath{get;set;}=".";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
    [Fact]
    public async Task RealSqlUserAdministrationIndependentApprovalInvitationAndRevocation()
    {
        await WithDatabase(async(db,password)=>
        {
            var factory=new AdministrationFactory(db.Database.GetConnectionString()!);var clock=new AdministrationClock(Now);var protection=new EphemeralDataProtectionProvider();
            var service=new UserAdministration(factory,new SqlCommandBoundary(factory,clock),new IdentitySecrets(protection,clock),clock,new AdminEnvironment());
            var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");var reviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="admin-reviewer@cover.example");
            ActorContext Actor(StaffUser u)=>new(u.Id,u.TeamId,null,new HashSet<string>{"system-admin"});
            var invited=await service.InviteAsync(Actor(admin),new("new-colleague@cover.example","New colleague",admin.TeamId!.Value,"Fictional onboarding"),Guid.NewGuid().ToString("N"));
            var deliveryId=JsonDocument.Parse(invited.Body).RootElement.GetProperty("deliveryId").GetGuid();var token=await service.RevealAsync(Actor(admin),deliveryId);
            Assert.DoesNotContain(token,invited.Body);Assert.True(await service.AcceptInvitationAsync(token,password));Assert.False(await service.AcceptInvitationAsync(token,password));
            var identity=new LocalIdentityService(factory,clock);var login=await identity.AuthenticateAsync("new-colleague@cover.example",password,default);Assert.NotNull(login);
            var store=new SqlTicketStore(factory,protection,clock);var ticketKey=await store.StoreAsync(new(login.Principal,new AuthenticationProperties{ExpiresUtc=Now.AddHours(8)},CookieAuthenticationDefaults.AuthenticationScheme));Assert.NotNull(await store.RetrieveAsync(ticketKey));
            var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==invited.ResourceId);
            var requestResult=await service.ProposeAsync(Actor(admin),new(user.Id,AdminAccess.Etag(user.RowVersion),user.Email,user.TeamId!.Value,["underwriter"],false,"Change responsibilities"),Guid.NewGuid().ToString("N"));
            var request=JsonSerializer.Deserialize<AdministrationRequest>(requestResult.Body,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(Actor(admin),request.Id,request.Etag,true,"Self approval",Guid.NewGuid().ToString("N")))).Status);
            await service.DecideAsync(Actor(reviewer),request.Id,request.Etag,true,"Independent review",Guid.NewGuid().ToString("N"));Assert.Null(await store.RetrieveAsync(ticketKey));
            login=await identity.AuthenticateAsync(user.Email,password,default);Assert.Contains("underwriter",login!.View.Roles);ticketKey=await store.StoreAsync(new(login.Principal,new AuthenticationProperties{ExpiresUtc=Now.AddHours(8)},CookieAuthenticationDefaults.AuthenticationScheme));
            user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==user.Id);var key=Guid.NewGuid().ToString("N");
            await service.RestrictAsync(Actor(admin),user.Id,AdminAccess.Etag(user.RowVersion),"suspend","Restrict access",key);Assert.Null(await store.RetrieveAsync(ticketKey));Assert.Null(await identity.AuthenticateAsync(user.Email,password,default));
            reviewer=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==reviewer.Id);await service.RestrictAsync(Actor(admin),reviewer.Id,AdminAccess.Etag(reviewer.RowVersion),"suspend","Test last administrator guard",Guid.NewGuid().ToString("N"));
            admin=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==admin.Id);Assert.Equal("last-active-administrator",(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.RestrictAsync(Actor(admin),admin.Id,AdminAccess.Etag(admin.RowVersion),"suspend","Would lock out administration",Guid.NewGuid().ToString("N")))).Code);
            var rows=await db.Set<AuditEvent>().Select(x=>x.After).ToArrayAsync();Assert.DoesNotContain(rows,x=>x!=null&&x.Contains(token));
        });
    }
}
