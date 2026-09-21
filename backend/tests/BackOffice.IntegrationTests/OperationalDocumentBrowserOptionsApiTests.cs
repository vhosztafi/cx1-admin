using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
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
    public Task RealSqlOperationalDocumentBrowserOptionsApi()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var root=Path.GetFullPath(Path.Combine(".local","document-options-api",db.Database.GetDbConnection().Database));
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",Path.Combine(root,"files"))
            .UseSetting("Cover:DataProtectionPath",Path.Combine(root,"keys")).UseSetting("Cover:FileWorkerEnabled","false")
            .UseSetting("Cover:DocumentWorkerEnabled","false").ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        await using var scope=host.Services.CreateAsyncScope();
        var subject=await scope.ServiceProvider.GetRequiredService<TaskService>().Register(f.Underwriter,new("policy",version.PolicyId),"browser-options-subject",default);
        using var client=host.CreateClient();
        var path=$"/api/v1/records/{subject.ResourceId}/documents/options";
        var query=$"?sourceKind=policy-version&sourceId={version.Id}&pageSize=1";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(path+query)).StatusCode);
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        string? cursor=null;string? firstCursor=null;var count=0;var pages=0;
        do
        {
            using var response=await client.GetAsync(path+query+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor)));
            response.EnsureSuccessStatusCode();Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Equal("nosniff",Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
            var page=await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(version.Id,page.GetProperty("sourceVersionId").GetGuid());
            Assert.False(page.TryGetProperty("nextTemplateId",out _));
            foreach(var item in page.GetProperty("items").EnumerateArray())
            {
                var source=item.GetProperty("source");Assert.Equal(2,source.EnumerateObject().Count());
                Assert.Equal(version.Id,source.GetProperty("policyVersionId").GetGuid());count++;
            }
            cursor=page.TryGetProperty("nextCursor",out var next)?next.GetString():null;
            firstCursor??=cursor;Assert.True(++pages<30);
        }while(cursor is not null);
        Assert.True(count>=3);Assert.NotNull(firstCursor);
        var options=await client.GetFromJsonAsync<JsonElement>(path+$"?sourceKind=policy-version&sourceId={version.Id}&pageSize=100");
        var selected=options.GetProperty("items").EnumerateArray().First(x=>x.GetProperty("kind").GetString()=="policy-schedule");
        var documents=scope.ServiceProvider.GetRequiredService<DocumentService>();
        var generated=await documents.Generate(f.Underwriter,subject.ResourceId,new("policy-schedule",new("policy-version",PolicyVersionId:version.Id),selected.GetProperty("templateVersionId").GetGuid(),"internal","Historical source metadata"),"browser-options-metadata",default);
        var metadata=await client.GetFromJsonAsync<JsonElement>("/api/v1/document-versions/"+generated.ResourceId);
        Assert.Equal("policy-version",metadata.GetProperty("sourceKind").GetString());
        Assert.Equal(version.EffectiveAt,metadata.GetProperty("sourceDate").GetDateTimeOffset());
        Assert.Contains("policy version",metadata.GetProperty("sourceLabel").GetString());
        Assert.Equal(Convert.ToHexStringLower(version.ContentHash),metadata.GetProperty("sourceHash").GetString());
        var heads=await client.GetFromJsonAsync<JsonElement>($"/api/v1/records/{subject.ResourceId}/documents");
        var head=Assert.Single(heads.GetProperty("items").EnumerateArray());
        Assert.Equal(generated.ResourceId,head.GetProperty("currentVersion").GetProperty("id").GetGuid());
        Assert.Equal(metadata.GetProperty("sourceLabel").GetString(),head.GetProperty("currentVersion").GetProperty("sourceLabel").GetString());
        var history=await client.GetFromJsonAsync<JsonElement>($"/api/v1/documents/{head.GetProperty("id").GetGuid()}/versions");
        Assert.Equal(metadata.GetProperty("sourceHash").GetString(),Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("sourceHash").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(path+query+"&unexpected=true")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(path+query+"&quoteTermsVersionId="+Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(path+query.Replace(version.Id.ToString(),Guid.NewGuid().ToString())+"&cursor="+Uri.EscapeDataString(firstCursor))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(path+query.Replace(version.Id.ToString(),Guid.NewGuid().ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(path+"?sourceKind=policy-version&sourceId=bad")).StatusCode);
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
        Assert.Contains((await client.GetAsync(path+query)).StatusCode,new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden});
    });
}
