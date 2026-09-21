using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
    public Task RealSqlOperationalDocumentApiRequiresReadyBytesAndReauthorizesDownloads() => WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Kind=="policy-schedule");
        var root=Path.GetFullPath(Path.Combine(".local","document-api",db.Database.GetDbConnection().Database));
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",Path.Combine(root,"files"))
            .UseSetting("Cover:DataProtectionPath",Path.Combine(root,"keys")).UseSetting("Cover:FileWorkerEnabled","false")
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        await using var scope=host.Services.CreateAsyncScope();
        var service=scope.ServiceProvider.GetRequiredService<DocumentService>();
        var created=await service.RegisterRetainedRequest(f.Underwriter,request.Id,"document-api-register",default);
        using var client=host.CreateClient();
        var route="/api/v1/document-versions/"+created.ResourceId;
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(route)).StatusCode);
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        Assert.Equal("pending",(await client.GetFromJsonAsync<JsonElement>(route)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(route+"/content")).StatusCode);
        await scope.ServiceProvider.GetRequiredService<DocumentGenerationWorker>().Process(request.WorkId,default);
        var metadata=await client.GetFromJsonAsync<JsonElement>(route);Assert.Equal("ready",metadata.GetProperty("state").GetString());
        using var downloaded=await client.GetAsync(route+"/content");downloaded.EnsureSuccessStatusCode();
        var bytes=await downloaded.Content.ReadAsByteArrayAsync();Assert.Equal(metadata.GetProperty("sha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("attachment",downloaded.Content.Headers.ContentDisposition!.DispositionType);Assert.True(downloaded.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff",Assert.Single(downloaded.Headers.GetValues("X-Content-Type-Options")));
        using var preview=await client.GetAsync(route+"/preview");preview.EnsureSuccessStatusCode();
        Assert.Equal("inline",preview.Content.Headers.ContentDisposition!.DispositionType);Assert.Equal(bytes,await preview.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/document-versions/"+Guid.NewGuid())).StatusCode);
        var fileId=await db.Set<DocumentVersionContent>().Where(x=>x.VersionId==created.ResourceId).Select(x=>x.FileObjectId).SingleAsync();
        await File.WriteAllTextAsync(Path.Combine(root,"files","ready",fileId.ToString("N")+".bin"),"Simulated damaged demo storage");
        using var corrupt=await client.GetAsync(route+"/content");Assert.Equal(HttpStatusCode.ServiceUnavailable,corrupt.StatusCode);
        Assert.DoesNotContain(root,await corrupt.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal("quarantined",(await client.GetFromJsonAsync<JsonElement>(route)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(route+"/preview")).StatusCode);
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
        Assert.Contains((await client.GetAsync(route+"/content")).StatusCode,new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden});
    });
}
