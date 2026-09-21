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
        var document=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync();
        var generateRoute="/api/v1/records/"+document.SubjectId+"/documents/generate";
        var generateInput=new{kind="policy-schedule",source=new{kind="policy-version",policyVersionId=request.VersionId},templateVersionId=request.TemplateVersionId,
            visibility="internal",reason="Explicit API regeneration",documentId=document.Id};
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        async Task<HttpResponseMessage> Generate(object input,string? key,bool includeCsrf=true)
        {
            using var message=new HttpRequestMessage(HttpMethod.Post,generateRoute){Content=JsonContent.Create(input)};
            if(includeCsrf)message.Headers.Add("X-CSRF-TOKEN",csrf);
            if(key is not null)message.Headers.Add("Idempotency-Key",key);
            return await client.SendAsync(message);
        }
        Assert.Equal(HttpStatusCode.Forbidden,(await Generate(generateInput,"no-csrf",false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Generate(generateInput,null)).StatusCode);
        var malformed=JsonSerializer.SerializeToNode(generateInput)!;malformed["source"]!["unknown"]=true;
        Assert.Equal(HttpStatusCode.BadRequest,(await Generate(malformed,"document-invalid-field")).StatusCode);
        malformed=JsonSerializer.SerializeToNode(generateInput)!;malformed["kind"]="renewal-invitation";
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Generate(malformed,"document-invalid-kind")).StatusCode);
        using var generated=await Generate(generateInput,"document-api-regenerate");Assert.Equal(HttpStatusCode.Accepted,generated.StatusCode);
        var generatedView=await generated.Content.ReadFromJsonAsync<JsonElement>();var regeneratedId=generatedView.GetProperty("id").GetGuid();
        using var replayed=await Generate(generateInput,"document-api-regenerate");Assert.Equal(HttpStatusCode.Accepted,replayed.StatusCode);
        Assert.Equal(regeneratedId,(await replayed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var regenerateVersion=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==regeneratedId);Assert.Equal(2,regenerateVersion.Number);
        await scope.ServiceProvider.GetRequiredService<DocumentGenerationWorker>().Process(regenerateVersion.WorkId,default);
        Assert.Equal("ready",(await client.GetFromJsonAsync<JsonElement>("/api/v1/document-versions/"+regeneratedId)).GetProperty("state").GetString());
        Assert.Equal(2,await db.Set<DocumentVersion>().CountAsync());
        var documentsRoute="/api/v1/records/"+document.SubjectId+"/documents";
        var documents=await client.GetFromJsonAsync<JsonElement>(documentsRoute+"?pageSize=1");
        Assert.Equal(document.Id,Assert.Single(documents.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(regeneratedId,documents.GetProperty("items")[0].GetProperty("currentVersionId").GetGuid());
        var historyRoute="/api/v1/documents/"+document.Id+"/versions";
        var firstPage=await client.GetFromJsonAsync<JsonElement>(historyRoute+"?pageSize=1");
        Assert.Equal(regeneratedId,Assert.Single(firstPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var cursor=firstPage.GetProperty("nextCursor").GetString()!;
        // Another generation can arrive between pages, even with the same clock timestamp.
        using var concurrentVersion=await Generate(generateInput,"document-between-history-pages");
        Assert.Equal(HttpStatusCode.Accepted,concurrentVersion.StatusCode);
        var secondPage=await client.GetFromJsonAsync<JsonElement>(historyRoute+"?pageSize=1&cursor="+Uri.EscapeDataString(cursor));
        Assert.Equal(created.ResourceId,Assert.Single(secondPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var anotherInput=JsonSerializer.SerializeToNode(generateInput)!.AsObject();anotherInput.Remove("documentId");
        using var another=await Generate(anotherInput,"document-new-logical-head");Assert.Equal(HttpStatusCode.Accepted,another.StatusCode);
        var anotherDocumentId=(await another.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        var headsPage=await client.GetFromJsonAsync<JsonElement>(documentsRoute+"?pageSize=1");
        var headsNext=await client.GetFromJsonAsync<JsonElement>(documentsRoute+"?pageSize=1&cursor="+Uri.EscapeDataString(headsPage.GetProperty("nextCursor").GetString()!));
        var headIds=new[]{Assert.Single(headsPage.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid(),
            Assert.Single(headsNext.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()};
        Assert.Equal(2,headIds.Distinct().Count());Assert.Contains(document.Id,headIds);Assert.Contains(anotherDocumentId,headIds);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(documentsRoute+"?pageSize=1&cursor="+Uri.EscapeDataString(cursor))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(historyRoute+"?unknown=true")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/documents/"+Guid.NewGuid()+"/versions")).StatusCode);
        var fileId=await db.Set<DocumentVersionContent>().Where(x=>x.VersionId==created.ResourceId).Select(x=>x.FileObjectId).SingleAsync();
        await File.WriteAllTextAsync(Path.Combine(root,"files","ready",fileId.ToString("N")+".bin"),"Simulated damaged demo storage");
        using var corrupt=await client.GetAsync(route+"/content");Assert.Equal(HttpStatusCode.ServiceUnavailable,corrupt.StatusCode);
        Assert.DoesNotContain(root,await corrupt.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
        Assert.Equal("quarantined",(await client.GetFromJsonAsync<JsonElement>(route)).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(route+"/preview")).StatusCode);
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
        Assert.Contains((await client.GetAsync(route+"/content")).StatusCode,new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden});
        Assert.Contains((await Generate(generateInput,"document-api-regenerate")).StatusCode,new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden});
        Assert.Contains((await client.GetAsync(historyRoute)).StatusCode,new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden});
    });
}
