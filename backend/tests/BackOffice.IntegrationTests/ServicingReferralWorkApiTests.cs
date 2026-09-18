using System.Net;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingReferralWorkHttp(BackOfficeDbContext db,DecisionFixture f,string password,ServicingCycle cycle,Guid fence,string etag)
    {
        var referral=await db.Set<ServicingReferral>().AsNoTracking().FirstAsync(x=>x.CycleId==cycle.Id);
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
            services.AddScoped(p=>new ServicingReferralService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock))));
        var login=await ServicingRatingLogin(host,password);using var client=login.Client;
        var path=$"/api/v1/drafts/{cycle.DraftId:D}/referrals/{referral.Id:D}";
        using(var read=await client.GetAsync(path))
        {
            var text=await read.Content.ReadAsStringAsync();Assert.True(read.StatusCode==HttpStatusCode.OK,text);Assert.True(read.Headers.CacheControl!.NoStore);Assert.Null(read.Headers.ETag);
            var data=JsonSerializer.Deserialize<JsonElement>(text);Assert.Equal(cycle.DraftId,data.GetProperty("draftId").GetGuid());Assert.Equal(cycle.Id,data.GetProperty("cycleId").GetGuid());
            Assert.Equal(referral.Id,Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());Assert.Equal(JsonValueKind.Null,data.GetProperty("nextCursor").ValueKind);
            if(Environment.GetEnvironmentVariable("COVER_REFERRAL_WORK_RESPONSES_DIRECTORY") is {Length:>0} output)
            {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}.json"),JsonSerializer.Serialize(new {schema="ServicingReferralWork",data}));}
        }
        using(var invalid=await client.GetAsync(path+"?pageSize=1"))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var invalid=await client.GetAsync($"/api/v1/drafts/{cycle.DraftId:D}/referrals/{Guid.NewGuid():D}"))Assert.Equal(HttpStatusCode.NotFound,invalid.StatusCode);
        using(var invalid=await client.GetAsync($"/api/v1/drafts/{Guid.NewGuid():D}/referrals/{referral.Id:D}"))Assert.Equal(HttpStatusCode.NotFound,invalid.StatusCode);
        using(var anonymous=host.CreateClient())
        {using var invalid=await anonymous.GetAsync(path);Assert.Equal(HttpStatusCode.Unauthorized,invalid.StatusCode);}
        await new ServicingRatingService(f.Factory,f.Clock).RateAsync(f.Servicing,cycle.DraftId,cycle.RevisionId,Convert.FromBase64String(etag.Trim('"')),fence,"Refresh fictional referral work",Guid.NewGuid().ToString(),Guid.NewGuid());
        using(var stale=await client.GetAsync(path))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
        using(var revoked=await client.GetAsync(path))Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
    }
}
