using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class AgencyTermsApiTests
{
    [Fact]
    public async Task RealSqlTermsApiPublishesIndependentlyAndResolvesCurrentProductsByLondonDate()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var password="Demo!"+Guid.NewGuid().ToString("N")+"a1";var clock=new MovingClock();var zone=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),zone).DateTime);var date=today.AddDays(1);
        var id=PartyDemoSeed.FirstAgencyId;var path="/api/v1/agencies/"+id;Guid productId,actorId,initialId;
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,password);
                actorId=await db.Set<StaffUser>().Where(x=>x.Email=="agency-admin@cover.example").Select(x=>x.Id).SingleAsync();var reviewerId=await db.Set<StaffUser>().Where(x=>x.Email=="agency-reviewer@cover.example").Select(x=>x.Id).SingleAsync();productId=await db.Set<ProductVersion>().Select(x=>x.Id).FirstAsync();
                db.Add(new AgencyDraftProduct{AgencyId=id,ProductVersionId=productId,EffectiveFrom=today.AddDays(-1),BrokerCommissionBasisPoints=9999});await db.SaveChangesAsync();
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);agency.State="active";await db.SaveChangesAsync();
                // Establish immutable initial provenance to isolate subsequent commercial HTTP commands.
                var activation=new AgencyStateRequest{AgencyId=id,BaseVersion=agency.RowVersion,ProposedInputFingerprint=new string('a',64),RequestedBy=actorId,CreatedBy=actorId,RequestReason="Fictional initial terms fixture"};db.Add(activation);await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AgencyStateRequest SET State=N'applied',DecisionBy={reviewerId},DecisionReason=N'Fixture independent decision',DecidedAt={DateTimeOffset.UtcNow} WHERE Id={activation.Id}");
                var initial=Input(today.AddDays(-1),productId,1250);initial.Remove("reason");var terms=new AgencyTermsVersion{AgencyId=id,Version=1,EffectiveFrom=today.AddDays(-1),ApprovedStateRequestId=activation.Id,Snapshot=initial.ToJsonString(),CreatedBy=reviewerId};initialId=terms.Id;db.Add(terms);await db.SaveChangesAsync();
            }
            using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",connection.ConnectionString).UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","terms-api-keys",owned))).ConfigureServices(services=>services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
            using var anonymous=host.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(path+"/terms")).StatusCode);
            using var requester=host.CreateClient();var csrf=await Login(requester,"agency-admin",password);
            using var reviewer=host.CreateClient();var reviewerCsrf=await Login(reviewer,"agency-reviewer",password);
            using var other=host.CreateClient();var otherCsrf=await Login(other,"system-admin",password);
            using var reader=host.CreateClient();var readerCsrf=await Login(reader,"underwriter",password);
            Assert.Equal(HttpStatusCode.OK,(await reader.GetAsync(path+"/terms")).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await reader.GetAsync(path+"/terms-requests")).StatusCode);
            var products=(await Read(requester,path+"/products")).GetProperty("items");Assert.Single(products.EnumerateArray());Assert.Equal(1250,products[0].GetProperty("brokerCommissionBasisPoints").GetInt32());Assert.Equal(initialId,products[0].GetProperty("termsVersionId").GetGuid());
            var basis=await Tag(requester,path+"/terms");var input=Input(date,productId,1750);var requestPath=path+"/terms-requests";
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(reader,readerCsrf,requestPath,input,basis)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await Send(requester,null,requestPath,input,basis)).StatusCode);
            Assert.Equal((HttpStatusCode)428,(await Send(requester,csrf,requestPath,input,null)).StatusCode);
            var invalid=input.DeepClone().AsObject();invalid["approvedBy"]=actorId.ToString();Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(requester,csrf,requestPath,invalid,basis)).StatusCode);
            invalid=input.DeepClone().AsObject();invalid["products"]![0]!["brokerCommissionBasisPoints"]=10001;Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(requester,csrf,requestPath,invalid,basis)).StatusCode);
            var key=Guid.NewGuid().ToString("N");using var proposal=await Send(requester,csrf,requestPath,input,basis,key);Assert.Equal(HttpStatusCode.Accepted,proposal.StatusCode);
            var receipt=await proposal.Content.ReadAsStringAsync();using var parsed=JsonDocument.Parse(receipt);Assert.Single(parsed.RootElement.EnumerateObject());var detail=proposal.Headers.Location!.ToString();var proposalTag=proposal.Headers.ETag!.ToString();Assert.Equal(basis,await Tag(requester,path+"/terms"));
            using var originalReplay=await Send(requester,csrf,requestPath,input,basis,key);Assert.Equal(receipt,await originalReplay.Content.ReadAsStringAsync());Assert.Equal(proposalTag,originalReplay.Headers.ETag!.ToString());
            var pending=await Read(requester,detail);Assert.Equal("pending",pending.GetProperty("state").GetString());Assert.Equal("99999999999999999.99",pending.GetProperty("creditLimit").GetString());Assert.Equal(proposalTag,pending.GetProperty("etag").GetString());Assert.True(pending.TryGetProperty("requestedByLabel",out _));Assert.Equal(HttpStatusCode.Forbidden,(await reader.GetAsync(detail)).StatusCode);
            var decision=new{outcome="approve",reason="Independent commercial review"};
            Assert.Equal(HttpStatusCode.Forbidden,(await Send(requester,csrf,detail+"/decision",decision,proposalTag)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(reviewer,reviewerCsrf,detail+"/decision",new{outcome="publish",reason="invalid"},proposalTag)).StatusCode);
            var first=Guid.NewGuid().ToString("N");var second=Guid.NewGuid().ToString("N");var race=await Task.WhenAll(Send(reviewer,reviewerCsrf,detail+"/decision",decision,proposalTag,first),Send(other,otherCsrf,detail+"/decision",decision,proposalTag,second));
            Assert.Single(race,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(race,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);var winnerIndex=Array.FindIndex(race,x=>x.StatusCode==HttpStatusCode.OK);var winner=winnerIndex==0?reviewer:other;var winnerCsrf=winnerIndex==0?reviewerCsrf:otherCsrf;var winnerKey=winnerIndex==0?first:second;
            using var replay=await Send(winner,winnerCsrf,detail+"/decision",decision,proposalTag,winnerKey);Assert.Equal(await race[winnerIndex].Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());foreach(var response in race)response.Dispose();
            var decided=await Read(requester,detail);Assert.Equal("applied",decided.GetProperty("state").GetString());Assert.True(decided.TryGetProperty("decisionByLabel",out _));
            var versions=await Read(requester,path+"/terms");var rows=versions.GetProperty("items");Assert.Equal(2,versions.GetProperty("totalCount").GetInt32());Assert.Equal("scheduled",rows[0].GetProperty("status").GetString());Assert.Equal("terms",rows[0].GetProperty("approvedRequestKind").GetString());Assert.Equal("current",rows[1].GetProperty("status").GetString());Assert.Equal(date.ToString("yyyy-MM-dd"),rows[1].GetProperty("effectiveTo").GetString());
            Assert.Equal(1250,(await Read(requester,path+"/products")).GetProperty("items")[0].GetProperty("brokerCommissionBasisPoints").GetInt32());
            var page=await Read(requester,path+"/terms?pageSize=1");var cursor=Uri.EscapeDataString(page.GetProperty("nextCursor").GetString()!);Assert.Single((await Read(requester,path+"/terms?pageSize=1&cursor="+cursor)).GetProperty("items").EnumerateArray());Assert.Equal(HttpStatusCode.BadRequest,(await requester.GetAsync("/api/v1/agencies/"+PartyDemoSeed.SecondAgencyId+"/terms?pageSize=1&cursor="+cursor)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await requester.GetAsync(path+"/products?asOf=2030-01-01")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await requester.GetAsync("/api/v1/agency-terms-requests/"+Guid.NewGuid())).StatusCode);
            Assert.Single((await Read(requester,requestPath)).GetProperty("items").EnumerateArray());Assert.Contains((await Read(requester,path+"/activity")).GetProperty("items").EnumerateArray(),x=>x.GetProperty("summary").GetString()=="Agreed terms published after independent review.");
            // Cross the exact London midnight and authenticate a fresh cookie at that time.
            var midnight=TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified),zone);clock.Offset=new DateTimeOffset(midnight).AddMinutes(1)-DateTimeOffset.UtcNow;
            using var later=host.CreateClient();await Login(later,"agency-admin",password);
            var effective=(await Read(later,path+"/terms")).GetProperty("items");Assert.Equal("current",effective[0].GetProperty("status").GetString());Assert.Equal("historical",effective[1].GetProperty("status").GetString());Assert.False(effective[0].TryGetProperty("effectiveTo",out _));
            var current=(await Read(later,path+"/products")).GetProperty("items")[0];Assert.Equal(1750,current.GetProperty("brokerCommissionBasisPoints").GetInt32());Assert.Equal(effective[0].GetProperty("id").GetGuid(),current.GetProperty("termsVersionId").GetGuid());
            await using(var db=new BackOfficeDbContext(options)){Assert.Equal(9999,await db.Set<AgencyDraftProduct>().Where(x=>x.AgencyId==id).Select(x=>x.BrokerCommissionBasisPoints).SingleAsync());Assert.Equal(2,await db.Set<AgencyTermsVersion>().CountAsync(x=>x.AgencyId==id));var who=decided.GetProperty("decisionBy").GetGuid();db.RemoveRange(await db.Set<UserRole>().Where(x=>x.UserId==who).ToListAsync());db.Add(new UserRole{UserId=who,RoleId=await db.Set<Role>().Where(x=>x.Code=="underwriter").Select(x=>x.Id).SingleAsync()});await db.SaveChangesAsync();}
            clock.Offset=TimeSpan.Zero;Assert.Equal(HttpStatusCode.Forbidden,(await Send(winner,winnerCsrf,detail+"/decision",decision,proposalTag,winnerKey)).StatusCode);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
    }
    private static JsonObject Input(DateOnly date,Guid product,int bps)=>JsonNode.Parse(JsonSerializer.Serialize(new{effectiveFrom=date.ToString("yyyy-MM-dd"),reason="Fictional complete commercial proposal",commercialTerms=new{effectiveFrom=date.ToString("yyyy-MM-dd"),commissionBasis="per-product",feeSharing="none",volumeCommitmentMode="none",minimumPremiumOverrideMode="none",referralRouting="standard-internal-underwriting"},settlement=new{statementCycle="monthly",method="bank-transfer",premiumCollection="agency",commissionSettlement="net-remittance"},paymentTermsDays=30,creditLimit="99999999999999999.99",products=new[]{new{productVersionId=product,effectiveFrom=date.ToString("yyyy-MM-dd"),brokerCommissionBasisPoints=bps}}}))!.AsObject();
    private sealed class MovingClock:TimeProvider{public TimeSpan Offset{get;set;}public override DateTimeOffset GetUtcNow()=>DateTimeOffset.UtcNow+Offset;}
    private static async Task<JsonElement> Read(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return await response.Content.ReadFromJsonAsync<JsonElement>();}
    private static async Task<string> Tag(HttpClient client,string path){using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();return response.Headers.ETag!.ToString();}
    private static async Task<string> Login(HttpClient client,string role,string password){var token=(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email=role+"@cover.example",password})};request.Headers.Add("X-CSRF-Token",token);using var response=await client.SendAsync(request);response.EnsureSuccessStatusCode();return(await Read(client,"/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;}
    private static Task<HttpResponseMessage> Send(HttpClient client,string? csrf,string path,object body,string? etag,string? key=null){var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};if(csrf!=null)request.Headers.Add("X-CSRF-Token",csrf);request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString("N"));if(etag!=null)request.Headers.Add("If-Match",etag);return client.SendAsync(request);}
}
