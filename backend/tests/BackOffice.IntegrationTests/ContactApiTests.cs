using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class ContactApiTests
{
    [Fact]
    public async Task RealSqlContactApiRequiresScopeCsrfVersionsAndReplaysOriginalResponses()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        var keys=Path.GetFullPath(Path.Combine(".local","contact-test-keys",ownedName));
        var relationship=PartyDemoSeed.RelationshipId(1,1);var other=PartyDemoSeed.RelationshipId(1,2);
        var parent="/api/v1/relationships/"+relationship;var path=parent+"/contacts";
        try
        {
            await using(var db=new BackOfficeDbContext(options)){await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);}
            using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",keys));
            using var anonymous=factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(path)).StatusCode);
            using var staff=factory.CreateClient();var csrf=await Login(staff,"servicing",password);
            using var finance=factory.CreateClient();var financeCsrf=await Login(finance,"finance",password);
            Assert.Equal(HttpStatusCode.Forbidden,(await finance.GetAsync(path)).StatusCode);
            var input=Body("Fictional API Alex");var version=await Tag(staff,parent);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(finance,financeCsrf,HttpMethod.Post,path,input,etag:version)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,null,HttpMethod.Post,path,input,etag:version)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(staff,csrf,HttpMethod.Post,path,input)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(staff,csrf,HttpMethod.Post,path,input,etag:"W/"+version)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(staff,csrf,HttpMethod.Post,path,new {fullName="Fictional",role="Director",isPrimary=false,marketingConsent=Consent(),actorId=Guid.NewGuid()},etag:version)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(staff,csrf,HttpMethod.Post,path,new {fullName="Fictional",role="Director",isPrimary=false,marketingConsent=Consent(),email=(string?)null},etag:version)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(staff,csrf,HttpMethod.Post,path,new {fullName="Fictional",role="Director",isPrimary=false,marketingConsent=Consent(),personId=Guid.NewGuid()},etag:version)).StatusCode);
            var key=Guid.NewGuid().ToString("N");using var created=await Send(staff,csrf,HttpMethod.Post,path,input,key,version);
            Assert.Equal(HttpStatusCode.Created,created.StatusCode);var original=await created.Content.ReadAsStringAsync();var row=JsonDocument.Parse(original).RootElement;
            Assert.True(row.GetProperty("isPrimary").GetBoolean());var contactPath=created.Headers.Location!.ToString();var firstTag=created.Headers.ETag!.ToString();
            Assert.Equal(firstTag,await Tag(staff,contactPath));Assert.NotEqual(version,await Tag(staff,parent));
            Assert.Equal(HttpStatusCode.NotFound,(await staff.GetAsync($"/api/v1/relationships/{other}/contacts/{row.GetProperty("id").GetGuid()}")).StatusCode);
            using var second=await Send(staff,csrf,HttpMethod.Post,path,Body("Fictional API Sam"),etag:await Tag(staff,parent));
            Assert.Equal(HttpStatusCode.Created,second.StatusCode);var secondPath=second.Headers.Location!.ToString();
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,HttpMethod.Put,contactPath,Body("Fictional demoted"),etag:firstTag)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,HttpMethod.Post,contactPath+"/end",new {reason="Fictional end"},etag:firstTag)).StatusCode);
            using var promoted=await Send(staff,csrf,HttpMethod.Post,secondPath+"/make-primary",null,etag:second.Headers.ETag!.ToString());Assert.Equal(HttpStatusCode.OK,promoted.StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(staff,csrf,HttpMethod.Put,contactPath,Body("Fictional stale"),etag:firstTag)).StatusCode);
            using var updated=await Send(staff,csrf,HttpMethod.Put,contactPath,Body("Fictional edited"),etag:await Tag(staff,contactPath));Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
            using var replay=await Send(staff,csrf,HttpMethod.Post,path,input,key,version);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);
            Assert.Equal(original,await replay.Content.ReadAsStringAsync());Assert.Equal(firstTag,replay.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,HttpMethod.Post,path,Body("Changed intent"),key,version)).StatusCode);
            var page=await Read(staff,path+"?pageSize=1");Assert.Equal(2,page.GetProperty("totalCount").GetInt32());
            var cursor=Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            Assert.Single((await Read(staff,path+"?pageSize=1&cursor="+cursor)).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync(path+"?pageSize=1&includeEnded=true&cursor="+cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync(path+"?includeEnded=invalid")).StatusCode);
            using var ended=await Send(staff,csrf,HttpMethod.Post,contactPath+"/end",new {reason="Fictional private reason"},etag:updated.Headers.ETag!.ToString());Assert.Equal(HttpStatusCode.OK,ended.StatusCode);
            Assert.DoesNotContain("private reason",await ended.Content.ReadAsStringAsync());
            Assert.Equal(1,(await Read(staff,path)).GetProperty("totalCount").GetInt32());Assert.Equal(2,(await Read(staff,path+"?includeEnded=true")).GetProperty("totalCount").GetInt32());
            Assert.Equal(HttpStatusCode.OK,(await staff.GetAsync(contactPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,HttpMethod.Put,contactPath,Body("Fictional ended edit"),etag:ended.Headers.ETag!.ToString())).StatusCode);
            var activity=await Read(staff,"/api/v1/clients/"+PartyDemoSeed.ClientId(1)+"/activity");
            Assert.Contains("Relationship contact ended.",activity.ToString());Assert.DoesNotContain("private reason",activity.ToString());Assert.Contains("contact",activity.ToString());
            await using(var db=new BackOfficeDbContext(options))
            {
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="servicing@cover.example");
                var roles=await db.Set<UserRole>().Where(x=>x.UserId==user.Id).ToListAsync();db.RemoveRange(roles);
                db.Add(new UserRole {UserId=user.Id,RoleId=await db.Set<Role>().Where(x=>x.Code=="finance").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,csrf,HttpMethod.Post,path,input,key,version)).StatusCode);
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static object Consent()=>new {state="not-asked",email=false,telephone=false,recordedAt=DateTimeOffset.UtcNow.AddMinutes(-1),source="Fictional test"};
    private static object Body(string name)=>new {fullName=name,role="Director",isPrimary=false,marketingConsent=Consent()};
    private static async Task<JsonElement> Read(HttpClient client,string path)
    {using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
    private static async Task<string> Tag(HttpClient client,string path)
    {using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return response.Headers.ETag!.ToString();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {
        var token=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var response=await Send(client,token,HttpMethod.Post,"/api/v1/auth/login",new {email=role+"@cover.example",password});response.EnsureSuccessStatusCode();
        return (await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,HttpMethod method,string path,object? body,string? key=null,string? etag=null)
    {
        var request=new HttpRequestMessage(method,path);if(body is not null)request.Content=JsonContent.Create(body);
        if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);
        request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag is not null)request.Headers.Add("If-Match",etag);
        return client.SendAsync(request);
    }
}
