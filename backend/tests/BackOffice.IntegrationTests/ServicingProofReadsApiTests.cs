using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingProofReadsHttp(BackOfficeDbContext db,DecisionFixture f,string password,ServicingCycle cycle,Guid associationId,Guid referralId,Guid fileId)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{
            services.AddScoped(p=>new ServicingEvidenceService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
            services.AddScoped(p=>new ServicingReferralService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
        }));
        var root=$"/api/v1/drafts/{cycle.DraftId:D}";
        var paths=new[]{"/evidence/requirements","/evidence-files","/evidence?cycleId="+cycle.Id,
            $"/evidence/{associationId:D}/events","/referrals",$"/referrals/{referralId:D}/decisions",$"/evidence-files/{fileId:D}/content"};
        using var anonymous=host.CreateClient();
        foreach(var path in paths) {using var denied=await anonymous.GetAsync(root+path);Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
        var login=await ServicingRatingLogin(host,password);using var client=login.Client;
        foreach(var path in paths)
        {
            using var response=await client.GetAsync(root+path);var body=await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode,body);Assert.True(response.Headers.CacheControl!.NoStore);
            if(path.EndsWith("/content",StringComparison.Ordinal))
            {Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);Assert.Contains("Fictional acknowledgement",body);}
            else
            {
                var json=JsonSerializer.Deserialize<JsonElement>(body);
                Assert.True(json.ValueKind==JsonValueKind.Object);
                if(Environment.GetEnvironmentVariable("COVER_CONTRACT_RESPONSES_DIRECTORY") is {Length:>0} output)
                {
                    var schema=path switch {"/evidence/requirements"=>"ServicingProofRequirements","/evidence-files"=>"ServicingFilePage",
                        "/referrals"=>"ServicingReferralPage",_=>path.Contains("/events",StringComparison.Ordinal)?"ServicingReviewPage":path.Contains("/decisions",StringComparison.Ordinal)?"ServicingDecisionPage":"ServicingAssociationPage"};
                    Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}-{schema}.json"),JsonSerializer.Serialize(new {schema,data=json}));
                }
                if(path=="/referrals") Assert.Equal("named-drivers-only",json.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==referralId).GetProperty("conditions")[0].GetProperty("definition").GetProperty("code").GetString());
            }
        }
        using var first=await client.GetAsync(root+"/evidence-files?pageSize=1");first.EnsureSuccessStatusCode();
        var page=await first.Content.ReadFromJsonAsync<JsonElement>();var cursor=page.GetProperty("nextCursor").GetString()!;
        using var second=await client.GetAsync(root+"/evidence-files?pageSize=1&cursor="+Uri.EscapeDataString(cursor));second.EnsureSuccessStatusCode();
        var next=await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id").GetGuid(),next.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null,next.GetProperty("nextCursor").ValueKind);
        foreach(var query in new[]{"pageSize=51","pageSize=1&pageSize=2","unknown=true","cursor=forged","pageSize=2&cursor="+Uri.EscapeDataString(cursor)})
        {using var denied=await client.GetAsync(root+"/evidence-files?"+query);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        foreach(var path in new[]{"/evidence","/evidence?cycleId=bad","/evidence/requirements?unknown=true"})
        {using var denied=await client.GetAsync(root+path);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        foreach(var path in new[]{"/evidence?cycleId="+Guid.NewGuid(),$"/evidence/{Guid.NewGuid():D}/events",$"/referrals/{Guid.NewGuid():D}/decisions",$"/evidence-files/{Guid.NewGuid():D}/content"})
        {using var denied=await client.GetAsync(root+path);Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);}
        using var wrongScope=await client.GetAsync($"/api/v1/drafts/{Guid.NewGuid():D}/evidence-files");Assert.Equal(HttpStatusCode.NotFound,wrongScope.StatusCode);
    }
}
