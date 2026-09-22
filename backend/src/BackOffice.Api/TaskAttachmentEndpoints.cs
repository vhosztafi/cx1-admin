using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public static partial class TaskEndpoints
{
    private static void MapTaskAttachments(WebApplication app)
    {
        app.MapGet("/api/v1/tasks/{taskId:guid}/attachments",(Guid taskId,HttpContext context,TaskService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(taskId);QuoteHttpInput.NoQuery(context.Request);context.Response.Headers.XContentTypeOptions="nosniff";
            var items=await service.ListDocumentAttachments(LocalIdentityService.Actor(context.User),taskId,context.RequestAborted);
            return new CommandOutcome(taskId,200,JsonSerializer.Serialize(new{items},ClientEndpoints.Json));
        })).RequireAuthorization("task-read");
        app.MapPost("/api/v1/tasks/{taskId:guid}/attachments",(Guid taskId,HttpContext context,TaskService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(taskId);QuoteHttpInput.NoQuery(context.Request);var input=await Input<AttachmentInput>(context);
            return await service.AttachDocument(LocalIdentityService.Actor(context.User),taskId,Version(context),QuoteHttpInput.Key(context.Request),input.DocumentVersionId,input.Reason,context.RequestAborted);
        })).RequireAuthorization("task-write");
        app.MapPost("/api/v1/tasks/{taskId:guid}/attachments/{attachmentId:guid}/remove",(Guid taskId,Guid attachmentId,HttpContext context,TaskService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(taskId);QuoteEndpoints.Id(attachmentId);QuoteHttpInput.NoQuery(context.Request);var input=await Input<AttachmentRemoveInput>(context);
            return await service.RemoveDocumentAttachment(LocalIdentityService.Actor(context.User),taskId,Version(context),QuoteHttpInput.Key(context.Request),attachmentId,input.Reason,context.RequestAborted);
        })).RequireAuthorization("task-write");
    }
    public sealed record AttachmentInput([property:JsonRequired] Guid DocumentVersionId,[property:JsonRequired] string Reason);
    public sealed record AttachmentRemoveInput([property:JsonRequired] string Reason);
}
