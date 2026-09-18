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
    private static async Task VerifyServicingCurrentAuthority(BackOfficeDbContext db,DecisionFixture f,string password,ServicingCycle cycle,Guid referralId)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{
            services.AddScoped(p=>new ServicingEvidenceService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
            services.AddScoped(p=>new ServicingReferralService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
        }));
        var root=$"/api/v1/drafts/{cycle.DraftId:D}/referrals/{referralId:D}/authority";
        using var client=host.CreateClient();
        using(var anonymous=await client.GetAsync(root))Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var response=await client.SendAsync(login);response.EnsureSuccessStatusCode();}
        async Task<JsonElement> Read(string suffix="")
        {
            using var response=await client.GetAsync(root+suffix);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
            var result=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(referralId,result.GetProperty("referralId").GetGuid());
            Assert.Equal(cycle.Id,result.GetProperty("cycleId").GetGuid());Assert.Equal(cycle.DraftId,result.GetProperty("draftId").GetGuid());return result;
        }
        var before=await Read();Assert.True(before.GetProperty("canDecide").GetBoolean());Assert.NotEmpty(before.GetProperty("items").EnumerateArray());
        foreach(var grant in before.GetProperty("items").EnumerateArray())
        {
            var id=grant.GetProperty("grantId").GetGuid();Assert.True(await db.Set<UserAuthorityGrant>().AnyAsync(x=>x.Id==id && x.UserId==f.Underwriter.UserId && x.RevokedAt==null));
            Assert.All(grant.GetProperty("rows").EnumerateArray(),row=>Assert.StartsWith("driver-age:",row.GetProperty("limit").GetProperty("code").GetString()));
        }
        foreach(var query in new[]{"?pageSize=6","?pageSize=1&pageSize=2","?unknown=true","?cursor=forged"})
        {using var denied=await client.GetAsync(root+query);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        using(var foreign=await client.GetAsync($"/api/v1/drafts/{cycle.DraftId:D}/referrals/{Guid.NewGuid():D}/authority"))Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        // Revoke only this isolated fixture actor's grants, after all write tests.
        // Keep the draft version unchanged: the read must still use live authority.
        await db.Set<UserAuthorityGrant>().Where(x=>x.UserId==f.Underwriter.UserId && x.RevokedAt==null).ExecuteUpdateAsync(s=>s
            .SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow).SetProperty(x=>x.RevokedBy,f.Underwriter.UserId).SetProperty(x=>x.RevocationReason,"Current authority read test"));
        var after=await Read();Assert.Empty(after.GetProperty("items").EnumerateArray());Assert.False(after.GetProperty("canDecide").GetBoolean());
        Assert.Equal(before.GetProperty("draftEtag").GetString(),after.GetProperty("draftEtag").GetString());
        if(Environment.GetEnvironmentVariable("COVER_AUTHORITY_RESPONSES_DIRECTORY") is {Length:>0} output)
        {
            Directory.CreateDirectory(output);
            foreach(var pair in new[]{("current",before),("revoked",after)})await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}-{pair.Item1}.json"),JsonSerializer.Serialize(new{schema="ServicingCurrentAuthorityPage",data=pair.Item2}));
        }
    }
}
