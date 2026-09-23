using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalCommunicationAttachmentsKeepAudienceAndOriginalScope()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var request=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Kind=="policy-schedule");
        var boundary=new SqlCommandBoundary(f.Factory,f.Clock);var renderer=new PolicyDocumentRenderService(f.Factory,new PolicyDocumentRenderer());
        var store=new OperationalFileStore(Path.GetFullPath(Path.Combine(".local","communication-attachments",db.Database.GetDbConnection().Database)),[]);
        var documents=new DocumentService(f.Factory,boundary,renderer,f.Clock,new FileService(f.Factory,boundary,store,f.Clock));
        var registered=await documents.RegisterRetainedRequest(f.Underwriter,request.Id,"communication-original-document",default);
        var version=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==registered.ResourceId);
        var document=await db.Set<OperationalDocument>().AsNoTracking().SingleAsync(x=>x.Id==version.DocumentId);
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        var threads=new ThreadService(f.Factory,boundary,f.Clock);
        var internalThread=await threads.Create(f.Underwriter,document.SubjectId,new("internal","Internal evidence review"),"internal-thread",default);
        var pending=new MessageDraftWrite("Pending evidence",[],[version.Id]);
        Assert.Equal(409,(await Assert.ThrowsAsync<OperationalAccessException>(()=>threads.CreateDraft(f.Underwriter,internalThread.ResourceId,pending,"pending-draft",default))).Status);
        await new DocumentGenerationWorker(f.Factory,renderer,store,f.Clock).Process(request.WorkId,default);
        var internalMessage=await threads.CreateDraft(f.Underwriter,internalThread.ResourceId,pending,"ready-internal-draft",default);
        var agencyThread=await threads.Create(f.Underwriter,document.SubjectId,new("agency","Agency evidence review",policy.RelationshipId),"agency-thread",default);
        Assert.Equal(404,(await Assert.ThrowsAsync<OperationalAccessException>(()=>threads.CreateDraft(f.Underwriter,agencyThread.ResourceId,pending,"internal-file-to-agency",default))).Status);
        var selected=await documents.Generate(f.Underwriter,document.SubjectId,new("policy-schedule",new("policy-version",PolicyVersionId:request.VersionId),request.TemplateVersionId,"agency","Fictional agency schedule",RelationshipId:policy.RelationshipId),"agency-document",default);
        var agencyVersion=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==selected.ResourceId);
        await new DocumentGenerationWorker(f.Factory,renderer,store,f.Clock).Process(agencyVersion.WorkId,default);
        var write=new MessageDraftWrite("Exact agency version",[],[agencyVersion.Id]);
        var message=await threads.CreateDraft(f.Underwriter,agencyThread.ResourceId,write,"agency-message",default);
        Assert.True((await threads.CreateDraft(f.Underwriter,agencyThread.ResourceId,write,"agency-message",default)).Replayed);
        var options=await threads.AttachmentOptions(f.Underwriter,agencyThread.ResourceId,null,25,f.Clock.GetUtcNow(),default);
        var option=Assert.Single(JsonSerializer.SerializeToElement(options.Items).EnumerateArray());Assert.Equal(agencyVersion.Id,option.GetProperty("id").GetGuid());
        var quoteSubject=await new TaskService(f.Factory,boundary,f.Clock).Register(f.Underwriter,new("quote",f.QuoteId),"communication-other-parent",default);
        var foreignThread=await threads.Create(f.Underwriter,quoteSubject.ResourceId,new("agency","Other original subject",policy.RelationshipId),"foreign-thread",default);
        Assert.Equal(404,(await Assert.ThrowsAsync<OperationalAccessException>(()=>threads.CreateDraft(f.Underwriter,foreignThread.ResourceId,write,"foreign-subject-file",default))).Status);
        var link=new MessageDraftAttachment{MessageId=message.ResourceId,DocumentVersionId=version.Id,CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow()};db.Add(link);
        var sqlGuard=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal(52001,Assert.IsType<SqlException>(sqlGuard.InnerException).Number);db.ChangeTracker.Clear();
        var read=await threads.ReadDraft(f.Underwriter,message.ResourceId,default);Assert.Contains(agencyVersion.Id.ToString(),read.Body);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalMessageDraft SET State=N'queued' WHERE Id={message.ResourceId}");
        var queued=await threads.ReadDraft(f.Underwriter,message.ResourceId,default);
        Assert.Equal(409,(await Assert.ThrowsAsync<OperationalAccessException>(()=>threads.UpdateDraft(f.Underwriter,message.ResourceId,queued.Etag!,write,"queued-edit",default))).Status);
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OperationalMessageDraft SET Body=N'changed' WHERE Id={message.ResourceId}"))).Number);
        Assert.Equal(52000,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM MessageDraftAttachment WHERE MessageId={message.ResourceId}"))).Number);
        await AssertRetainedMidDowngradeRefused(db, "20260921215507_TaskDocumentAttachments");
        Assert.Equal(2,await db.Set<OperationalMessageDraft>().CountAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAgencyRelationship SET State=N'inactive' WHERE Id={policy.RelationshipId}");
        var revokedRead=await Record.ExceptionAsync(()=>threads.ReadDraft(f.Underwriter,message.ResourceId,default));
        Assert.True(revokedRead is OperationalAccessException{Status:404} or QuoteOperationException{Status:404});
        var revokedReplay=await Record.ExceptionAsync(()=>threads.CreateDraft(f.Underwriter,agencyThread.ResourceId,write,"agency-message",default));
        Assert.True(revokedReplay is OperationalAccessException{Status:404} or QuoteOperationException{Status:404});
        Assert.Equal(2,await db.Set<OperationalMessageDraft>().CountAsync());
    });
}
