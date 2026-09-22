using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public static class CommunicationEndpoints
{
    private static readonly JsonSerializerOptions InputJson=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public static void MapCommunications(this WebApplication app)
    {
        app.MapGet("/api/v1/records/{recordId:guid}/thread-relationships",(Guid recordId,HttpContext context,ThreadService service,PartyPaging paging)=>Options(recordId,context,paging,(actor,page)=>service.Relationships(actor,recordId,page.KeyId,page.Size,page.AsOf,context.RequestAborted))).RequireAuthorization("message-read");
        app.MapGet("/api/v1/threads/{threadId:guid}/recipient-options",(Guid threadId,HttpContext context,ThreadService service,PartyPaging paging)=>Options(threadId,context,paging,(actor,page)=>service.RecipientOptions(actor,threadId,page.KeyId,page.Size,page.AsOf,context.RequestAborted))).RequireAuthorization("message-read");
        app.MapGet("/api/v1/threads/{threadId:guid}/attachment-options",(Guid threadId,HttpContext context,ThreadService service,PartyPaging paging)=>Options(threadId,context,paging,(actor,page)=>service.AttachmentOptions(actor,threadId,page.KeyId,page.Size,page.AsOf,context.RequestAborted))).RequireAuthorization("message-read");
        app.MapPost("/api/v1/records/{recordId:guid}/notes",(Guid recordId,HttpContext context,NoteService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(recordId);var input=await Input<NoteInput>(context);
            return await service.Add(LocalIdentityService.Actor(context.User),recordId,input.Body,QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("internal-note-write");
        app.MapGet("/api/v1/records/{recordId:guid}/notes",(Guid recordId,HttpContext context,NoteService service,PartyPaging paging)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(recordId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"communication-created-id-v1")??throw new QuoteHttpException(400,"invalid-communication-cursor");
            return Page(recordId,await service.List(actor,recordId,page.KeyId,page.Size,page.AsOf,context.RequestAborted),paging,page);
        })).RequireAuthorization("internal-note-read");
        app.MapPost("/api/v1/records/{recordId:guid}/threads",(Guid recordId,HttpContext context,ThreadService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(recordId);var input=await Input<ThreadInput>(context);
            return await service.Create(LocalIdentityService.Actor(context.User),recordId,new(input.Visibility,input.Subject,input.RelationshipId),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("message-write");
        app.MapGet("/api/v1/records/{recordId:guid}/threads",(Guid recordId,HttpContext context,ThreadService service,PartyPaging paging)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(recordId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"communication-created-id-v1","visibility")??throw new QuoteHttpException(400,"invalid-communication-cursor");
            var visibility=context.Request.Query.ContainsKey("visibility")?context.Request.Query["visibility"].ToString():null;
            return Page(recordId,await service.List(actor,recordId,visibility,page.KeyId,page.Size,page.AsOf,context.RequestAborted),paging,page);
        })).RequireAuthorization("message-read");
        app.MapPost("/api/v1/threads/{threadId:guid}/messages",(Guid threadId,HttpContext context,ThreadService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(threadId);var input=await Input<DraftInput>(context);
            return await service.CreateDraft(LocalIdentityService.Actor(context.User),threadId,new(input.Body,input.RecipientContactIds,input.AttachmentVersionIds),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("message-write");
        app.MapGet("/api/v1/threads/{threadId:guid}/messages",(Guid threadId,HttpContext context,ThreadService service,PartyPaging paging)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(threadId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"communication-created-id-v1")??throw new QuoteHttpException(400,"invalid-communication-cursor");
            return Page(threadId,await service.Messages(actor,threadId,page.KeyId,page.Size,page.AsOf,context.RequestAborted),paging,page);
        })).RequireAuthorization("message-read");
        app.MapPut("/api/v1/messages/{messageId:guid}",(Guid messageId,HttpContext context,ThreadService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(messageId);var input=await Input<DraftInput>(context);
            return await service.UpdateDraft(LocalIdentityService.Actor(context.User),messageId,TaskService.Etag(QuoteHttpInput.Version(context.Request)),
                new(input.Body,input.RecipientContactIds,input.AttachmentVersionIds),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("message-write");
        app.MapGet("/api/v1/messages/{messageId:guid}",(Guid messageId,HttpContext context,ThreadService service)=>Run(context,()=>
        {
            QuoteEndpoints.Id(messageId);QuoteHttpInput.NoQuery(context.Request);return service.ReadDraft(LocalIdentityService.Actor(context.User),messageId,context.RequestAborted);
        })).RequireAuthorization("message-read");
    }
    public static void MapDocumentPackOptions(this WebApplication app)
    {
        app.MapGet("/api/v1/records/{recordId:guid}/document-delivery-recipients/{relationshipId:guid}",(Guid recordId,Guid relationshipId,HttpContext context,ThreadService service,PartyPaging paging)=>Options(recordId,context,paging,(actor,page)=>
        {
            QuoteEndpoints.Id(relationshipId);return service.PackRecipients(actor,recordId,relationshipId,page.KeyId,page.Size,page.AsOf,context.RequestAborted);
        })).RequireAuthorization("document-send");
    }
    private static CommandOutcome Page(Guid id,CommunicationPage rows,PartyPaging paging,PartyPaging.Page page)
        =>new(id,200,JsonSerializer.Serialize(new{items=rows.Items,totalCount=rows.TotalCount,nextCursor=paging.NextGuid(page,rows.NextId)},ClientEndpoints.Json));
    private static Task<IResult> Options(Guid id,HttpContext context,PartyPaging paging,Func<BackOffice.Application.ActorContext,PartyPaging.Page,Task<CommunicationOptionsPage>> read)=>Run(context,async()=>
    {
        QuoteEndpoints.Id(id);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"communication-option-id-v1")??throw new QuoteHttpException(400,"invalid-communication-cursor");
        var rows=await read(actor,page);return new CommandOutcome(id,200,JsonSerializer.Serialize(new{items=rows.Items,nextCursor=paging.NextGuid(page,rows.NextId)},ClientEndpoints.Json));
    });
    private static async Task<T> Input<T>(HttpContext context)
    {
        QuoteHttpInput.NoQuery(context.Request);using var json=await QuoteHttpInput.Read(context.Request,context.RequestAborted,65536);
        if(typeof(T)==typeof(ThreadInput)&&json.RootElement.TryGetProperty("visibility",out var audience)&&audience.ValueKind==JsonValueKind.String&&audience.GetString()=="internal"&&json.RootElement.TryGetProperty("relationshipId",out _))
            throw new QuoteHttpException(400,"invalid-thread-audience-shape");
        try{return json.RootElement.Deserialize<T>(InputJson)??throw new QuoteHttpException(400,"invalid-communication-input");}
        catch(JsonException){throw new QuoteHttpException(400,"invalid-communication-input");}
    }
    private static async Task<IResult> Run(HttpContext context,Func<Task<CommandOutcome>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";
        try{return QuoteEndpoints.Outcome(context,await action());}
        catch(OperationalAccessException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"The communication is unavailable.");}
        catch(CommunicationRuleException error){return IdentityEndpoints.Problem(context,422,error.Code,"Check the communication details.");}
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
    public sealed record NoteInput([property:JsonRequired]string Body);
    public sealed record ThreadInput([property:JsonRequired]string Visibility,[property:JsonRequired]string Subject,Guid? RelationshipId=null);
    public sealed record DraftInput([property:JsonRequired]string Body,[property:JsonRequired]Guid[] RecipientContactIds,[property:JsonRequired]Guid[] AttachmentVersionIds);
}
