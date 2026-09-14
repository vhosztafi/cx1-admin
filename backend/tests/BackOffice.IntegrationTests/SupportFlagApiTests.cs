using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class SupportFlagApiTests
{
    [Fact]
    public async Task RealSqlSupportFlagApiKeepsReceiptsSafeAndChangesHistoryAtomically()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
        var origin=PartyDemoSeed.RelationshipId(3,1);var target=PartyDemoSeed.RelationshipId(3,2);var person=ContactDemoSeed.PersonId(1);
        var list=$"/api/v1/relationships/{origin}/people/{person}/flags";var preview=$"/api/v1/relationships/{target}/support-instructions/preview";
        var activity="/api/v1/clients/"+PartyDemoSeed.ClientId(3)+"/activity";
        var request=Body([target]);Guid actorId;
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);actorId=await db.Set<StaffUser>().Where(x=>x.Email=="servicing@cover.example").Select(x=>x.Id).SingleAsync();}
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","flag-test-keys",ownedName))));
            using var anonymous=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(list)).StatusCode);
            using var staff=host.CreateClient();var csrf=await Login(staff,"servicing",password);
            using var limited=host.CreateClient();var limitedCsrf=await Login(limited,"agency-admin",password);
            Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(list)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await staff.GetAsync($"/api/v1/relationships/{target}/support-instructions")).StatusCode);
            var originalTag=await Tag(staff,"/api/v1/relationships/"+origin);var key=Guid.NewGuid().ToString("N");
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(limited,limitedCsrf,HttpMethod.Post,list,request,etag:originalTag)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,null,HttpMethod.Post,list,request,etag:originalTag)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(staff,csrf,HttpMethod.Post,list,request)).StatusCode);
            const string declined="FICTIONAL-DECLINED-DETAIL";
            using var rejection=await Send(staff,csrf,HttpMethod.Post,list,request with {ConsentBasis="declined",InternalInstruction=declined,Reason=declined},etag:originalTag);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,rejection.StatusCode);Assert.DoesNotContain(declined,await rejection.Content.ReadAsStringAsync());
            await using(var db=new BackOfficeDbContext(options))
            {Assert.False(await db.Set<SupportFlag>().AnyAsync());Assert.False(await db.Set<SupportFlagHistory>().AnyAsync());Assert.False(await db.Set<IdempotencyRecord>().AnyAsync());Assert.False(await db.Set<AuditEvent>().AnyAsync(x=>x.After!=null && x.After.Contains(declined)));}
            Assert.Equal(HttpStatusCode.NotFound,(await Send(staff,csrf,HttpMethod.Post,list,request with {VisibleRelationshipIds=[PartyDemoSeed.RelationshipId(1,1)]},etag:originalTag)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(staff,csrf,HttpMethod.Post,list,request with {ReviewOn=DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)},etag:originalTag)).StatusCode);
            using var created=await Send(staff,csrf,HttpMethod.Post,list,request,key,originalTag);Assert.Equal(HttpStatusCode.Created,created.StatusCode);
            var receipt=await created.Content.ReadFromJsonAsync<JsonElement>();Assert.Single(receipt.EnumerateObject());var id=receipt.GetProperty("id").GetGuid();var path="/api/v1/flags/"+id;var flagTag=created.Headers.ETag!.ToString();
            Assert.Equal(path,created.Headers.Location!.ToString());Assert.Equal(flagTag,await Tag(staff,path));Assert.Equal(request.InternalInstruction,(await Read(staff,path)).GetProperty("internalInstruction").GetString());
            Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(path+"/history")).StatusCode);
            var safe=await Read(limited,preview);Assert.Single(safe.GetProperty("items").EnumerateArray());Assert.False(safe.TryGetProperty("totalCount",out _));Assert.DoesNotContain("FICTIONAL-INTERNAL",safe.ToString());
            Assert.Empty((await Read(staff,$"/api/v1/relationships/{origin}/support-instructions/preview")).GetProperty("items").EnumerateArray());
            Assert.DoesNotContain("support-flag",(await Read(limited,activity)).ToString());Assert.Contains("support-flag.created",(await Read(staff,activity)).ToString());Assert.DoesNotContain("FICTIONAL-INTERNAL",(await Read(staff,activity)).ToString());

            // The same parent and flag version serialize concurrent reviewers into one winner.
            var amended=request with {InternalInstruction="FICTIONAL-INTERNAL-AMENDED",AgencyInstruction="Provide written follow-up.",Reason="Fictional amendment"};
            var race=await Task.WhenAll(Send(staff,csrf,HttpMethod.Put,path,amended,etag:flagTag),Send(staff,csrf,HttpMethod.Put,path,amended,etag:flagTag));
            Assert.Single(race,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);foreach(var response in race)response.Dispose();
            using var replay=await Send(staff,csrf,HttpMethod.Post,list,request,key,originalTag);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Equal(flagTag,replay.Headers.ETag!.ToString());Assert.Equal(receipt.ToString(),await replay.Content.ReadAsStringAsync());
            var history=await Read(staff,path+"/history");Assert.Equal(2,history.GetProperty("totalCount").GetInt32());Assert.Contains("amended",history.ToString());
            using var review=await Send(staff,csrf,HttpMethod.Put,path,amended with {Reason="Fictional review",ReviewOn=amended.ReviewOn.AddDays(1)},etag:await Tag(staff,path));Assert.Equal(HttpStatusCode.OK,review.StatusCode);
            Assert.Contains("reviewed",(await Read(staff,path+"/history")).ToString());

            // A crash after both service saves must roll back flag, grants and restricted history.
            var before=await Read(staff,path);var beforeHistory=(await Read(staff,path+"/history")).GetProperty("totalCount").GetInt32();
            var failed=new CommandIdentity(actorId,"/internal/flag-test","rollback-flag",Guid.NewGuid());var boundary=new SqlCommandBoundary(new Factory(options),TimeProvider.System);
            var actor=new ActorContext(actorId,null,null,new HashSet<string>{"servicing"});
            var version=Convert.FromBase64String((await Tag(staff,path))[1..^1]);
            await Assert.ThrowsAsync<InjectedFailure>(()=>boundary.ExecuteAsync(failed,new {change=true},"support-flag.changed",async(db,token)=>
            {
                await SupportFlagService.UpdateAsync(db,actor,id,version,SupportFlagRules.Validate(amended with {VisibleRelationshipIds=[],Reason="Fictional rolled back"},DateOnly.FromDateTime(DateTime.UtcNow)),DateTimeOffset.UtcNow,token);
                throw new InjectedFailure();
            }));
            Assert.Equal(before.ToString(),(await Read(staff,path)).ToString());Assert.Equal(beforeHistory,(await Read(staff,path+"/history")).GetProperty("totalCount").GetInt32());Assert.Single((await Read(limited,preview)).GetProperty("items").EnumerateArray());
            var later=new FutureTime();var laterBoundary=new SqlCommandBoundary(new Factory(options),later);
            var originalIntent=new {fields=SupportFlagRules.Validate(request),reason=(string?)null};
            var laterReplay=await laterBoundary.ExecuteAsync(new CommandIdentity(actorId,list,key,Guid.NewGuid()),originalIntent,"support-flag.created",async(db,token)=>
            {
                // Would reject the now-overdue review date if replay failed to bypass mutable prerequisites.
                await SupportFlagService.CreateAsync(db,actor,origin,person,Convert.FromBase64String(originalTag[1..^1]),originalIntent.fields,later.GetUtcNow(),token);
                throw new InvalidOperationException("Original receipt must be replayed.");
            });
            Assert.True(laterReplay.Replayed);Assert.Equal(flagTag,laterReplay.Etag);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key==failed.Key));Assert.False(await db.Set<AuditEvent>().AnyAsync(x=>x.CorrelationId==failed.CorrelationId));
                var receipts=await db.Set<IdempotencyRecord>().Select(x=>x.ResultBody).ToListAsync();Assert.All(receipts,x=>Assert.DoesNotContain("FICTIONAL-INTERNAL",x));
                Assert.True(await db.Set<AuditEvent>().AnyAsync(x=>x.EventType=="support.instructions-inspected"));
            }
            using var revoke=await Send(staff,csrf,HttpMethod.Put,path,amended with {VisibleRelationshipIds=[],Reason="Fictional sharing withdrawn"},etag:await Tag(staff,path));Assert.Equal(HttpStatusCode.OK,revoke.StatusCode);Assert.Empty((await Read(limited,preview)).GetProperty("items").EnumerateArray());
            await using(var db=new BackOfficeDbContext(options))
            {var row=await db.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==origin);row.State="inactive";await db.SaveChangesAsync();}
            using var ended=await Send(staff,csrf,HttpMethod.Post,path+"/end",new {reason="Fictional resolved after relationship ended"},etag:await Tag(staff,path));Assert.Equal(HttpStatusCode.OK,ended.StatusCode);Assert.True((await Read(staff,path)).TryGetProperty("endedAt",out _));
            Assert.Contains("ended",(await Read(staff,path+"/history")).ToString());
            await using(var db=new BackOfficeDbContext(options))
            {
                db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==actorId).ToListAsync());db.Add(new UserRole {UserId=actorId,RoleId=await db.Set<Role>().Where(x=>x.Code=="agency-admin").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,csrf,HttpMethod.Post,list,request,key,originalTag)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await staff.GetAsync(path+"/history")).StatusCode);
        }
        finally
        {
            if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();
        }
    }
    private static FlagWrite Body(Guid[] grants)=>new("vulnerability","health","FICTIONAL-INTERNAL-DETAIL","verbal-consent",DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),"FICTIONAL-INTERNAL-REASON",grants,"Allow additional time.");
    private sealed class InjectedFailure:Exception;
    private sealed class FutureTime:TimeProvider {public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow.AddYears(1);}
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options):IDbContextFactory<BackOfficeDbContext>{public BackOfficeDbContext CreateDbContext()=>new(options);}
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
    private static async Task<string> Tag(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return response.Headers.ETag!.ToString();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {
        var token=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using var response=await Send(client,token,HttpMethod.Post,"/api/v1/auth/login",new {email=role+"@cover.example",password});response.EnsureSuccessStatusCode();return (await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,HttpMethod method,string path,object body,string? key=null,string? etag=null)
    {
        var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag is not null)request.Headers.Add("If-Match",etag);return client.SendAsync(request);
    }
}
