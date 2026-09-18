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
    private static async Task VerifyRenewalLapseHttp(BackOfficeDbContext db,DecisionFixture f,string password,Guid termId,string etag,string? replayKey=null,string? expectedBody=null)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder
            .UseSetting("Cover:RenewalLifecycleWorkerEnabled","false").UseSetting("Cover:ServicingDeliveryWorkerEnabled","false")
            .ConfigureServices(services=>services.AddSingleton(p=>new RenewalLifecycleService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock))));
        using var client=host.CreateClient();var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var response=await client.SendAsync(login);response.EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var root=$"/api/v1/terms/{termId:D}";
        async Task Capture(string name,string schema,string content)
        {
            if(Environment.GetEnvironmentVariable("COVER_LAPSE_RESPONSES_DIRECTORY") is {Length:>0} output)
            {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{termId:D}-{name}.json"),JsonSerializer.Serialize(new{schema,data=JsonSerializer.Deserialize<JsonElement>(content)}));}
        }
        using(var read=await client.GetAsync(root+"/renewal-lifecycle"))
        {Assert.Equal(HttpStatusCode.OK,read.StatusCode);Assert.True(read.Headers.CacheControl!.NoStore);Assert.NotNull(read.Headers.ETag);await Capture(replayKey is null?"before":"after","RenewalLifecycleView",await read.Content.ReadAsStringAsync());}
        async Task<HttpResponseMessage> Post(object body,bool antiForgery=true,string? match=null,string? key=null,string suffix="")
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,root+"/lapse"+suffix){Content=JsonContent.Create(body)};
            if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);
            if(match!="missing")request.Headers.TryAddWithoutValidation("If-Match",match??etag);
            if(key!="missing")request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());return await client.SendAsync(request);
        }
        var input=new{reason="Fictional insured declined renewal"};
        if(replayKey is not null)
        {
            using var replay=await Post(input,key:replayKey);var body=await replay.Content.ReadAsStringAsync();Assert.Equal(HttpStatusCode.Created,replay.StatusCode);
            Assert.Equal(expectedBody,body);Assert.True(replay.Headers.CacheControl!.NoStore);Assert.NotNull(replay.Headers.ETag);await Capture("receipt","RenewalLapseReceipt",body);return;
        }
        using(var denied=await Post(input,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post(input,key:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(input,match:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
        using(var denied=await Post(input,match:"W/"+etag))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(input,match:"\"AAAAAAAAAAA=\""))Assert.Equal(HttpStatusCode.PreconditionFailed,denied.StatusCode);
        using(var denied=await Post(input,suffix:"?automatic=true"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(new{reason=input.reason,effectiveAt=f.Clock.GetUtcNow()}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(new{reason="short"}))Assert.Equal(HttpStatusCode.UnprocessableEntity,denied.StatusCode);
        using(var denied=await client.GetAsync(root+"/renewal-lifecycle?forged=true"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var anonymous=host.CreateClient())
        {using var denied=await anonymous.PostAsJsonAsync(root+"/lapse",input);Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
    }
}
