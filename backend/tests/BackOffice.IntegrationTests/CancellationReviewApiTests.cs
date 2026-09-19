using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyCancellationHttp(BackOfficeDbContext db, DecisionFixture f, string password, Guid draftId, string etag, Guid lease, string hash)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var response=await client.SendAsync(login);response.EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var root=$"/api/v1/drafts/{draftId:D}";
        foreach(var suffix in new[]{"cancellation-preview","cancellation-evidence"})
        {
            using var read=await client.GetAsync(root+"/"+suffix);Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl!.NoStore);Assert.Equal(etag,read.Headers.ETag!.ToString());
            var data=await read.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(draftId,data.GetProperty("draftId").GetGuid());
            if(suffix=="cancellation-preview")Assert.Equal(JsonValueKind.String,data.GetProperty("amounts").GetProperty("posting").GetProperty("premium").ValueKind);
            using var query=await client.GetAsync(root+"/"+suffix+"?forged=true");Assert.Equal(HttpStatusCode.BadRequest,query.StatusCode);
            using var anonymous=host.CreateClient();using var denied=await anonymous.GetAsync(root+"/"+suffix);Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        }
        async Task<HttpResponseMessage> Post(string suffix,object body,bool antiForgery=true,string? match=null,string? key=null,string? fence=null)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,root+"/"+suffix){Content=JsonContent.Create(body)};
            if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);
            if(match!="missing")request.Headers.TryAddWithoutValidation("If-Match",match??etag);
            if(key!="missing")request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
            if(fence!="missing")request.Headers.Add("X-Edit-Lease",fence??lease.ToString());
            return await client.SendAsync(request);
        }
        foreach(var (suffix,body) in new (string,object)[]{("cancellation-preview",new{previewHash=hash}),
            ("cancellation-approvals",new{previewId=Guid.NewGuid(),previewHash=hash,reason="Fictional complete approval reason"}),
            ($"cancellation-evidence/{Guid.NewGuid():D}/reviews",new{outcome="accepted",reason="Fictional complete evidence reason"})})
        {
            using(var denied=await Post(suffix,body,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using(var denied=await Post(suffix,body,match:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
            using(var denied=await Post(suffix,body,match:"W/"+etag))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
            using(var denied=await Post(suffix,body,key:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
            using(var denied=await Post(suffix,body,fence:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
            using(var denied=await Post(suffix+"?force=true",body))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
            using(var denied=await Post(suffix,new{forged=true}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        }
        using(var denied=await Post("cancellation-preview",new{previewHash=hash},match:"\"AAAAAAAAAAA=\""))Assert.Equal(HttpStatusCode.PreconditionFailed,denied.StatusCode);
        using(var denied=await Post("cancellation-evidence/uploads",new{forged=true},false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post("cancellation-evidence/uploads",new{forged=true}))Assert.Equal(HttpStatusCode.UnsupportedMediaType,denied.StatusCode);
    }
}
