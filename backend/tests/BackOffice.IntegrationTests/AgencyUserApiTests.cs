using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Agencies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyUserApiTests
{
    [Fact]
    public async Task RealSqlAgencyUserApiScopesReadsAndPreservesVersionedLifecycle()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        try
        {
            Guid actorId,activeId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);actorId=(await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example")).Id;
                // Explicit active fixture: approval implementation belongs to04-06.
                var active=new Agency{Reference="AG-USERS-API",LegalName="Fictional active user API",State="active"};db.Add(active);db.Add(new AgencyOnboarding{AgencyId=active.Id,Details="{\"legalName\":\"Fictional active user API\"}"});await db.SaveChangesAsync();activeId=active.Id;
            }
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString)
                .UseSetting("Cover:DiagnosticWorkerEnabled","false").UseSetting("Cover:AgencyNotificationWorkerEnabled","false")
                .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","agency-user-api-keys",owned))));
            using var admin=host.CreateClient();var csrf=await Login(admin,"agency-admin",password);using var uw=host.CreateClient();await Login(uw,"underwriter",password);using var anonymous=host.CreateClient();
            using var draft=await Send(admin,HttpMethod.Post,"/api/v1/agencies",csrf,null,Key(),new{details=new{legalName="Fictional user API draft"},onboardingStep=2});draft.EnsureSuccessStatusCode();
            var draftId=(await draft.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();var draftPath=$"/api/v1/agencies/{draftId}";var invitePath=draftPath+"/invitations";var agencyVersion=draft.Headers.ETag!.ToString();
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(draftPath+"/users")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await uw.GetAsync(invitePath)).StatusCode);
            await Counts(0,0);var baselineKpis=await Read(admin,"/api/v1/agencies/kpis");var baselineTotal=baselineKpis.GetProperty("brokerUsers").GetInt32();var baselineInvited=baselineKpis.GetProperty("invitedUsers").GetInt32();
            async Task Counts(int total,int invited)
            {
                var detail=await Read(admin,draftPath);Assert.Equal(total,detail.GetProperty("userCount").GetInt32());Assert.Equal(invited,detail.GetProperty("invitedUserCount").GetInt32());
                var row=(await Read(admin,"/api/v1/agencies?q=Fictional%20user%20API%20draft")).GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==draftId);Assert.Equal(total,row.GetProperty("userCount").GetInt32());Assert.Equal(invited,row.GetProperty("invitedUserCount").GetInt32());
            }
            var input=new{email="first-user-api@cover.example",displayName="Fictional first user",role="broker-user"};var createKey=Key();
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,HttpMethod.Post,invitePath,null,agencyVersion,Key(),input)).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionRequired,(await Send(admin,HttpMethod.Post,invitePath,csrf,null,Key(),input)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Post,invitePath,csrf,agencyVersion,Key(),new{input.email,input.displayName,input.role,agencyId=activeId})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(admin,HttpMethod.Post,invitePath,csrf,agencyVersion,Key(),new{input.email,input.displayName,role="system-admin"})).StatusCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Send(admin,HttpMethod.Post,invitePath,csrf,agencyVersion,Key(),new{input.email,displayName=new string('a',9000),input.role})).StatusCode);
            using var created=await Send(admin,HttpMethod.Post,invitePath,csrf,agencyVersion,createKey,input);Assert.Equal(HttpStatusCode.Created,created.StatusCode);Assert.True(created.Headers.CacheControl!.NoStore);
            await Counts(1,1);
            var ids=await created.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(2,ids.EnumerateObject().Count());var userId=ids.GetProperty("id").GetGuid();var invitationId=ids.GetProperty("invitationId").GetGuid();
            using var replay=await Send(admin,HttpMethod.Post,invitePath,csrf,agencyVersion,createKey,input);Assert.Equal(await created.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
            var userPath=draftPath+"/users/"+userId;var user=await Read(admin,userPath);Assert.Equal("invited",user.GetProperty("state").GetString());Assert.False(user.TryGetProperty("securityStamp",out _));Assert.False(user.TryGetProperty("passwordHash",out _));
            Assert.False(user.TryGetProperty("lastSeenAt",out _));
            var lastSeen=DateTimeOffset.UtcNow.AddHours(-1);
            await using(var db=new BackOfficeDbContext(options))
            {
                // Historical revoked sessions are still evidence of past activity, never current access.
                foreach(var seen in new[]{lastSeen.AddHours(-2),lastSeen})db.Add(new UserSession{UserId=userId,TokenHash=RandomNumberGenerator.GetBytes(32),CreatedAt=lastSeen.AddDays(-1),ExpiresAt=lastSeen.AddDays(1),RevokedAt=lastSeen.AddMinutes(1),LastSeenAt=seen,DeviceLabel="Fictional history",SecurityStamp="fictional-revoked-session",TicketCiphertext=[]});
                await db.SaveChangesAsync();
            }
            Assert.Equal(lastSeen,(await Read(admin,userPath)).GetProperty("lastSeenAt").GetDateTimeOffset());
            var listedUser=(await Read(admin,draftPath+"/users")).GetProperty("items")[0];Assert.Equal(lastSeen,listedUser.GetProperty("lastSeenAt").GetDateTimeOffset());Assert.False(listedUser.TryGetProperty("tokenHash",out _));Assert.False(listedUser.TryGetProperty("ticketCiphertext",out _));
            var invitation=await Read(admin,invitePath+"/"+invitationId);Assert.Equal("staged",invitation.GetProperty("state").GetString());Assert.False(invitation.TryGetProperty("tokenHash",out _));Assert.False(invitation.TryGetProperty("expiresAt",out _));Assert.False(invitation.TryGetProperty("notificationId",out _));
            await AdministratorReadiness(admin,draftPath,"missing"); // A staged broker-user is not an administrator.
            using var second=await Send(admin,HttpMethod.Post,invitePath,csrf,created.Headers.ETag!.ToString(),Key(),new{email="second-user-api@cover.example",displayName="Fictional second user",role="broker-admin"});second.EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,draftPath,"satisfied");
            var adminIds=await second.Content.ReadFromJsonAsync<JsonElement>();var brokerId=adminIds.GetProperty("id").GetGuid();var brokerInvite=adminIds.GetProperty("invitationId").GetGuid();var brokerPath=draftPath+"/users/"+brokerId;
            Assert.False((await Read(admin,brokerPath)).TryGetProperty("lastSeenAt",out _));
            var stagedAdmin=await Read(admin,invitePath+"/"+brokerInvite);
            (await Send(admin,HttpMethod.Post,$"/api/v1/invitations/{brokerInvite}/revoke",csrf,stagedAdmin.GetProperty("etag").GetString(),Key(),new{reason="Fictional readiness revocation"})).EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,draftPath,"missing");
            var broker=await Read(admin,brokerPath);
            using var disabledAdmin=await Send(admin,HttpMethod.Post,brokerPath+"/deactivate",csrf,broker.GetProperty("etag").GetString(),Key(),new{reason="Fictional readiness removal"});disabledAdmin.EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,draftPath,"missing");
            using var restoredAdmin=await Send(admin,HttpMethod.Post,brokerPath+"/reactivate",csrf,disabledAdmin.Headers.ETag!.ToString(),Key(),new{reason="Fictional readiness restoration"});restoredAdmin.EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,draftPath,"satisfied");
            using var demotedAdmin=await Send(admin,HttpMethod.Put,brokerPath,csrf,restoredAdmin.Headers.ETag!.ToString(),Key(),new{displayName="Fictional second user",role="broker-readonly",reason="Fictional readiness role change"});demotedAdmin.EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,draftPath,"missing");
            (await Send(admin,HttpMethod.Put,brokerPath,csrf,demotedAdmin.Headers.ETag!.ToString(),Key(),new{displayName="Fictional second user",role="broker-admin",reason="Fictional readiness role restoration"})).EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,draftPath,"satisfied");
            await Counts(2,2);var currentKpis=await Read(admin,"/api/v1/agencies/kpis");Assert.Equal(baselineTotal+2,currentKpis.GetProperty("brokerUsers").GetInt32());Assert.Equal(baselineInvited+2,currentKpis.GetProperty("invitedUsers").GetInt32());
            var page=await Read(admin,draftPath+"/users?pageSize=1");Assert.Equal(2,page.GetProperty("totalCount").GetInt32());var cursor=page.GetProperty("nextCursor").GetString()!;
            Assert.Single((await Read(admin,draftPath+"/users?pageSize=1&cursor="+Uri.EscapeDataString(cursor))).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync($"/api/v1/agencies/{activeId}/users?pageSize=1&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(invitePath+"?userId=not-a-guid")).StatusCode);
            Assert.Single((await Read(admin,invitePath+"?userId="+userId)).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/agencies/{activeId}/users/{userId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/api/v1/agencies/{activeId}/invitations/{invitationId}")).StatusCode);
            var userVersion=user.GetProperty("etag").GetString();var editKey=Key();var edit=new{displayName="Fictional updated user",role="broker-readonly",reason="Fictional role change"};
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(admin,HttpMethod.Put,userPath,csrf,userVersion,Key(),new{edit.displayName,edit.role,edit.reason,email="forged@cover.example"})).StatusCode);
            using var edited=await Send(admin,HttpMethod.Put,userPath,csrf,userVersion,editKey,edit);Assert.Equal(HttpStatusCode.OK,edited.StatusCode);Assert.Single((await edited.Content.ReadFromJsonAsync<JsonElement>()).EnumerateObject());
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,HttpMethod.Put,userPath,csrf,userVersion,editKey,edit)).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(admin,HttpMethod.Put,userPath,csrf,userVersion,Key(),edit)).StatusCode);
            using var deactivated=await Send(admin,HttpMethod.Post,userPath+"/deactivate",csrf,edited.Headers.ETag!.ToString(),Key(),new{reason="Fictional removal"});deactivated.EnsureSuccessStatusCode();Assert.Equal("inactive",(await Read(admin,userPath)).GetProperty("state").GetString());
            await Counts(2,1);
            Assert.Equal("revoked",(await Read(admin,invitePath+"/"+invitationId)).GetProperty("state").GetString());
            using var restored=await Send(admin,HttpMethod.Post,userPath+"/reactivate",csrf,deactivated.Headers.ETag!.ToString(),Key(),new{reason="Fictional restoration"});restored.EnsureSuccessStatusCode();Assert.Equal("invited",(await Read(admin,userPath)).GetProperty("state").GetString());
            Assert.Equal(lastSeen,(await Read(admin,userPath)).GetProperty("lastSeenAt").GetDateTimeOffset());
            await Counts(2,2);
            var history=await Read(admin,invitePath+"?userId="+userId);Assert.Equal(2,history.GetProperty("totalCount").GetInt32());
            var activePath=$"/api/v1/agencies/{activeId}";using var activeResponse=await admin.GetAsync(activePath);activeResponse.EnsureSuccessStatusCode();
            using var issued=await Send(admin,HttpMethod.Post,activePath+"/invitations",csrf,activeResponse.Headers.ETag!.ToString(),Key(),new{email="active-user-api@cover.example",displayName="Fictional active user",role="broker-admin"});issued.EnsureSuccessStatusCode();var activeInvitation=(await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationId").GetGuid();
            var pending=await Read(admin,activePath+"/invitations/"+activeInvitation);Assert.Equal("pending",pending.GetProperty("state").GetString());Assert.True(pending.TryGetProperty("notificationId",out _));Assert.False(pending.TryGetProperty("protectedPayload",out _));
            await AdministratorReadiness(admin,activePath,"satisfied");
            await ReadinessAt(pending.GetProperty("issuedAt").GetDateTimeOffset().AddTicks(-1));
            await ReadinessAt(pending.GetProperty("expiresAt").GetDateTimeOffset());
            async Task ReadinessAt(DateTimeOffset instant)
            {
                await using var db=new BackOfficeDbContext(options);await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==activeId);
                var assessment=await new AgencyEvidenceService(null!,null!,new ReadinessClock(instant)).Validate(db,agency,default);
                Assert.Contains(assessment.Items,x=>x.Code=="broker-administrator"&&x.State=="missing");
            }
            var resendPath=$"/api/v1/invitations/{activeInvitation}/resend";var resendKey=Key();var pendingVersion=pending.GetProperty("etag").GetString();
            using var resent=await Send(admin,HttpMethod.Post,resendPath,csrf,pendingVersion,resendKey,new{reason="Fictional resend"});Assert.Equal(HttpStatusCode.Accepted,resent.StatusCode);
            using var resentReplay=await Send(admin,HttpMethod.Post,resendPath,csrf,pendingVersion,resendKey,new{reason="Fictional resend"});Assert.Equal(await resent.Content.ReadAsStringAsync(),await resentReplay.Content.ReadAsStringAsync());
            await AdministratorReadiness(admin,activePath,"satisfied");
            var replacement=(await resent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.NotEqual(activeInvitation,replacement);
            Assert.Equal("revoked",(await Read(admin,activePath+"/invitations/"+activeInvitation)).GetProperty("state").GetString());
            using var revoked=await Send(admin,HttpMethod.Post,$"/api/v1/invitations/{replacement}/revoke",csrf,resent.Headers.ETag!.ToString(),Key(),new{reason="Fictional revoke"});revoked.EnsureSuccessStatusCode();
            Assert.Equal("revoked",(await Read(admin,activePath+"/invitations/"+replacement)).GetProperty("state").GetString());
            await AdministratorReadiness(admin,activePath,"missing");
            var activity=await Read(admin,draftPath+"/activity");Assert.Contains(activity.GetProperty("items").EnumerateArray(),x=>x.GetProperty("summary").GetString()=="Agency user updated.");
            var demoInvitation=await AgencyInvitationDemo.Create(new PooledDbContextFactory<BackOfficeDbContext>(options),host.Services.GetRequiredService<AgencyNotificationPayload>());
            Guid demoAgency;await using(var db=new BackOfficeDbContext(options)){demoAgency=(await db.Set<AgencyInvitation>().SingleAsync(x=>x.Id==demoInvitation)).AgencyId;}
            Assert.Equal(HttpStatusCode.OK,(await admin.GetAsync($"/api/v1/agencies/{demoAgency}")).StatusCode);
            Assert.Contains((await Read(admin,"/api/v1/agencies?q=Fictional%20invitation%20demonstration")).GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==demoAgency);
            var demoPath=$"/api/v1/agencies/{demoAgency}";await AdministratorReadiness(admin,demoPath,"missing"); // An administrator in another agency cannot satisfy it.
            var demoUser=(await Read(admin,demoPath+"/users")).GetProperty("items")[0];var demoUserPath=demoPath+"/users/"+demoUser.GetProperty("id").GetGuid();
            (await Send(admin,HttpMethod.Put,demoUserPath,csrf,demoUser.GetProperty("etag").GetString(),Key(),new{displayName="Fictional accepted administrator",role="broker-admin",reason="Fictional credential readiness"})).EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,demoPath,"satisfied");
            using var reveal=await Send(admin,HttpMethod.Post,$"/api/v1/invitations/{demoInvitation}/demo-link",csrf,null,Key(),new{});reveal.EnsureSuccessStatusCode();var secret=(await reveal.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationToken").GetString();
            (await Send(admin,HttpMethod.Post,"/api/v1/auth/invitations/accept",csrf,null,Key(),new{invitationToken=secret,password})).EnsureSuccessStatusCode();
            await AdministratorReadiness(admin,demoPath,"satisfied"); // Accepted invitation plus active local credential.

            await using(var db=new BackOfficeDbContext(options)){var role=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==actorId&&x.RoleId==role.Id));await db.SaveChangesAsync();}
            Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync(userPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,HttpMethod.Post,invitePath,csrf,agencyVersion,createKey,input)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,HttpMethod.Post,resendPath,csrf,pendingVersion,resendKey,new{reason="Fictional resend"})).StatusCode);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private sealed class ReadinessClock(DateTimeOffset instant):TimeProvider {public override DateTimeOffset GetUtcNow()=>instant;}
    private static async Task AdministratorReadiness(HttpClient client,string path,string expected)
    {
        var detail=await Read(client,path);var checks=detail.GetProperty("validation").GetProperty("items").EnumerateArray();
        Assert.Equal(expected,checks.Single(x=>x.GetProperty("code").GetString()=="broker-administrator").GetProperty("state").GetString());
        Assert.False(detail.GetProperty("validation").GetProperty("valid").GetBoolean()); // Other prerequisites still apply.
    }
    private static string Key()=>Guid.NewGuid().ToString("N");
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {var csrf=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var response=await Send(client,HttpMethod.Post,"/api/v1/auth/login",csrf,null,Key(),new{email=role+"@cover.example",password});response.EnsureSuccessStatusCode();return(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;}
    private static Task<HttpResponseMessage> Send(HttpClient client,HttpMethod method,string path,string? csrf,string? etag,string key,object body)
    {var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);if(etag is not null)request.Headers.Add("If-Match",etag);request.Headers.Add("Idempotency-Key",key);return client.SendAsync(request);}
}
