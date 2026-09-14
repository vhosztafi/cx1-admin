using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class ClientApiTests
{
    [Fact]
    public async Task RealSqlClientApiPersistsSerializesReplaysAndRechecksPermissions()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql => sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        var keys=Path.GetFullPath(Path.Combine(".local","client-test-keys",ownedName));
        try
        {
            await using(var db=new BackOfficeDbContext(options)){await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);}
            using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",keys));
            using var anonymous=factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/clients")).StatusCode);
            using var staff=factory.CreateClient();var csrf=await Login(staff,"servicing",password);
            using var agencyAdmin=factory.CreateClient();var agencyCsrf=await Login(agencyAdmin,"agency-admin",password);
            using var admin=factory.CreateClient();await Login(admin,"system-admin",password);
            Assert.Equal(HttpStatusCode.Forbidden,(await admin.GetAsync("/api/v1/clients")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await agencyAdmin.GetAsync("/api/v1/clients")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(agencyAdmin,agencyCsrf,HttpMethod.Post,"/api/v1/clients",Body("Forbidden"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,null,HttpMethod.Post,"/api/v1/clients",Body("No CSRF"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",Body("No key"),key:"")).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",new {legalName="Null optional",entityType="llp",address=Address(),companyNumber=(string?)null})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",new {legalName="Injected",entityType="llp",address=Address(),id=Guid.NewGuid()})).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",Body(" "))).StatusCode);
            var createKey=Guid.NewGuid().ToString("N");var body=Body("Fictional API Traders");
            var creates=await Task.WhenAll(Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",body,createKey),Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",body,createKey));
            Assert.All(creates,x=>Assert.Equal(HttpStatusCode.Created,x.StatusCode));
            var original=await creates[0].Content.ReadAsStringAsync();Assert.Equal(original,await creates[1].Content.ReadAsStringAsync());
            var id=JsonDocument.Parse(original).RootElement.GetProperty("id").GetGuid();var route="/api/v1/clients/"+id;
            var initialTag=creates[0].Headers.ETag!.ToString();Assert.Equal(initialTag,creates[1].Headers.ETag!.ToString());
            Assert.Equal(route,creates[0].Headers.Location!.ToString());
            foreach(var response in creates)response.Dispose();
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",Body("Different"),createKey)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(staff,csrf,HttpMethod.Put,route,body)).StatusCode);
            var changed=Body("Fictional Amended API Traders");var updateKey=Guid.NewGuid().ToString("N");
            using var update=await Send(staff,csrf,HttpMethod.Put,route,changed,updateKey,initialTag);
            Assert.Equal(HttpStatusCode.OK,update.StatusCode);var updatedTag=update.Headers.ETag!.ToString();Assert.NotEqual(initialTag,updatedTag);
            using var replay=await Send(staff,csrf,HttpMethod.Put,route,changed,updateKey,initialTag);
            Assert.Equal(HttpStatusCode.OK,replay.StatusCode);Assert.Equal(updatedTag,replay.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(staff,csrf,HttpMethod.Put,route,body,etag:initialTag)).StatusCode);
            var competing=await Task.WhenAll(Send(staff,csrf,HttpMethod.Put,route,Body("Fictional concurrent A"),etag:updatedTag),Send(staff,csrf,HttpMethod.Put,route,Body("Fictional concurrent B"),etag:updatedTag));
            Assert.Single(competing,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(competing,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);
            foreach(var response in competing)response.Dispose();
            using var createReplay=await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",body,createKey);
            Assert.Equal(original,await createReplay.Content.ReadAsStringAsync());Assert.Equal(initialTag,createReplay.Headers.ETag!.ToString());
            using var current=await staff.GetAsync(route);Assert.Equal(HttpStatusCode.OK,current.StatusCode);
            var relationshipKey=Guid.NewGuid().ToString("N");var parentTag=current.Headers.ETag!.ToString();
            using var relationship=await Send(staff,csrf,HttpMethod.Post,route+"/relationships",new {agencyId=PartyDemoSeed.FirstAgencyId},relationshipKey,parentTag);
            Assert.Equal(HttpStatusCode.Created,relationship.StatusCode);
            using var relationshipReplay=await Send(staff,csrf,HttpMethod.Post,route+"/relationships",new {agencyId=PartyDemoSeed.FirstAgencyId},relationshipKey,parentTag);
            Assert.Equal(HttpStatusCode.Created,relationshipReplay.StatusCode);Assert.Equal(relationship.Headers.ETag,relationshipReplay.Headers.ETag);
            using var parent=await staff.GetAsync(route);Assert.NotEqual(parentTag,parent.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,HttpMethod.Post,route+"/relationships",new {agencyId=PartyDemoSeed.FirstAgencyId},etag:parent.Headers.ETag!.ToString())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Send(staff,csrf,HttpMethod.Post,route+"/relationships",new {agencyId=Guid.NewGuid()},etag:parent.Headers.ETag!.ToString())).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await staff.GetAsync(relationship.Headers.Location)).StatusCode);
            Assert.Equal(1,(await Read(staff,route+"/relationships")).GetProperty("totalCount").GetInt32());
            Assert.Equal(2,(await Read(staff,"/api/v1/relationship-agencies")).GetProperty("totalCount").GetInt32());
            var agencyPage=await Read(staff,"/api/v1/relationship-agencies?pageSize=1");
            Assert.Single(agencyPage.GetProperty("items").EnumerateArray());
            var nextAgency=await Read(staff,"/api/v1/relationship-agencies?pageSize=1&cursor="+Uri.EscapeDataString(agencyPage.GetProperty("nextCursor").GetString()!));
            Assert.NotEqual(agencyPage.GetProperty("items")[0].GetProperty("id").GetGuid(),nextAgency.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.False(nextAgency.TryGetProperty("nextCursor",out _));
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await staff.GetAsync(route+"/records")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await staff.GetAsync("/api/v1/clients/"+Guid.NewGuid()+"/records")).StatusCode);
            var activity=await Read(staff,route+"/activity");Assert.Equal(4,activity.GetProperty("totalCount").GetInt32());
            Assert.DoesNotContain("concurrent",activity.ToString());
            var page=await Read(staff,"/api/v1/clients?pageSize=2");Assert.Equal(33,page.GetProperty("totalCount").GetInt32());
            Assert.Equal("unavailable",page.GetProperty("items")[0].GetProperty("records").GetProperty("state").GetString());
            var cursor=Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            var next="/api/v1/clients?pageSize=2&cursor="+cursor;
            var second=await Read(staff,next);Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id").GetGuid(),second.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.BadRequest,(await agencyAdmin.GetAsync(next)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync(next+"&q=other")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/v1/clients?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/v1/clients?q=a&q=b")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/v1/clients?agencyId="+PartyDemoSeed.FirstAgencyId)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/v1/clients?entityType=unknown")).StatusCode);
            var entityPage=await Read(staff,"/api/v1/clients?entityType=llp");
            Assert.All(entityPage.GetProperty("items").EnumerateArray(),x=>Assert.Equal("llp",x.GetProperty("entityType").GetString()));
            Assert.Equal(0,(await Read(staff,"/api/v1/clients?q=%25")).GetProperty("totalCount").GetInt32());
            Assert.Equal(1,(await Read(staff,"/api/v1/clients?q=concurrent")).GetProperty("totalCount").GetInt32());
            var agencySearch=await Read(staff,"/api/v1/clients?q=AG-DEMO-01&pageSize=100");
            Assert.Contains(agencySearch.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==id);
            Assert.Equal(agencySearch.GetProperty("totalCount").GetInt32(),(await Read(staff,"/api/v1/clients?q=brightside&pageSize=100")).GetProperty("totalCount").GetInt32());
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(4,await db.Set<IdempotencyRecord>().CountAsync());
                Assert.Equal(1,await db.Set<AuditEvent>().CountAsync(x=>x.EventType=="client.created"));
                Assert.Equal(2,await db.Set<AuditEvent>().CountAsync(x=>x.EventType=="client.updated"));
                Assert.Equal(1,await db.Set<AuditEvent>().CountAsync(x=>x.EventType=="client.relationship-created"));
                db.Add(new ClientActivity {ClientId=id,EventType="client.updated",RecordId=PartyDemoSeed.ClientId(2),RecordKind="client"});
                db.Add(new ClientActivity {ClientId=id,EventType="sensitive.internal-event"});
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="servicing@cover.example");
                var servicingRole=await db.Set<Role>().SingleAsync(x=>x.Code=="servicing");
                var readRole=await db.Set<Role>().SingleAsync(x=>x.Code=="agency-admin");
                db.Remove(await db.Set<UserRole>().SingleAsync(x=>x.UserId==user.Id && x.RoleId==servicingRole.Id));
                db.Add(new UserRole {UserId=user.Id,RoleId=readRole.Id});await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync(next)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,csrf,HttpMethod.Post,"/api/v1/clients",body,createKey)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await staff.GetAsync(route)).StatusCode);
            var safeActivity=await Read(staff,route+"/activity");
            Assert.Equal(5,safeActivity.GetProperty("totalCount").GetInt32());
            Assert.DoesNotContain(PartyDemoSeed.ClientId(2).ToString(),safeActivity.ToString());
            Assert.DoesNotContain("sensitive",safeActivity.ToString());
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");
            await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static object Address()=>new {line1="1 Fictional Road",town="Sheffield",postcode="S1 1AA",country="GB"};
    private static object Body(string name)=>new {legalName=name,entityType="llp",address=Address()};
    private static async Task<JsonElement> Read(HttpClient client,string path)
    {using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {
        var token=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var response=await Send(client,token,HttpMethod.Post,"/api/v1/auth/login",new {email=role+"@cover.example",password});response.EnsureSuccessStatusCode();
        return (await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,HttpMethod method,string path,object body,string? key=null,string? etag=null)
    {
        var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};
        if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);
        if(key!="")request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));
        if(etag is not null)request.Headers.Add("If-Match",etag);
        return client.SendAsync(request);
    }
}
