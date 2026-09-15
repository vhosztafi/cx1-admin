using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyInvitationAcceptanceApiTests
{
    [Fact]
    public async Task RealSqlAgencyInvitationAcceptanceApiProtectsSecretsAndDemoReveal()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            Guid agencyId;ActorContext actor;byte[] version;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
                var staff=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(staff.Id,staff.TeamId,null,new HashSet<string>{"agency-admin"});
                var agency=new Agency{Reference="AG-ACCEPT-API",LegalName="Fictional acceptance API",State="active"};db.Add(agency);await db.SaveChangesAsync();agencyId=agency.Id;version=agency.RowVersion;
            }
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:AgencyNotificationWorkerEnabled","false")
                .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","invitation-api-keys",owned))));
            using var admin=host.CreateClient();using var anonymous=host.CreateClient();using var uw=host.CreateClient();
            var adminCsrf=await Login(admin,"agency-admin",password);var anonCsrf=await Csrf(anonymous);var uwCsrf=await Login(uw,"underwriter",password);
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=TimeProvider.System;var drafts=new AgencyDraftService(factory,new SqlCommandBoundary(factory,clock),clock);
            var payload=host.Services.GetRequiredService<AgencyNotificationPayload>();var issuer=new InvitationService(new AgencyNotificationService(payload,clock),clock);
            var created=await new AgencyUserService(drafts,new SqlCommandBoundary(factory,clock),clock,issuer).Invite(actor,agencyId,Guid.NewGuid().ToString(),version,AgencyUserRules.Validate("api-accepted@cover.example","Fictional API broker","broker-admin"));
            var invitationId=JsonDocument.Parse(created.Body).RootElement.GetProperty("invitationId").GetGuid();var path=$"/api/v1/invitations/{invitationId}/demo-link";
            Assert.Equal(HttpStatusCode.Unauthorized,(await Send(anonymous,path,anonCsrf,new{})).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(uw,path,uwCsrf,new{})).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,path,null,new{})).StatusCode);
            var production=new InvitationDemoReveal(factory,drafts,payload,clock,new EnvironmentStub());
            Assert.Equal(404,(await Assert.ThrowsAsync<AgencyCommandException>(()=>production.Reveal(actor,invitationId))).Status);
            using var revealed=await Send(admin,path,adminCsrf,new{});Assert.Equal(HttpStatusCode.OK,revealed.StatusCode);Assert.True(revealed.Headers.CacheControl!.NoStore);
            var raw=(await revealed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationToken").GetString()!;
            const string acceptPath="/api/v1/auth/invitations/accept";const string newPassword="Fictional accepted phrase!";
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(anonymous,acceptPath,null,new{invitationToken=raw,password=newPassword})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(anonymous,acceptPath,anonCsrf,new{invitationToken=raw,password=newPassword,role="system-admin"})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(anonymous,acceptPath,anonCsrf,new{invitationToken=raw,password="short"})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(anonymous,acceptPath,anonCsrf,new{invitationToken=raw,password=(string?)null})).StatusCode);
            using(var duplicate=new HttpRequestMessage(HttpMethod.Post,acceptPath){Content=new StringContent("{\"invitationToken\":\"malformed\",\"password\":\"Fictional first phrase\",\"password\":\"Fictional second phrase\"}",System.Text.Encoding.UTF8,"application/json")})
            {duplicate.Headers.Add("X-CSRF-Token",anonCsrf);Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.SendAsync(duplicate)).StatusCode);}
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Send(anonymous,acceptPath,anonCsrf,new{invitationToken=raw,password=new string('a',2500)})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(anonymous,acceptPath,anonCsrf,new{invitationToken="malformed",password=newPassword})).StatusCode);
            using var accepted=await Send(anonymous,acceptPath,anonCsrf,new{invitationToken=raw,password=newPassword});Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);Assert.True(accepted.Headers.CacheControl!.NoStore);
            var acceptedBody=await accepted.Content.ReadFromJsonAsync<JsonElement>();Assert.True(acceptedBody.GetProperty("accepted").GetBoolean());Assert.Single(acceptedBody.EnumerateObject());
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/account")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(anonymous,acceptPath,anonCsrf,new{invitationToken=raw,password="Different replacement password!"})).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(admin,path,adminCsrf,new{})).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await Send(anonymous,"/api/v1/auth/login",anonCsrf,new{email="api-accepted@cover.example",password=newPassword})).StatusCode);
            await using(var db=new BackOfficeDbContext(options))
            {
                var audits=await db.Set<AuditEvent>().Where(x=>x.SubjectRecordId==invitationId).ToListAsync();Assert.Single(audits,x=>x.EventType=="agency.invitation-demo-revealed");Assert.Single(audits,x=>x.EventType=="agency.invitation-accepted");
                Assert.DoesNotContain(audits,x=>(x.After??"").Contains(raw)||(x.After??"").Contains(newPassword));
                Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Route==acceptPath||x.Route==path));Assert.Single(await db.Set<UserCredential>().Where(x=>x.UserId==created.ResourceId).ToListAsync());
                var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==actor.UserId&&x.RoleId==role.Id));await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Unauthorized,(await Send(admin,path,adminCsrf,new{})).StatusCode);
            HttpStatusCode final=HttpStatusCode.OK;
            for(var i=0;i<25&&final!=HttpStatusCode.TooManyRequests;i++){using var limited=await Send(anonymous,acceptPath,anonCsrf,new{invitationToken="malformed",password=newPassword});final=limited.StatusCode;if(final==HttpStatusCode.TooManyRequests)Assert.NotNull(limited.Headers.RetryAfter);}
            Assert.Equal(HttpStatusCode.TooManyRequests,final);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static async Task<string> Csrf(HttpClient client)=>(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    private static async Task<string> Login(HttpClient client,string role,string password){using var response=await Send(client,"/api/v1/auth/login",await Csrf(client),new{email=role+"@cover.example",password});response.EnsureSuccessStatusCode();return await Csrf(client);}
    private static Task<HttpResponseMessage> Send(HttpClient client,string path,string? csrf,object body)
    {var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);return client.SendAsync(request);}
    private sealed class EnvironmentStub:IHostEnvironment
    {
        public string EnvironmentName{get;set;}="Production";public string ApplicationName{get;set;}="Test";public string ContentRootPath{get;set;}="";
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
    }
}
