using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;
namespace BackOffice.Api;
public static class ClaimsEndpoints
{
    public static void MapClaims(this WebApplication app)
    {
        foreach(var action in new[]{"handoff","log-and-handoff"})
        {
            var log=action=="log-and-handoff";
            app.MapPost($"/api/v1/incidents/{{incidentId:guid}}/{action}",(Guid incidentId,HttpContext context,ClaimsHandoffService service)=>Run(context,async()=>
            {
                QuoteEndpoints.Id(incidentId);using var input=await Input(context);QuoteHttpInput.Keys(input.RootElement,"revisionId","resolutionId","providerId");
                return await service.Handoff(LocalIdentityService.Actor(context.User),incidentId,Etag(context),Id(input.RootElement,"revisionId"),Id(input.RootElement,"resolutionId"),Id(input.RootElement,"providerId"),log,QuoteHttpInput.Key(context.Request),context.RequestAborted);
            })).RequireAuthorization("incident-handoff");
        }
        foreach(var purpose in new[]{"refresh","contact"})
        {
            var captured=purpose;
            app.MapPost($"/api/v1/incidents/{{incidentId:guid}}/{purpose}",(Guid incidentId,HttpContext context,ClaimsSummaryService service)=>Run(context,async()=>
            {
                QuoteEndpoints.Id(incidentId);using var input=await Input(context);QuoteHttpInput.Keys(input.RootElement,captured=="contact"?["body"]:[]);
                return await service.FollowUp(LocalIdentityService.Actor(context.User),incidentId,Etag(context),captured,captured=="contact"?Text(input.RootElement,"body"):null,QuoteHttpInput.Key(context.Request),context.RequestAborted);
            })).RequireAuthorization("incident-handoff");
        }
        app.MapPost("/api/v1/incidents/{incidentId:guid}/requests/{requestId:guid}/retry",(Guid incidentId,Guid requestId,HttpContext context,ClaimsSummaryService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(incidentId);QuoteEndpoints.Id(requestId);using var input=await Input(context);QuoteHttpInput.Keys(input.RootElement,"reason");
            return await service.Retry(LocalIdentityService.Actor(context.User),incidentId,requestId,Etag(context),Text(input.RootElement,"reason"),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("incident-handoff");
        app.MapGet("/api/v1/incidents/{incidentId:guid}/administrators",(Guid incidentId,HttpContext context,ClaimsSummaryService service)=>Run(context,()=>
        {QuoteEndpoints.Id(incidentId);QuoteHttpInput.NoQuery(context.Request);return service.Administrators(LocalIdentityService.Actor(context.User),incidentId,context.RequestAborted);})).RequireAuthorization("incident-read");
        foreach(var family in new[]{"handoffs","summaries","requests"})
        {
            var captured=family;
            app.MapGet($"/api/v1/incidents/{{incidentId:guid}}/{family}",(Guid incidentId,HttpContext context,ClaimsSummaryService service,PartyPaging paging)=>Run(context,async()=>
            {
                QuoteEndpoints.Id(incidentId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"claims-"+captured+"-v1")??throw new QuoteHttpException(400,"invalid-claims-cursor");
                var rows=captured=="handoffs"?await service.Handoffs(actor,incidentId,page.KeyId,page.Size,page.AsOf,context.RequestAborted):captured=="summaries"?await service.Summaries(actor,incidentId,page.KeyId,page.Size,page.AsOf,context.RequestAborted):await service.Requests(actor,incidentId,page.KeyId,page.Size,page.AsOf,context.RequestAborted);
                return new(incidentId,200,JsonSerializer.Serialize(new{items=rows.Items,totalCount=rows.TotalCount,nextCursor=paging.NextGuid(page,rows.NextId)},new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            })).RequireAuthorization("incident-read");
        }
    }
    private static string Etag(HttpContext context)=>TaskService.Etag(QuoteHttpInput.Version(context.Request));
    private static Guid Id(JsonElement root,string name)
    {if(!root.TryGetProperty(name,out var field)||field.ValueKind!=JsonValueKind.String||!field.TryGetGuid(out var id)||id==Guid.Empty)throw new QuoteHttpException(400,"invalid-claims-selection");return id;}
    private static string Text(JsonElement root,string name)=>root.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString()!:throw new QuoteHttpException(400,"invalid-claims-input");
    private static Task<JsonDocument> Input(HttpContext context){QuoteHttpInput.NoQuery(context.Request);return QuoteHttpInput.Read(context.Request,context.RequestAborted,16384);}
    private static async Task<IResult> Run(HttpContext context,Func<Task<CommandOutcome>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";
        try{return QuoteEndpoints.Outcome(context,await action());}
        catch(OperationalAccessException e){return IdentityEndpoints.Problem(context,e.Status,e.Code,"Review the incident and claims request.");}
        catch(ClaimsRuleException e){return IdentityEndpoints.Problem(context,422,e.Code,"Check the claims request.");}
        catch(IncidentRuleException e){return IdentityEndpoints.Problem(context,422,e.Code,"Complete the incident before handoff.");}
        catch(IncidentOccurrenceException e){return IdentityEndpoints.Problem(context,422,e.Code,"Clarify the incident occurrence.");}
        catch(Exception e)when(QuoteEndpoints.Known(e)){return QuoteEndpoints.Failure(context,e);}
    }
}
