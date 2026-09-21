using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalDocumentHostedWorkerImportsOriginalWorkAndPreservesFilesAcrossHostRestart()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var requests=await db.Set<PolicyDocumentRequest>().AsNoTracking().ToArrayAsync();Assert.Equal(3,requests.Length);
        var root=Path.GetFullPath(Path.Combine(".local","document-hosted",db.Database.GetDbConnection().Database));
        WebApplicationFactory<Program> Host(bool enabled)=>new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",Path.Combine(root,"files"))
            .UseSetting("Cover:DataProtectionPath",Path.Combine(root,"keys")).UseSetting("Cover:DocumentWorkerEnabled",enabled.ToString())
            .UseSetting("Cover:FileWorkerEnabled","false").ConfigureServices(s=>s.AddSingleton<TimeProvider>(f.Clock)));
        using(var host=Host(true))
        using(var client=host.CreateClient())
        {
            var completed=false;
            for(var poll=0;poll<60&&!completed;poll++)
            {
                completed=await db.Set<OutboxWork>().CountAsync(x=>requests.Select(r=>r.WorkId).Contains(x.Id)&&x.State=="succeeded")==3;
                if(!completed)await Task.Delay(250);
            }
            Assert.True(completed,"Hosted document worker did not consume the original three policy requests.");
        }
        var versions=await db.Set<DocumentVersion>().AsNoTracking().ToArrayAsync();Assert.Equal(3,versions.Length);
        Assert.All(versions,v=>Assert.Contains(requests,r=>r.Id==v.PolicyDocumentRequestId&&r.WorkId==v.WorkId));
        using(var host=Host(false))
        using(var client=host.CreateClient())
        {
            var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
            var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
            using var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})};login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();
            foreach(var version in versions)
            {
                var metadata=await client.GetFromJsonAsync<JsonElement>("/api/v1/document-versions/"+version.Id);Assert.Equal("ready",metadata.GetProperty("state").GetString());
                var bytes=await client.GetByteArrayAsync("/api/v1/document-versions/"+version.Id+"/content");
                Assert.Equal(metadata.GetProperty("sha256").GetString(),Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
            }
        }
        Assert.Equal(3,await db.Set<DocumentVersion>().CountAsync());Assert.Equal(3,await db.Set<DocumentVersionContent>().CountAsync());
        foreach(var request in requests)Assert.Equal(request.PayloadJson,(await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Id==request.Id)).PayloadJson);
    });
}
