using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlRenewalPreparationApiRequiresStrictOwnedCommandsAndRetainsEvidenceBytes(string product)
    {
        await WithDatabase(async(db,password)=>
        {
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var drafts=new ServicingDraftService(f.Factory,f.Clock);
            static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
            var created=await drafts.CreateAsync(f.Underwriter,issued.TermId,Version((await drafts.ListAsync(f.Underwriter,issued.TermId)).Etag),
                new("renewal",issued.Id,JsonSerializer.SerializeToElement(new{localDate="2027-10-01",localTime="01:00",timeZone="Europe/London"}),"Fictional HTTP renewal preparation"),Guid.NewGuid().ToString(),Guid.NewGuid());
            var leased=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,Version(created.Etag!),"acquire",null,null,Guid.NewGuid().ToString(),Guid.NewGuid());
            using var leaseBody=JsonDocument.Parse(leased.Body);var fence=leaseBody.RootElement.GetProperty("lease").GetProperty("leaseToken").GetString()!;
            using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
            {
                services.AddScoped(p=>new RenewalPreparationService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
                services.AddScoped(p=>new ServicingDraftService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
            }));
            using var client=host.CreateClient();var previewRoute=$"/api/v1/terms/{issued.TermId:D}/renewal-preview";
            using(var denied=await client.GetAsync(previewRoute))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
            var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
            {login.Headers.Add("X-CSRF-Token",csrf);using var response=await client.SendAsync(login);response.EnsureSuccessStatusCode();}
            csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
            var responseSequence=0;
            async Task Capture(string schema,JsonElement data)
            {
                if(Environment.GetEnvironmentVariable("COVER_COMMAND_RESPONSES_DIRECTORY") is not {Length:>0} output)return;
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output,$"{issued.TermId:D}-{++responseSequence}.json"),JsonSerializer.Serialize(new{schema,data}));
            }
            using(var preview=await client.GetAsync(previewRoute)){preview.EnsureSuccessStatusCode();Assert.True(preview.Headers.CacheControl!.NoStore);await Capture("RenewalPreparationPreview",await preview.Content.ReadFromJsonAsync<JsonElement>());}
            using(var bad=await client.GetAsync(previewRoute+"?premium=1"))Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
            var root=$"/api/v1/drafts/{created.ResourceId:D}/renewal";var etag=leased.Etag!;
            async Task<HttpResponseMessage> Send(HttpMethod method,string suffix,object body,string? key=null,string? tag=null,bool antiForgery=true)
            {
                using var request=new HttpRequestMessage(method,root+suffix){Content=JsonContent.Create(body)};
                if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);
                request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());request.Headers.Add("X-Edit-Lease",fence);
                if(tag!="missing")request.Headers.TryAddWithoutValidation("If-Match",tag??etag);
                return await client.SendAsync(request);
            }
            async Task<JsonElement> Success(HttpResponseMessage response)
            {
                var body=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,body);Assert.True(response.Headers.CacheControl!.NoStore);
                etag=response.Headers.ETag!.Tag;var data=JsonSerializer.Deserialize<JsonElement>(body);await Capture("RenewalPreparationReceipt",data);return data;
            }
            foreach(var (method,path) in new[]{(HttpMethod.Post,"/preparation"),(HttpMethod.Put,"/experience"),(HttpMethod.Post,"/experience/uploads"),(HttpMethod.Post,$"/experience/{Guid.NewGuid():D}/reviews")})
            {using var denied=await Send(method,path,new{},antiForgery:false);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
            using(var denied=await Send(HttpMethod.Post,"/preparation",new{termMonths=12},tag:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
            using(var denied=await Send(HttpMethod.Post,"/preparation",new{termMonths=12,premium="1.00"}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
            var prepareKey=Guid.NewGuid().ToString();var prepareEtag=etag;
            using(var prepared=await Send(HttpMethod.Post,"/preparation",new{termMonths=12},prepareKey)){await Success(prepared);}
            using(var replay=await Send(HttpMethod.Post,"/preparation",new{termMonths=12},prepareKey,prepareEtag))replay.EnsureSuccessStatusCode();
            var bytes=Encoding.UTF8.GetBytes("FICTIONAL HTTP supplied renewal claims evidence.");
            using var form=new MultipartFormDataContent();var file=new ByteArrayContent(bytes);file.Headers.ContentType=new("text/plain");
            form.Add(file,"file","claims.txt");form.Add(new StringContent("claims.txt"),"fileName");form.Add(new StringContent("text/plain"),"contentType");
            using var upload=new HttpRequestMessage(HttpMethod.Post,root+"/experience/uploads"){Content=form};
            upload.Headers.Add("X-CSRF-Token",csrf);upload.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());upload.Headers.TryAddWithoutValidation("If-Match",etag);upload.Headers.Add("X-Edit-Lease",fence);
            using var uploaded=await client.SendAsync(upload);var association=(await Success(uploaded)).GetProperty("resourceId").GetGuid();
            var facts=new{observationStartsOn="2025-09-16",observationEndsOn="2026-09-16",claimCount=1,paid="500.01",outstanding="0.00",earnedPremium="1000.00",sourceCode="agency",sourceReference="Fictional HTTP statement",evidenceAssociationId=association};
            using(var bad=await Send(HttpMethod.Put,"/experience",new{facts.observationStartsOn,facts.observationEndsOn,facts.claimCount,paid=500.01m,facts.outstanding,facts.earnedPremium,facts.sourceCode,facts.sourceReference,facts.evidenceAssociationId}))Assert.Equal(HttpStatusCode.UnprocessableEntity,bad.StatusCode);
            Guid experience;
            using(var saved=await Send(HttpMethod.Put,"/experience",facts))experience=(await Success(saved)).GetProperty("resourceId").GetGuid();
            var reviewKey=Guid.NewGuid().ToString();var reviewEtag=etag;var decision=new{outcome="accepted",reason="Checked fictional evidence and exact supplied amounts"};
            using(var review=await Send(HttpMethod.Post,$"/experience/{experience:D}/reviews",decision,reviewKey))await Success(review);
            using(var replay=await Send(HttpMethod.Post,$"/experience/{experience:D}/reviews",decision,reviewKey,reviewEtag))replay.EnsureSuccessStatusCode();
            using var read=await client.GetAsync(root+"/experience");read.EnsureSuccessStatusCode();Assert.True(read.Headers.CacheControl!.NoStore);
            var result=await read.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("500.01",result.GetProperty("experience").GetProperty("paid").GetString());
            await Capture("RenewalExperienceView",result);
            Assert.Equal("accepted",result.GetProperty("review").GetProperty("outcome").GetString());
            var fileId=result.GetProperty("evidenceFileId").GetGuid();
            Assert.Equal(bytes,await client.GetByteArrayAsync($"/api/v1/drafts/{created.ResourceId:D}/evidence-files/{fileId:D}/content"));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional HTTP authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            using(var denied=await Send(HttpMethod.Post,$"/experience/{experience:D}/reviews",decision,reviewKey,reviewEtag))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        });
    }
}
