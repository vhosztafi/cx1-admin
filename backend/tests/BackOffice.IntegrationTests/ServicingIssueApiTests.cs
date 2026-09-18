using System.Net;
using System.Net.Http.Json;
using System.Text;
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
    private static async Task VerifyServicingIssueHttp(BackOfficeDbContext db,DecisionFixture f,string password,Guid draftId,
        byte[] version,Guid fence,ServicingIssueInput input,string? replayKey=null,string? expectedBody=null)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder
            .UseSetting("Cover:ServicingDeliveryWorkerEnabled","false")
            .ConfigureServices(services=>services.AddScoped(p=>new ServicingIssueService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock))));
        using var client=host.CreateClient();
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var response=await client.SendAsync(login);response.EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var root=$"/api/v1/drafts/{draftId:D}/issue";var etag="\""+Convert.ToBase64String(version)+"\"";
        async Task<HttpResponseMessage> Post(object body,bool antiForgery=true,string? match=null,string? lease=null,string? key=null,string suffix="")
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,root+suffix){Content=body is string raw?new StringContent(raw,Encoding.UTF8,"application/json"):JsonContent.Create(body)};
            if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);
            if(match!="missing")request.Headers.TryAddWithoutValidation("If-Match",match??etag);
            if(lease!="missing")request.Headers.Add("X-Edit-Lease",lease??fence.ToString());
            if(key!="missing")request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
            return await client.SendAsync(request);
        }
        if(replayKey is not null)
        {
            using var response=await Post(input,key:replayKey);
            var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==HttpStatusCode.Created,text);
            Assert.Equal(expectedBody,text);Assert.True(response.Headers.CacheControl!.NoStore);Assert.NotNull(response.Headers.ETag);
            if(Environment.GetEnvironmentVariable("COVER_ISSUE_RESPONSES_DIRECTORY") is {Length:>0} output)
            {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{draftId:D}.json"),JsonSerializer.Serialize(new{schema="ServicingIssueResult",data=JsonSerializer.Deserialize<JsonElement>(text)}));}
            return;
        }
        using(var denied=await Post(input,antiForgery:false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post(input,key:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(input,match:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
        using(var denied=await Post(input,match:"W/"+etag))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(input,lease:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post(input,lease:Guid.NewGuid().ToString()))Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using(var denied=await Post(input,suffix:"?forged=true"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var json=JsonSerializer.Serialize(input,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using(var denied=await Post(json[..^1]+",\"premium\":\"0.00\"}"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var anonymous=host.CreateClient())
        {using var denied=await anonymous.PostAsJsonAsync(root,input);Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
    }
}
