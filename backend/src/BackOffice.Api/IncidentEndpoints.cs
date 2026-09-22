using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;

namespace BackOffice.Api;

public static class IncidentEndpoints
{
    public static void MapIncidents(this WebApplication app)
    {
        app.MapPost("/api/v1/incidents",(HttpContext context,IncidentService service)=>Run(context,async()=>
        {
            using var input=await Input(context);return await service.Create(LocalIdentityService.Actor(context.User),input.RootElement,QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("incident-write");
        app.MapPost("/api/v1/incidents/validate",(HttpContext context,IncidentService service)=>Run(context,async()=>
        {
            using var input=await Input(context);return new(Guid.Empty,200,JsonSerializer.Serialize(await service.Validate(LocalIdentityService.Actor(context.User),input.RootElement,context.RequestAborted),ClientEndpoints.Json));
        })).RequireAuthorization("incident-write");
        app.MapGet("/api/v1/incidents",(HttpContext context,IncidentService service,PartyPaging paging)=>Run(context,async()=>
        {
            var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"incident-created-id-v1","policyId","state")??throw new QuoteHttpException(400,"invalid-incident-cursor");
            if(!Guid.TryParse(context.Request.Query["policyId"],out var policyId))throw new QuoteHttpException(400,"incident-policy-required");QuoteEndpoints.Id(policyId);
            var state=context.Request.Query.ContainsKey("state")?context.Request.Query["state"].ToString():null;
            return Page(policyId,await service.List(actor,policyId,state,page.KeyId,page.Size,page.AsOf,context.RequestAborted),paging,page);
        })).RequireAuthorization("incident-read");
        app.MapGet("/api/v1/incidents/{incidentId:guid}",(Guid incidentId,HttpContext context,IncidentService service)=>Run(context,()=>
        {QuoteEndpoints.Id(incidentId);QuoteHttpInput.NoQuery(context.Request);return service.Read(LocalIdentityService.Actor(context.User),incidentId,context.RequestAborted);})).RequireAuthorization("incident-read");
        app.MapGet("/api/v1/incidents/{incidentId:guid}/revisions",(Guid incidentId,HttpContext context,IncidentService service,PartyPaging paging)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(incidentId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"incident-revision-number-v1")??throw new QuoteHttpException(400,"invalid-incident-cursor");
            return Page(incidentId,await service.Revisions(actor,incidentId,page.KeyId,page.Size,page.AsOf,context.RequestAborted),paging,page);
        })).RequireAuthorization("incident-read");
        app.MapGet("/api/v1/incidents/{incidentId:guid}/subject-options",(Guid incidentId,HttpContext context,IncidentService service)=>Run(context,()=>
        {
            QuoteEndpoints.Id(incidentId);
            if(context.Request.Query.Count!=1||context.Request.Query["versionId"].Count!=1||!Guid.TryParse(context.Request.Query["versionId"],out var versionId))throw new QuoteHttpException(400,"incident-version-required");
            QuoteEndpoints.Id(versionId);return service.SubjectOptions(LocalIdentityService.Actor(context.User),incidentId,versionId,context.RequestAborted);
        })).RequireAuthorization("incident-read");
        app.MapGet("/api/v1/incidents/{incidentId:guid}/occurrence-resolutions",(Guid incidentId,HttpContext context,IncidentService service,PartyPaging paging)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(incidentId);var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"incident-resolution-created-id-v1")??throw new QuoteHttpException(400,"invalid-incident-cursor");
            return Page(incidentId,await service.Resolutions(actor,incidentId,page.KeyId,page.Size,page.AsOf,context.RequestAborted),paging,page);
        })).RequireAuthorization("incident-read");
        app.MapPut("/api/v1/incidents/{incidentId:guid}",(Guid incidentId,HttpContext context,IncidentService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(incidentId);using var input=await Input(context);
            return await service.Update(LocalIdentityService.Actor(context.User),incidentId,Etag(context),input.RootElement,QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("incident-write");
        app.MapPut("/api/v1/incidents/{incidentId:guid}/description",(Guid incidentId,HttpContext context,IncidentService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(incidentId);using var input=await Input(context);QuoteHttpInput.Keys(input.RootElement,"description");
            return await service.SaveDescription(LocalIdentityService.Actor(context.User),incidentId,Etag(context),RequiredText(input.RootElement,"description"),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("incident-write");
        app.MapPost("/api/v1/incidents/{incidentId:guid}/occurrence",(Guid incidentId,HttpContext context,IncidentService service)=>Run(context,async()=>
        {
            QuoteEndpoints.Id(incidentId);using var input=await Input(context);QuoteHttpInput.Keys(input.RootElement,"occurrence","reason");
            if(!input.RootElement.TryGetProperty("occurrence",out var occurrence))throw new QuoteHttpException(400,"incident-occurrence-required");
            return await service.Clarify(LocalIdentityService.Actor(context.User),incidentId,Etag(context),occurrence,RequiredText(input.RootElement,"reason"),QuoteHttpInput.Key(context.Request),context.RequestAborted);
        })).RequireAuthorization("incident-write");
        foreach(var action in new[]{"log","occurrence-resolution"})
        {
            var log=action=="log";
            app.MapPost($"/api/v1/incidents/{{incidentId:guid}}/{action}",(Guid incidentId,HttpContext context,IncidentService service)=>Run(context,async()=>
            {
                QuoteEndpoints.Id(incidentId);using var input=await Input(context);QuoteHttpInput.Keys(input.RootElement);
                return await service.Resolve(LocalIdentityService.Actor(context.User),incidentId,Etag(context),log,QuoteHttpInput.Key(context.Request),context.RequestAborted);
            })).RequireAuthorization("incident-write");
        }
    }
    private static string Etag(HttpContext context)=>TaskService.Etag(QuoteHttpInput.Version(context.Request));
    private static string RequiredText(JsonElement input,string name)=>input.TryGetProperty(name,out var text)&&text.ValueKind==JsonValueKind.String?text.GetString()!:throw new QuoteHttpException(400,"invalid-incident-input");
    private static Task<JsonDocument> Input(HttpContext context)
    {QuoteHttpInput.NoQuery(context.Request);return QuoteHttpInput.Read(context.Request,context.RequestAborted,65536);}
    private static CommandOutcome Page(Guid id,CommunicationPage rows,PartyPaging paging,PartyPaging.Page page)
        =>new(id,200,JsonSerializer.Serialize(new{items=rows.Items,totalCount=rows.TotalCount,nextCursor=paging.NextGuid(page,rows.NextId)},ClientEndpoints.Json));
    private static async Task<IResult> Run(HttpContext context,Func<Task<CommandOutcome>> action)
    {
        context.Response.Headers.CacheControl="private, no-store";
        try{return QuoteEndpoints.Outcome(context,await action());}
        catch(OperationalAccessException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"The incident is unavailable. Review its current details.");}
        catch(IncidentRuleException error){return IdentityEndpoints.Problem(context,422,error.Code,"Check the incident details.");}
        catch(IncidentOccurrenceException error){return IdentityEndpoints.Problem(context,422,error.Code,"Check the occurrence and historical subject details.");}
        catch(Exception error)when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
