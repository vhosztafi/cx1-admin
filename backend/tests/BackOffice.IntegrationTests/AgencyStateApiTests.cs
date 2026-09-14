using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyStateApiTests
{
    [Fact]
    public async Task RealSqlAgencyStateApiEnforcesIndependentScopedVersionedDecisions()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Guid.NewGuid().ToString("N")+"a1";var agencyId=PartyDemoSeed.FirstAgencyId;var path="/api/v1/agencies/"+agencyId;
        try
        {
            Guid actorId;string requesterLabel;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
                var actor=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actorId=actor.Id;requesterLabel=actor.DisplayName;
                // Active fixture isolates HTTP suspension behavior. Full activation is covered by the real service lifecycle test.
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==agencyId);agency.State="active";await db.SaveChangesAsync();
            }
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","state-api-test-keys",owned))));
            using var anonymous=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(path+"/state-requests")).StatusCode);
            using var requester=host.CreateClient();var csrf=await Login(requester,"agency-admin",password);
            using var reviewer=host.CreateClient();var reviewerCsrf=await Login(reviewer,"agency-reviewer",password);
            using var other=host.CreateClient();var otherCsrf=await Login(other,"system-admin",password);
            using var limited=host.CreateClient();var limitedCsrf=await Login(limited,"underwriter",password);
            Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(path+"/state-requests")).StatusCode);
            var basis=await Tag(requester,path);var reason=new{reason="Fictional independent suspension request"};
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(limited,limitedCsrf,path+"/suspend",reason,basis)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(requester,null,path+"/suspend",reason,basis)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(requester,csrf,path+"/suspend",reason,null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(requester,csrf,path+"/suspend",new{reason="forged",approvedBy=actorId},basis)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(requester,csrf,path+"/suspend",new{reason=" "},basis)).StatusCode);
            using(var wrongType=new HttpRequestMessage(HttpMethod.Post,path+"/suspend"){Content=new StringContent("{}",Encoding.UTF8,"text/plain")})
            {wrongType.Headers.Add("X-CSRF-Token",csrf);Assert.Equal(HttpStatusCode.UnsupportedMediaType,(await requester.SendAsync(wrongType)).StatusCode);}
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Send(requester,csrf,path+"/suspend",new{reason=new string('x',66000)},basis)).StatusCode);
            var key=Guid.NewGuid().ToString("N");using var proposal=await Send(requester,csrf,path+"/suspend",reason,basis,key);Assert.Equal(HttpStatusCode.Accepted,proposal.StatusCode);
            var receipt=await proposal.Content.ReadAsStringAsync();using var receiptJson=JsonDocument.Parse(receipt);Assert.Single(receiptJson.RootElement.EnumerateObject());var requestPath=proposal.Headers.Location!.ToString();var requestTag=proposal.Headers.ETag!.ToString();
            Assert.Equal(basis,await Tag(requester,path));using var replay=await Send(requester,csrf,path+"/suspend",reason,basis,key);Assert.Equal(receipt,await replay.Content.ReadAsStringAsync());Assert.Equal(requestTag,replay.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(requester,csrf,path+"/suspend",new{reason="changed intent"},basis,key)).StatusCode);
            var read=await Read(requester,requestPath);Assert.Equal("pending",read.GetProperty("state").GetString());Assert.Equal("suspension",read.GetProperty("requestKind").GetString());Assert.Equal(requesterLabel,read.GetProperty("requestedByLabel").GetString());Assert.Equal(basis,read.GetProperty("baseVersion").GetString());Assert.Equal(requestTag,read.GetProperty("etag").GetString());Assert.False(read.TryGetProperty("decisionBy",out _));
            Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(requestPath)).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await requester.GetAsync(requestPath+"?agencyId="+Guid.NewGuid())).StatusCode);
            var approve=new{outcome="approve",reason="Independent HTTP review"};var decisionPath=requestPath+"/decision";
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(requester,csrf,decisionPath,approve,requestTag)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(reviewer,reviewerCsrf,decisionPath,new{outcome="activate",reason="invalid"},requestTag)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await Send(reviewer,reviewerCsrf,decisionPath,new{outcome="approve",reason="forged",agencyId=Guid.NewGuid()},requestTag)).StatusCode);
            Assert.Equal(HttpStatusCode.PreconditionFailed,(await Send(reviewer,reviewerCsrf,decisionPath,approve,"\"AAAAAAAAAAA=\"")).StatusCode);
            var firstKey=Guid.NewGuid().ToString("N");var secondKey=Guid.NewGuid().ToString("N");
            var race=await Task.WhenAll(Send(reviewer,reviewerCsrf,decisionPath,approve,requestTag,firstKey),Send(other,otherCsrf,decisionPath,approve,requestTag,secondKey));
            Assert.Single(race,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);
            var winnerIndex=Array.FindIndex(race,x=>x.StatusCode==HttpStatusCode.OK);var winner=winnerIndex==0?reviewer:other;var winnerCsrf=winnerIndex==0?reviewerCsrf:otherCsrf;var winnerKey=winnerIndex==0?firstKey:secondKey;
            using var decisionReplay=await Send(winner,winnerCsrf,decisionPath,approve,requestTag,winnerKey);Assert.Equal(await race[winnerIndex].Content.ReadAsStringAsync(),await decisionReplay.Content.ReadAsStringAsync());Assert.Equal(race[winnerIndex].Headers.ETag,decisionReplay.Headers.ETag);foreach(var response in race)response.Dispose();
            Assert.Equal("suspended",(await Read(requester,path)).GetProperty("state").GetString());var decided=await Read(requester,requestPath);Assert.Equal("applied",decided.GetProperty("state").GetString());Assert.NotEqual(actorId,decided.GetProperty("decisionBy").GetGuid());Assert.True(decided.TryGetProperty("decisionByLabel",out _));
            Assert.Contains((await Read(requester,path+"/activity")).GetProperty("items").EnumerateArray(),x=>x.GetProperty("action").GetString()=="agency.suspended"&&x.GetProperty("summary").GetString()=="Agency suspended and access revoked.");
            // Incomplete drafts/reactivations reach their actual prerequisite service and cannot bypass it.
            var draftPath="/api/v1/agencies/"+PartyDemoSeed.SecondAgencyId;
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(requester,csrf,draftPath+"/activate",reason,await Tag(requester,draftPath))).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(requester,csrf,path+"/reactivate",reason,await Tag(requester,path))).StatusCode);
            // Stored pending kinds exercise rejection dispatch without fabricating readiness or approval.
            foreach(var kind in new[]{"activation","reactivation"})
            {
                Guid pendingId;var target=kind=="activation"?PartyDemoSeed.SecondAgencyId:agencyId;
                await using(var db=new BackOfficeDbContext(options)){var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==target);var pending=new AgencyStateRequest{AgencyId=target,Kind=kind,RequestedState="active",BaseVersion=agency.RowVersion,ProposedInputFingerprint=new string('a',64),RequestedBy=actorId,CreatedBy=actorId,RequestReason="Fictional rejection fixture"};db.Add(pending);await db.SaveChangesAsync();pendingId=pending.Id;}
                var pendingPath="/api/v1/agency-state-requests/"+pendingId;Assert.Equal(HttpStatusCode.OK,(await Send(reviewer,reviewerCsrf,pendingPath+"/decision",new{outcome="reject",reason="Incomplete proposal rejected"},await Tag(reviewer,pendingPath))).StatusCode);Assert.Equal("rejected",(await Read(requester,pendingPath)).GetProperty("state").GetString());
            }
            var page=await Read(requester,path+"/state-requests?pageSize=1");Assert.Equal(2,page.GetProperty("totalCount").GetInt32());var cursor=Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            Assert.Single((await Read(requester,path+"/state-requests?pageSize=1&cursor="+cursor)).GetProperty("items").EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest,(await requester.GetAsync(draftPath+"/state-requests?pageSize=1&cursor="+cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await requester.GetAsync("/api/v1/agency-state-requests/"+Guid.NewGuid())).StatusCode);
            await using(var db=new BackOfficeDbContext(options)){var winnerId=decided.GetProperty("decisionBy").GetGuid();db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==winnerId).ToListAsync());db.Add(new UserRole{UserId=winnerId,RoleId=await db.Set<Role>().Where(x=>x.Code=="underwriter").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();}
            Assert.Equal(HttpStatusCode.Forbidden,(await winner.GetAsync(requestPath)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(winner,winnerCsrf,decisionPath,approve,requestTag,winnerKey)).StatusCode);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<string> Tag(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return response.Headers.ETag!.ToString();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {
        var token=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=role+"@cover.example",password})};request.Headers.Add("X-CSRF-Token",token);using var response=await client.SendAsync(request);response.EnsureSuccessStatusCode();return(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,string path,object body,string? etag,string? key=null)
    {var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};if(csrf!=null)request.Headers.Add("X-CSRF-Token",csrf);request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag!=null)request.Headers.Add("If-Match",etag);return client.SendAsync(request);}
}
