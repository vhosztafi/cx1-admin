using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingSubmissionHttp(BackOfficeDbContext db,DecisionFixture f,string password,
        ServicingCycle cycle,ServicingCycle older,Guid fence,string etag)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
            services.AddScoped(p=>new ServicingSubmissionService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock))));
        var login=await ServicingRatingLogin(host,password);using var client=login.Client;var csrf=login.Csrf;
        var root=$"/api/v1/drafts/{cycle.DraftId:D}";var serial=0;
        var body=new {cycleId=cycle.Id,revisionId=cycle.RevisionId,reason="Submit fictional HTTP servicing risk"};
        async Task<HttpResponseMessage> Post(object input,string? key=null,string? version=null,bool antiForgery=true,string? lease=null,string suffix="/submit")
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,root+suffix){Content=input is string raw?new StringContent(raw,Encoding.UTF8,"application/json"):JsonContent.Create(input)};
            if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);
            if(key!="missing")request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
            if(version!="missing")request.Headers.TryAddWithoutValidation("If-Match",version??etag);
            if(lease!="missing")request.Headers.Add("X-Edit-Lease",lease??fence.ToString());
            return await client.SendAsync(request);
        }
        async Task<JsonElement> Capture(HttpResponseMessage response,HttpStatusCode status,string schema)
        {
            var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==status,text);
            Assert.True(response.Headers.CacheControl!.NoStore);var data=JsonSerializer.Deserialize<JsonElement>(text);
            if(Environment.GetEnvironmentVariable("COVER_SUBMISSION_RESPONSES_DIRECTORY") is {Length:>0} output)
            {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}-{++serial}.json"),JsonSerializer.Serialize(new {schema,data}));}
            return data;
        }
        using(var empty=await client.GetAsync(root+"/submissions"))
        {var value=await Capture(empty,HttpStatusCode.OK,"ServicingSubmissionPage");Assert.Equal(0,value.GetProperty("items").GetArrayLength());}
        using(var anonymous=host.CreateClient())
        {using var denied=await anonymous.GetAsync(root+"/submissions");Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
        foreach(var query in new[]{"?unknown=1","?pageSize=51","?pageSize=0","?pageSize=1&pageSize=2","?cursor=forged"})
        {using var denied=await client.GetAsync(root+"/submissions"+query);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        using(var denied=await Post(body,antiForgery:false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post(body,key:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(body,version:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
        using(var denied=await Post(body,version:"W/"+etag))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(body,lease:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(body,lease:Guid.NewGuid().ToString()))Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using(var denied=await Post(body,suffix:"/submit?unknown=1"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(new{body.cycleId,body.revisionId,body.reason,approved=true}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(new{cycleId=Guid.NewGuid(),body.revisionId,body.reason}))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        using(var denied=await Post(new{cycleId=older.Id,body.revisionId,body.reason}))Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using(var denied=await Post(body,version:"\"AAAAAAAAAAA=\""))Assert.Equal(HttpStatusCode.PreconditionFailed,denied.StatusCode);
        var key=Guid.NewGuid().ToString();var original=etag;
        using(var response=await Post(body,key))
        {
            var receipt=await Capture(response,HttpStatusCode.Created,"ServicingSubmissionReceipt");etag=response.Headers.ETag!.Tag;
            Assert.Equal(etag,receipt.GetProperty("draftEtag").GetString());Assert.Null(response.Headers.Location);
            using var replay=await Post(body,key,original);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);
            Assert.Equal(await response.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        }
        using(var duplicate=await Post(body))Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
        using(var read=await client.GetAsync(root+"/submissions"))
        {var value=await Capture(read,HttpStatusCode.OK,"ServicingSubmissionPage");Assert.True(value.GetProperty("current").GetProperty("applicable").GetBoolean());}
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        var rerated=await new ServicingRatingService(f.Factory,f.Clock).RateAsync(f.Servicing,cycle.DraftId,cycle.RevisionId,Version(etag),fence,"Refresh HTTP submitted servicing",Guid.NewGuid().ToString(),Guid.NewGuid());
        var next=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==rerated.ResourceId);
        var worker=new ServicingRatingWorker(f.Factory,f.Clock);var job=Assert.IsType<JobLease>(await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync("servicing-rating",next.WorkId));
        Assert.True(await worker.ApplyAsync(job,await worker.ExecuteProviderAsync(job)));
        etag=(await new ServicingRatingReadModel(f.Factory,f.Clock).ReadAsync(f.Servicing,cycle.DraftId)).DraftEtag;
        f.Clock.Current=f.Clock.Current.AddSeconds(1);
        using(var second=await Post(new{cycleId=next.Id,body.revisionId,body.reason}))
        {await Capture(second,HttpStatusCode.Created,"ServicingSubmissionReceipt");etag=second.Headers.ETag!.Tag;}
        string cursor;
        using(var read=await client.GetAsync(root+"/submissions?pageSize=1"))
        {var page=await Capture(read,HttpStatusCode.OK,"ServicingSubmissionPage");cursor=page.GetProperty("nextCursor").GetString()!;Assert.True(page.GetProperty("items")[0].GetProperty("applicable").GetBoolean());}
        using(var read=await client.GetAsync(root+"/submissions?pageSize=1&cursor="+Uri.EscapeDataString(cursor)))
        {var page=await Capture(read,HttpStatusCode.OK,"ServicingSubmissionPage");Assert.False(page.GetProperty("items")[0].GetProperty("applicable").GetBoolean());Assert.True(page.GetProperty("current").GetProperty("applicable").GetBoolean());}
        using(var stale=await Post(body,key,original))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        await new ServicingDraftService(f.Factory,f.Clock).LeaseAsync(f.Servicing,cycle.DraftId,Version(etag),"renew",fence,null,Guid.NewGuid().ToString(),Guid.NewGuid());
        using(var stale=await client.GetAsync(root+"/submissions?pageSize=1&cursor="+Uri.EscapeDataString(cursor)))Assert.Equal(HttpStatusCode.BadRequest,stale.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
        // The identity snapshot rejects the session after all roles are removed.
        using(var revoked=await Post(body,key,original))Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        using(var revoked=await client.GetAsync(root+"/submissions"))Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
        Assert.Equal(2,await db.Set<ServicingUnderwritingSubmission>().CountAsync(x=>x.DraftId==cycle.DraftId));
    }
}
