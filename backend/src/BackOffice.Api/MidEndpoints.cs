using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;
namespace BackOffice.Api;
public static class MidEndpoints
{
    public static void MapMid(this WebApplication app)
    {
        app.MapGet("/api/v1/versions/{versionId:guid}/mid-submissions",(Guid versionId,HttpContext context,MidSubmissionService service,PartyPaging paging)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(versionId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"mid-submissions-v1")??throw new QuoteHttpException(400,"invalid-mid-cursor");
            var rows=await service.List(actor,versionId,page.KeyId,page.Size,page.AsOf,context.RequestAborted);
            return new(versionId,200,JsonSerializer.Serialize(new{items=rows.Items,totalCount=rows.TotalCount,nextCursor=paging.NextGuid(page,rows.NextId)},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        })).RequireAuthorization("mid-read");
        app.MapPost("/api/v1/mid-submissions/{submissionId:guid}/retry",(Guid submissionId,HttpContext context,MidSubmissionService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(submissionId);QuoteHttpInput.NoQuery(context.Request);using var input=await QuoteHttpInput.Read(context.Request,context.RequestAborted,16384);QuoteHttpInput.Keys(input.RootElement,"reason");
            if(!input.RootElement.TryGetProperty("reason",out var field)||field.ValueKind!=JsonValueKind.String)throw new QuoteHttpException(400,"invalid-mid-reason");
            return await service.Retry(LocalIdentityService.Actor(context.User),submissionId,TaskService.Etag(QuoteHttpInput.Version(context.Request)),field.GetString()!,QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("mid-retry");
    }
    private static async Task<IResult> Run(HttpContext context,Func<Task<CommandOutcome>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";
        try{return QuoteEndpoints.Outcome(context,await action());}
        catch(OperationalAccessException e){return IdentityEndpoints.Problem(context,e.Status,e.Code,"Review the policy and exact MID submission.");}
        catch(Exception e)when(QuoteEndpoints.Known(e)){return QuoteEndpoints.Failure(context,e);}
    }
}
