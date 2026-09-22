using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalDocumentBrowserTaskAttachmentsKeepOriginalAuthorityAndVersion()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Kind=="policy-schedule");
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);var renderer=new PolicyDocumentRenderService(f.Factory,new PolicyDocumentRenderer());
        var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","document-task-links",db.Database.GetDbConnection().Database)),[]);
        var documents=new DocumentService(f.Factory,boundary,renderer,f.Clock,new FileService(f.Factory,boundary,store,f.Clock));
        var registered=await documents.RegisterRetainedRequest(f.Underwriter,request.Id,"task-link-original-document",default);
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==registered.ResourceId);
        var document=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==version.DocumentId);
        var tasks=new TaskService(f.Factory,boundary,f.Clock);
        var created=await tasks.Create(f.Underwriter,document.SubjectId,new("servicing","Review original policy evidence","normal",new("unassigned"),null),"task-link-create",default);
        var etag=created.Etag!;
        var pending=await Assert.ThrowsAsync<OperationalAccessException>(()=>tasks.AttachDocument(f.Underwriter,created.ResourceId,etag,"task-link-pending",version.Id,"Attach exact policy file",default));
        Assert.Equal(409,pending.Status);
        await new DocumentGenerationWorker(f.Factory,renderer,store,f.Clock).Process(request.WorkId,default);
        var attached=await tasks.AttachDocument(f.Underwriter,created.ResourceId,etag,"task-link-ready",version.Id,"Attach exact policy file",default);
        Assert.NotEqual(etag,attached.Etag);Assert.True((await tasks.AttachDocument(f.Underwriter,created.ResourceId,etag,"task-link-ready",version.Id,"Attach exact policy file",default)).Replayed);
        var links=JsonSerializer.SerializeToElement(await tasks.ListDocumentAttachments(f.Underwriter,created.ResourceId,default));
        var link=Assert.Single(links.EnumerateArray());var linkId=link.GetProperty("id").GetGuid();
        Assert.Equal(version.Id,link.GetProperty("version").GetProperty("id").GetGuid());Assert.Equal("ready",link.GetProperty("version").GetProperty("state").GetString());
        var originalHash=link.GetProperty("version").GetProperty("sha256").GetString();
        var changed=await documents.Generate(f.Underwriter,document.SubjectId,new(document.Kind,new("policy-version",PolicyVersionId:request.VersionId),request.TemplateVersionId,"internal","Later document remains separate",DocumentId:document.Id),"task-link-later-version",default);
        Assert.NotEqual(version.Id,changed.ResourceId);
        links=JsonSerializer.SerializeToElement(await tasks.ListDocumentAttachments(f.Underwriter,created.ResourceId,default));
        Assert.Equal(version.Id,links[0].GetProperty("version").GetProperty("id").GetGuid());
        var quoteSubject=await tasks.Register(f.Underwriter,new("quote",f.QuoteId),"task-link-other-parent",default);
        var other=await tasks.Create(f.Underwriter,quoteSubject.ResourceId,new("servicing","Other source task","normal",new("unassigned"),null),"task-link-other-create",default);
        var foreign=await Assert.ThrowsAsync<OperationalAccessException>(()=>tasks.AttachDocument(f.Underwriter,other.ResourceId,other.Etag!,"task-link-cross-parent",version.Id,"Do not cross original scope",default));Assert.Equal(404,foreign.Status);
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TaskDocumentAttachment(Id,TaskId,DocumentVersionId,AuthorLabel,Reason,CreatedAt,CreatedBy) VALUES({Guid.NewGuid()},{other.ResourceId},{version.Id},N'Fictional actor',N'Direct cross-parent rejection',{f.Clock.GetUtcNow()},{f.Underwriter.UserId})"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TaskDocumentAttachment SET DocumentVersionId={changed.ResourceId} WHERE Id={linkId}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM TaskDocumentAttachment WHERE Id={linkId}"));
        var duplicate=await Assert.ThrowsAsync<OperationalAccessException>(()=>tasks.AttachDocument(f.Underwriter,created.ResourceId,attached.Etag!,"task-link-duplicate",version.Id,"Duplicate selection",default));Assert.Equal(409,duplicate.Status);
        var stale=await Assert.ThrowsAsync<OperationalAccessException>(()=>tasks.RemoveDocumentAttachment(f.Underwriter,created.ResourceId,etag,"task-link-stale-removal",linkId,"No longer required",default));Assert.Equal(412,stale.Status);
        var removed=await tasks.RemoveDocumentAttachment(f.Underwriter,created.ResourceId,attached.Etag!,"task-link-remove",linkId,"No longer required",default);
        Assert.Empty(await tasks.ListDocumentAttachments(f.Underwriter,created.ResourceId,default));
        Assert.True((await tasks.RemoveDocumentAttachment(f.Underwriter,created.ResourceId,attached.Etag!,"task-link-remove",linkId,"No longer required",default)).Replayed);
        Assert.Equal(originalHash,JsonDocument.Parse((await documents.ReadVersion(f.Underwriter,version.Id,default)).Body).RootElement.GetProperty("sha256").GetString());
        await tasks.AttachDocument(f.Underwriter,created.ResourceId,removed.Etag!,"task-link-reattach",version.Id,"Restore original evidence link",default);
        Assert.Equal(2,await db.Set<TaskDocumentAttachment>().CountAsync(x=>x.TaskId==created.ResourceId));
        Assert.Single(await db.Set<TaskDocumentAttachment>().Where(x=>x.TaskId==created.ResourceId&&x.RemovedAt==null).ToArrayAsync());
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:DataProtectionPath",Path.GetFullPath(Path.Combine(".local","task-attachment-api-keys",db.Database.GetDbConnection().Database)))
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(f.Clock)));
        using var client=host.CreateClient();var route=$"/api/v1/tasks/{created.ResourceId}/attachments";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(route)).StatusCode);
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email,password})})
        {login.Headers.Add("X-CSRF-TOKEN",csrf);(await client.SendAsync(login)).EnsureSuccessStatusCode();}
        using var listResponse=await client.GetAsync(route);listResponse.EnsureSuccessStatusCode();Assert.True(listResponse.Headers.CacheControl!.NoStore);
        var list=await listResponse.Content.ReadFromJsonAsync<JsonElement>();var activeId=Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(route+"?unexpected=true")).StatusCode);
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString();
        var current=(await tasks.Read(f.Underwriter,created.ResourceId,default)).Etag!;
        async Task<HttpResponseMessage> Post(string path,object input,bool security=true,string? versionTag=null)
        {
            using var message=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(input)};
            message.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());if(security)message.Headers.Add("X-CSRF-TOKEN",csrf);
            if(versionTag is not null)message.Headers.Add("If-Match",versionTag);return await client.SendAsync(message);
        }
        var removeRoute=route+$"/{activeId}/remove";
        Assert.Equal(HttpStatusCode.Forbidden,(await Post(removeRoute,new{reason="Remove evidence association"},false,current)).StatusCode);
        Assert.Equal((HttpStatusCode)428,(await Post(removeRoute,new{reason="Remove evidence association"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(removeRoute,new{reason="Remove evidence association",unknown=true},versionTag:current)).StatusCode);
        using var removedApi=await Post(removeRoute,new{reason="Remove evidence association"},versionTag:current);Assert.Equal(HttpStatusCode.OK,removedApi.StatusCode);
        var apiEtag=removedApi.Headers.ETag!.ToString();
        using var addedApi=await Post(route,new{documentVersionId=version.Id,reason="Attach original through API"},versionTag:apiEtag);Assert.Equal(HttpStatusCode.OK,addedApi.StatusCode);
        Assert.Equal(created.ResourceId,(await addedApi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(()=>tasks.ListDocumentAttachments(f.Underwriter,created.ResourceId,default));
        await Assert.ThrowsAsync<OperationalAccessException>(()=>tasks.AttachDocument(f.Underwriter,created.ResourceId,etag,"task-link-ready",version.Id,"Attach exact policy file",default));
        // A rollback must not erase retained attachment/removal history.
        var retainedCount=await db.Set<TaskDocumentAttachment>().CountAsync();
        // Exercise this migration's actual guard directly: later migrations can
        // legitimately reject a full downgrade before it reaches attachments.
        var migrations=db.GetService<IMigrationsAssembly>();
        var migration=migrations.CreateMigration(migrations.Migrations["20260921215507_TaskDocumentAttachments"],db.Database.ProviderName!);
        var guard=Assert.Single(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>());
        var downgrade=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync(guard.Sql));
        Assert.Equal(52000,downgrade.Number);
        Assert.Contains("Cannot remove retained task attachment history",downgrade.Message);
        Assert.Equal(retainedCount,await db.Set<TaskDocumentAttachment>().CountAsync());
    });
}
