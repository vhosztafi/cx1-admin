using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;
using BackOffice.Application;

namespace BackOffice.Api;

public static class DeliveryEndpoints
{
    private static readonly JsonSerializerOptions InputJson=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=false,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public static void MapDeliveries(this WebApplication app)
    {
        app.MapDocumentPackOptions();
        app.MapGet("/api/v1/messages/{messageId:guid}/deliveries",(Guid messageId,HttpContext context,DeliveryReadService read,PartyPaging paging)=>List(messageId,true,context,read,paging)).RequireAuthorization("message-read");
        app.MapGet("/api/v1/records/{recordId:guid}/document-deliveries",(Guid recordId,HttpContext context,DeliveryReadService read,PartyPaging paging)=>List(recordId,false,context,read,paging)).RequireAuthorization("document-read");
        foreach(var kind in new[]{"message","document"})
        {
            var message=kind=="message";
            app.MapGet($"/api/v1/{kind}-deliveries/{{deliveryId:guid}}",(Guid deliveryId,HttpContext context,DeliveryReadService read)=>Read(context,async()=>
            {QuoteEndpoints.Id(deliveryId);QuoteHttpInput.NoQuery(context.Request);return await read.Read(LocalIdentityService.Actor(context.User),deliveryId,context.RequestAborted);})).RequireAuthorization(message?"message-read":"document-read");
            app.MapGet($"/api/v1/{kind}-deliveries/{{deliveryId:guid}}/attempts",(Guid deliveryId,HttpContext context,DeliveryReadService read)=>Read(context,async()=>
            {QuoteEndpoints.Id(deliveryId);QuoteHttpInput.NoQuery(context.Request);return new(deliveryId,200,JsonSerializer.Serialize(await read.Attempts(LocalIdentityService.Actor(context.User),deliveryId,context.RequestAborted),ClientEndpoints.Json));})).RequireAuthorization(message?"message-read":"document-read");
            foreach(var action in new[]{"retry","resend"})
            {
                var resend=action=="resend";
                app.MapPost($"/api/v1/{kind}-deliveries/{{deliveryId:guid}}/{action}",(Guid deliveryId,HttpContext context,MessageDeliveryService service)=>Run(context,async()=>
                {
                    QuoteEndpoints.Id(deliveryId);using var json=await QuoteHttpInput.Read(context.Request,context.RequestAborted,8192);QuoteHttpInput.Keys(json.RootElement,"reason");
                    if(!json.RootElement.TryGetProperty("reason",out var reason)||reason.ValueKind!=JsonValueKind.String)throw new QuoteHttpException(422,"delivery-reason-required");
                    return await service.Recover(LocalIdentityService.Actor(context.User),deliveryId,TaskService.Etag(QuoteHttpInput.Version(context.Request)),reason.GetString()!,resend,message,QuoteHttpInput.Key(context.Request),context.RequestAborted);
                })).RequireAuthorization(message?"message-send":"document-send");
            }
        }
        app.MapPost("/api/v1/messages/{messageId:guid}/send",(Guid messageId,HttpContext context,MessageDeliveryService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(messageId);using var input=await QuoteHttpInput.Read(context.Request,context.RequestAborted,4096);QuoteHttpInput.Keys(input.RootElement);
            return await service.Send(LocalIdentityService.Actor(context.User),messageId,TaskService.Etag(QuoteHttpInput.Version(context.Request)),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("message-send");
        app.MapPost("/api/v1/records/{recordId:guid}/document-deliveries",(Guid recordId,HttpContext context,DocumentPackService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(recordId);using var json=await QuoteHttpInput.Read(context.Request,context.RequestAborted,65536);
            PackInput input;try{input=json.RootElement.Deserialize<PackInput>(InputJson)??throw new QuoteHttpException(400,"invalid-pack-input");}
            catch(JsonException){throw new QuoteHttpException(400,"invalid-pack-input");}
            return await service.Send(LocalIdentityService.Actor(context.User),recordId,new(input.DocumentVersionIds,input.RecipientContactIds,input.Subject,input.Body),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("document-send");
    }
    private static Task<IResult> List(Guid id,bool message,HttpContext context,DeliveryReadService read,PartyPaging paging)=>Read(context,async()=>
    {
        QuoteEndpoints.Id(id);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"delivery-created-id-v1")??throw new QuoteHttpException(400,"invalid-delivery-cursor");
        var rows=await read.List(actor,id,message,page.KeyId,page.Size,page.AsOf,context.RequestAborted);
        return new(id,200,JsonSerializer.Serialize(new{items=rows.Items,totalCount=rows.TotalCount,nextCursor=paging.NextGuid(page,rows.NextId)},ClientEndpoints.Json));
    });
    private static async Task<IResult> Read(HttpContext context,Func<Task<CommandOutcome>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";
        try{return QuoteEndpoints.Outcome(context,await action());}
        catch(OperationalAccessException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"The delivery is unavailable.");}
        catch(Exception error)when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
    private static async Task<IResult> Run(HttpContext context,Func<Task<CommandOutcome>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";
        try{var result=await action();context.Response.Headers.Location=$"/api/v1/jobs/{result.ResourceId}";return QuoteEndpoints.Outcome(context,result);}
        catch(OperationalAccessException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"The delivery is unavailable. Review its current details.");}
        catch(CommunicationRuleException error){return IdentityEndpoints.Problem(context,422,error.Code,"Choose eligible recipients and ready files, and enter a subject and message.");}
        catch(Exception error)when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
    public sealed record PackInput([property:JsonRequired]Guid[] DocumentVersionIds,[property:JsonRequired]Guid[] RecipientContactIds,[property:JsonRequired]string Subject,[property:JsonRequired]string Body);
}
