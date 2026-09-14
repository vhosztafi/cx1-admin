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

public sealed class MatchApiTests
{
    [Fact]
    public async Task RealSqlMatchApiDecisionsReplaySerializeAndNeverMergeCompetingRecords()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";Guid actorId;var first=Path(1);var separate=Path(3);
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password,includeSupportFlags:true,includeMatches:true);actorId=await db.Set<StaffUser>().Where(x=>x.Email=="underwriter@cover.example").Select(x=>x.Id).SingleAsync();}
            var competingBefore=await Competing(options);
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",System.IO.Path.GetFullPath(System.IO.Path.Combine(".local","match-test-keys",ownedName))));
            using var anonymous=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/matches")).StatusCode);
            using var staff=host.CreateClient();var csrf=await Login(staff,"underwriter",password);
            using var limited=host.CreateClient();var limitedCsrf=await Login(limited,"servicing",password);
            Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(first)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await limited.GetAsync(first+"/decisions")).StatusCode);
            var page=await Read(staff,"/api/v1/matches?pageSize=2");Assert.Equal(6,page.GetProperty("totalCount").GetInt32());var cursor=Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);
            Assert.Equal(2,(await Read(staff,"/api/v1/matches?pageSize=2&cursor="+cursor)).GetProperty("items").GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/v1/matches?pageSize=2&state=pending&cursor="+cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await staff.GetAsync("/api/v1/matches?state=unknown")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await staff.GetAsync("/api/v1/matches/"+Guid.NewGuid())).StatusCode);
            var original=await Read(staff,first);Assert.False(original.TryGetProperty("quoteId",out _));Assert.False(original.GetProperty("submission").TryGetProperty("quoteId",out _));
            Assert.Equal(original.GetProperty("submissionId").GetGuid(),original.GetProperty("submission").GetProperty("id").GetGuid());Assert.Equal(original.GetProperty("ruleVersionId").GetGuid(),original.GetProperty("rule").GetProperty("id").GetGuid());
            var tag=await Tag(staff,first);var query=new MatchDecisionWrite("query","FICTIONAL-RESTRICTED-COMPARISON-REASON");
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,null,first,query,etag:tag)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(limited,limitedCsrf,first,query,etag:tag)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(staff,csrf,first,query)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(staff,csrf,first,query with {Reason=" "},etag:tag)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(staff,csrf,first,new MatchDecisionWrite("link","Fictional wrong candidate",Guid.NewGuid()),etag:tag)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,first,new MatchDecisionWrite("reopen","Fictional invalid transition"),etag:tag)).StatusCode);
            var queryKey=Guid.NewGuid().ToString("N");using var queried=await Send(staff,csrf,first,query,queryKey,tag);Assert.Equal(HttpStatusCode.OK,queried.StatusCode);
            var receipt=await queried.Content.ReadAsStringAsync();Assert.Single(JsonDocument.Parse(receipt).RootElement.EnumerateObject());var queryTag=queried.Headers.ETag!.ToString();
            using var repeated=await Send(staff,csrf,first,query,queryKey,tag);Assert.Equal(HttpStatusCode.OK,repeated.StatusCode);Assert.Equal(receipt,await repeated.Content.ReadAsStringAsync());Assert.Equal(queryTag,repeated.Headers.ETag!.ToString());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,first,query with {Reason="Changed intent"},queryKey,tag)).StatusCode);
            await Decide(first,"query","Fictional follow-up question");Assert.Equal(2,(await Read(staff,first+"/information-requests")).GetProperty("totalCount").GetInt32());
            await using(var db=new BackOfficeDbContext(options))
            {var name=await db.Set<StaffUser>().Where(x=>x.Id==actorId).Select(x=>x.DisplayName).SingleAsync();Assert.All((await Read(staff,first+"/decisions")).GetProperty("items").EnumerateArray(),row=>Assert.Equal(name,row.GetProperty("actorLabel").GetString()));}
            await Decide(first,"decline","Fictional intake declined");
            await using(var db=new BackOfficeDbContext(options))
            {
                var own=new MatchScope(new ActorContext(actorId,null,PartyDemoSeed.SecondAgencyId,new HashSet<string>{"agency-admin"}));
                var safe=await own.SubmissionOutcomes(db).SingleAsync(x=>x.SubmissionId==MatchDemoSeed.SubmissionId(1));
                var safeJson=JsonSerializer.Serialize(safe,new JsonSerializerOptions(JsonSerializerDefaults.Web));using var parsed=JsonDocument.Parse(safeJson);
                Assert.Equal(new[] {"state","submissionId"},parsed.RootElement.EnumerateObject().Select(x=>x.Name).Order());Assert.Equal("declined",safe.State);Assert.DoesNotContain("candidate",safeJson);
                Assert.False(await new MatchScope(new ActorContext(actorId,null,PartyDemoSeed.FirstAgencyId,new HashSet<string>{"agency-admin"})).SubmissionOutcomes(db).AnyAsync(x=>x.SubmissionId==safe.SubmissionId));
            }
            Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,first,new MatchDecisionWrite("link","Fictional terminal link"),etag:await Tag(staff,first))).StatusCode);
            await Decide(first,"reopen","Fictional reconsideration");await Decide(first,"link","Fictional existing relationship link");
            var linked=(await Read(staff,first)).GetProperty("submission");Assert.Equal(PartyDemoSeed.ClientId(3),linked.GetProperty("linkedClientId").GetGuid());Assert.Equal(PartyDemoSeed.RelationshipId(3,2),linked.GetProperty("linkedRelationshipId").GetGuid());
            var activity="/api/v1/clients/"+PartyDemoSeed.ClientId(3)+"/activity";
            Assert.DoesNotContain("match.",(await Read(limited,activity)).ToString());var visibleActivity=(await Read(staff,activity)).ToString();Assert.Contains("match.link",visibleActivity);Assert.DoesNotContain(query.Reason!,visibleActivity);
            var countsBefore=await Counts(options);
            var separateKey=Guid.NewGuid().ToString("N");var separateTag=await Tag(staff,separate);var separateInput=new MatchDecisionWrite("separate","Fictional distinct business");
            using var separated=await Send(staff,csrf,separate,separateInput,separateKey,separateTag);Assert.Equal(HttpStatusCode.OK,separated.StatusCode);
            var separateId=(await Read(staff,separate)).GetProperty("submission").GetProperty("linkedClientId").GetGuid();Assert.NotEqual(PartyDemoSeed.ClientId(3),separateId);
            await Decide(separate,"reopen","Fictional recheck");await Decide(separate,"link","Fictional candidate link after recheck");await Decide(separate,"reopen","Fictional restore separate decision");await Decide(separate,"separate","Fictional retain separate account");
            Assert.Equal(separateId,(await Read(staff,separate)).GetProperty("submission").GetProperty("linkedClientId").GetGuid());
            using var separateReplay=await Send(staff,csrf,separate,separateInput,separateKey,separateTag);Assert.Equal(HttpStatusCode.OK,separateReplay.StatusCode);Assert.Equal(separated.Headers.ETag!.ToString(),separateReplay.Headers.ETag!.ToString());
            var after=await Counts(options);Assert.Equal(countsBefore.Clients+1,after.Clients);Assert.Equal(countsBefore.Relationships+1,after.Relationships);Assert.Equal(countsBefore.Contacts,after.Contacts);Assert.Equal(countsBefore.Flags,after.Flags);Assert.Equal(countsBefore.Grants,after.Grants);
            await Decide(separate,"reopen","Fictional inactive-relationship check");
            await using(var db=new BackOfficeDbContext(options)){var relationship=await db.Set<ClientAgencyRelationship>().SingleAsync(x=>x.ClientId==separateId);relationship.State="inactive";await db.SaveChangesAsync();}
            var inactiveBefore=await Counts(options);Assert.Equal(HttpStatusCode.Conflict,(await Send(staff,csrf,separate,new MatchDecisionWrite("separate","Fictional cannot reactivate implicitly"),etag:await Tag(staff,separate))).StatusCode);Assert.Equal(inactiveBefore,await Counts(options));
            await using(var db=new BackOfficeDbContext(options))
            {
                var account=await db.Set<ClientAccount>().SingleAsync(x=>x.Id==separateId);Assert.Equal("Fictional Intake Traders 03",account.LegalName);
                Assert.False(await db.Set<Contact>().AnyAsync(x=>x.ClientId==separateId));Assert.False(await db.Set<SupportFlag>().AnyAsync(x=>x.ClientId==separateId));
            }
            // Two distinct commands against the same version have exactly one winner.
            var raceTag=await Tag(staff,Path(2));var priorTrail=(await Read(staff,Path(2)+"/decisions")).GetProperty("totalCount").GetInt32();
            var race=await Task.WhenAll(Send(staff,csrf,Path(2),new MatchDecisionWrite("query","Fictional race request"),etag:raceTag),Send(staff,csrf,Path(2),new MatchDecisionWrite("decline","Fictional race decline"),etag:raceTag));
            Assert.Single(race,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);foreach(var response in race)response.Dispose();Assert.Equal(priorTrail+1,(await Read(staff,Path(2)+"/decisions")).GetProperty("totalCount").GetInt32());
            // Rollback after the service flush removes every effect, including a new client or request.
            var actor=new ActorContext(actorId,null,null,new HashSet<string>{"underwriter"});var boundary=new SqlCommandBoundary(new Factory(options),TimeProvider.System);
            foreach(var outcome in new[] {"separate","query"})
            {
                var before=await Counts(options);var reviewBefore=(await Read(staff,Path(4))).ToString();var command=new CommandIdentity(actorId,"/internal/match-rollback",Guid.NewGuid().ToString("N"),Guid.NewGuid());var version=Convert.FromBase64String((await Tag(staff,Path(4)))[1..^1]);
                await Assert.ThrowsAsync<InjectedFailure>(()=>boundary.ExecuteAsync(command,new {outcome},"match."+outcome,async(db,token)=>
                {await MatchService.DecideAsync(db,actor,MatchDemoSeed.ReviewId(4),version,MatchRules.Validate(new(outcome,"Fictional rolled back")),DateTimeOffset.UtcNow,token);throw new InjectedFailure();}));
                Assert.Equal(before,await Counts(options));Assert.Equal(reviewBefore,(await Read(staff,Path(4))).ToString());
                await using var db=new BackOfficeDbContext(options);Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key==command.Key));Assert.False(await db.Set<AuditEvent>().AnyAsync(x=>x.CorrelationId==command.CorrelationId));
            }
            // A Link can create a missing submission-agency relationship without copying contacts.
            Guid extraId;
            await using(var db=new BackOfficeDbContext(options))
            {
                var source=await db.Set<MatchReview>().SingleAsync(x=>x.Id==MatchDemoSeed.ReviewId(1));var sourceIntake=await db.Set<MatchSubmission>().SingleAsync(x=>x.Id==source.SubmissionId);
                var intake=new MatchSubmission {Reference="MI-API-EXTRA",AgencyId=PartyDemoSeed.SecondAgencyId,IdentitySnapshot=sourceIntake.IdentitySnapshot};
                var review=new MatchReview {SubmissionId=intake.Id,CandidateClientId=PartyDemoSeed.ClientId(4),CandidateRelationshipId=PartyDemoSeed.RelationshipId(4,1),RuleVersionId=source.RuleVersionId,RuleSnapshot=source.RuleSnapshot,Signals=source.Signals};
                db.AddRange(intake,review);await db.SaveChangesAsync();extraId=review.Id;
            }
            var extra="/api/v1/matches/"+extraId;await Decide(extra,"link","Fictional new agency relationship");
            Assert.Equal(competingBefore,await Competing(options));
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Single(await db.Set<ClientAgencyRelationship>().Where(x=>x.ClientId==PartyDemoSeed.ClientId(4) && x.AgencyId==PartyDemoSeed.SecondAgencyId).ToListAsync());
                Assert.Equal(3,await db.Set<Contact>().CountAsync());Assert.Equal(2,await db.Set<SupportFlag>().CountAsync());Assert.Single(await db.Set<FlagVisibility>().ToListAsync());
                Assert.All(await db.Set<IdempotencyRecord>().Select(x=>x.ResultBody).ToListAsync(),value=>{Assert.DoesNotContain("FICTIONAL-RESTRICTED",value);Assert.DoesNotContain("candidateClientId",value);});
                db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==actorId).ToListAsync());db.Add(new UserRole {UserId=actorId,RoleId=await db.Set<Role>().Where(x=>x.Code=="servicing").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(staff,csrf,first,query,queryKey,tag)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await staff.GetAsync(first+"/decisions")).StatusCode);
            async Task Decide(string path,string outcome,string reason){using var response=await Send(staff,csrf,path,new MatchDecisionWrite(outcome,reason),etag:await Tag(staff,path));Assert.Equal(HttpStatusCode.OK,response.StatusCode);}
        }
        finally {if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static string Path(int index)=>"/api/v1/matches/"+MatchDemoSeed.ReviewId(index);
    private sealed record Totals(int Clients,int Relationships,int Contacts,int Flags,int Grants,int Decisions,int Requests,int Activity,int Receipts,int Audit);
    private static async Task<string> Competing(DbContextOptions<BackOfficeDbContext> options)
    {await using var db=new BackOfficeDbContext(options);return JsonSerializer.Serialize(new {contacts=await db.Set<Contact>().OrderBy(x=>x.Id).ToArrayAsync(),people=await db.Set<Person>().OrderBy(x=>x.Id).ToArrayAsync(),flags=await db.Set<SupportFlag>().OrderBy(x=>x.Id).ToArrayAsync(),grants=await db.Set<FlagVisibility>().OrderBy(x=>x.Id).ToArrayAsync()});}
    private static async Task<Totals> Counts(DbContextOptions<BackOfficeDbContext> options){await using var db=new BackOfficeDbContext(options);return new(await db.Set<ClientAccount>().CountAsync(),await db.Set<ClientAgencyRelationship>().CountAsync(),await db.Set<Contact>().CountAsync(),await db.Set<SupportFlag>().CountAsync(),await db.Set<FlagVisibility>().CountAsync(),await db.Set<MatchDecision>().CountAsync(),await db.Set<MatchInformationRequest>().CountAsync(),await db.Set<ClientActivity>().CountAsync(),await db.Set<IdempotencyRecord>().CountAsync(),await db.Set<AuditEvent>().CountAsync());}
    private sealed class InjectedFailure:Exception;
    private sealed class Factory(DbContextOptions<BackOfficeDbContext> options):IDbContextFactory<BackOfficeDbContext>{public BackOfficeDbContext CreateDbContext()=>new(options);}
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();}
    private static async Task<string> Tag(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return response.Headers.ETag!.ToString();}
    private static async Task<string> Login(HttpClient client,string role,string password)
    {
        var token=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new {email=role+"@cover.example",password})};request.Headers.Add("X-CSRF-Token",token);
        using var response=await client.SendAsync(request);response.EnsureSuccessStatusCode();return (await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,string path,object body,string? key=null,string? etag=null)
    {
        // Omit absent optional DTO fields to match the strict public JSON contract.
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
        var request=new HttpRequestMessage(HttpMethod.Post,path+"/decisions"){Content=JsonContent.Create(body,options:json)};if(csrf is not null)request.Headers.Add("X-CSRF-Token",csrf);request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag is not null)request.Headers.Add("If-Match",etag);return client.SendAsync(request);
    }
}
