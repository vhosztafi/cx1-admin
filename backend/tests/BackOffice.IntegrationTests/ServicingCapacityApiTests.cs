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
    private static async Task VerifyServicingCapacityHttp(BackOfficeDbContext db,DecisionFixture f,string password,ServicingCycle cycle,
        ServicingReferral referral,Guid lease,string etag)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.UseSetting("Cover:ServicingCapacityWorkerEnabled","false")
            .ConfigureServices(services=>{
                services.AddScoped(p=>new ServicingCapacityService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
                services.AddScoped(p=>new ServicingCapacityReadModel(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
            }));
        using var client=host.CreateClient();
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var signed=await client.SendAsync(login);signed.EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var path=$"/api/v1/drafts/{cycle.DraftId:D}/capacity";
        async Task<HttpResponseMessage> Post(string route,object body,string? expected=null,string? key=null,bool includeCsrf=true)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(body)};
            request.Headers.TryAddWithoutValidation("If-Match",expected??etag);request.Headers.Add("X-Edit-Lease",lease.ToString());
            request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());if(includeCsrf)request.Headers.Add("X-CSRF-Token",csrf);
            return await client.SendAsync(request);
        }
        var create=new{cycleId=cycle.Id,referralId=referral.Id,referralEtag="\""+Convert.ToBase64String(referral.RowVersion)+"\"",reason="Create fictional servicing capacity case over HTTP"};
        using(var denied=await Post(path,create,includeCsrf:false)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var invalid=await Post(path,new{create.cycleId,create.referralId,create.referralEtag,create.reason,waiveAuthority=true}))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var createKey=Guid.NewGuid().ToString();using var created=await Post(path,create,key:createKey);
        var text=await created.Content.ReadAsStringAsync();Assert.True(created.StatusCode==HttpStatusCode.Created,text);Assert.True(created.Headers.CacheControl!.NoStore);
        var result=JsonSerializer.Deserialize<JsonElement>(text);var caseId=result.GetProperty("id").GetGuid();
        using(var replay=await Post(path,create,key:createKey)) {Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Equal(text,await replay.Content.ReadAsStringAsync());}
        using var detail=await client.GetAsync(path+"/"+caseId.ToString("D"));detail.EnsureSuccessStatusCode();Assert.True(detail.Headers.CacheControl!.NoStore);
        Assert.Null(detail.Headers.ETag);
        var view=await detail.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(caseId,view.GetProperty("case").GetProperty("id").GetGuid());
        Assert.Equal(caseId,await db.Set<ServicingCapacityCase>().Where(x=>x.DraftId==cycle.DraftId).Select(x=>x.Id).SingleAsync());
        await VerifyServicingCapacityHttpCommands(db,f,client,csrf,cycle,caseId,lease,result.GetProperty("draftEtag").GetString()!);
    }
}
