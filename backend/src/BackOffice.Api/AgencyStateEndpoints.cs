using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyStateEndpoints
{
    public static void MapAgencyStateRequests(this WebApplication app)
    {
        foreach(var action in new[]{"activate","suspend","reactivate"})
            app.MapPost($"/api/v1/agencies/{{agencyId:guid}}/{action}",(Guid agencyId,HttpContext context)=>Propose(agencyId,action,context)).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/state-requests",List).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agency-state-requests/{requestId:guid}",Get).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agency-state-requests/{requestId:guid}/decision",Decide).RequireAuthorization("agency-admin");
    }
    private static async Task<IResult> Propose(Guid agencyId,string action,HttpContext context)
    {
        try
        {
            var input=ClientEndpoints.Input<ReasonInput>(await AgencyEndpoints.ReadBody(context));var actor=LocalIdentityService.Actor(context.User);
            var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);var token=context.RequestAborted;
            var result=action switch
            {
                "activate"=>await context.RequestServices.GetRequiredService<AgencyActivationService>().Propose(actor,agencyId,key,version,input.Reason,token),
                "suspend"=>await context.RequestServices.GetRequiredService<AgencySuspensionService>().Propose(actor,agencyId,key,version,input.Reason,token),
                _=>await context.RequestServices.GetRequiredService<AgencyReactivationService>().Propose(actor,agencyId,key,version,input.Reason,token)
            };
            return Response(context,result);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Decide(Guid requestId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies)
    {
        try
        {
            var input=ClientEndpoints.Input<DecisionInput>(await AgencyEndpoints.ReadBody(context));
            if(input.Outcome is not ("approve" or "reject"))throw new AgencyCommandException(422,"invalid-decision");
            var actor=LocalIdentityService.Actor(context.User);await agencies.Authorize(actor,null,context.RequestAborted);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            // Resolve the immutable request scope/kind before service receipt lookup; neither comes from the body.
            var request=await db.Set<AgencyStateRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId,context.RequestAborted);
            if(request is null)return Missing(context);
            var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);var approve=input.Outcome=="approve";var token=context.RequestAborted;
            var result=request.Kind switch
            {
                "activation"=>await context.RequestServices.GetRequiredService<AgencyActivationDecisions>().Decide(actor,request.AgencyId,requestId,key,version,approve,input.Reason,token),
                "suspension"=>await context.RequestServices.GetRequiredService<AgencySuspensionService>().Decide(actor,request.AgencyId,requestId,key,version,approve,input.Reason,token),
                "reactivation"=>await context.RequestServices.GetRequiredService<AgencyReactivationService>().Decide(actor,request.AgencyId,requestId,key,version,approve,input.Reason,token),
                _=>throw new AgencyCommandException(409,"unsupported-state-request")
            };
            return Response(context,result);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Get(Guid requestId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies)
    {
        try
        {
            if(context.Request.Query.Count>0)return BadQuery(context);
            await agencies.Authorize(LocalIdentityService.Actor(context.User),null,context.RequestAborted);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            var request=await db.Set<AgencyStateRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId,context.RequestAborted);if(request is null)return Missing(context);
            var labels=await Labels(db,[request],context.RequestAborted);context.Response.Headers.ETag=AgencyDraftService.Etag(request.RowVersion);
            return Results.Json(View(request,labels),ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> List(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies,PartyPaging paging)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);await agencies.Authorize(actor,agencyId,context.RequestAborted);
            var page=paging.Read(context,actor,"createdAt-desc,id");if(page is null)return BadQuery(context);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            var query=db.Set<AgencyStateRequest>().AsNoTracking().Where(x=>x.AgencyId==agencyId&&x.CreatedAt<=page.AsOf);
            var total=await query.CountAsync(context.RequestAborted);var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
            var labels=await Labels(db,rows,context.RequestAborted);var items=rows.Select(x=>View(x,labels)).ToList();
            return Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<Dictionary<Guid,string>> Labels(BackOfficeDbContext db,IReadOnlyList<AgencyStateRequest> rows,CancellationToken token)
    {
        var ids=rows.Select(x=>x.RequestedBy).Concat(rows.Where(x=>x.DecisionBy!=null).Select(x=>x.DecisionBy!.Value)).Distinct().ToArray();
        return await db.Set<StaffUser>().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,token);
    }
    private static object View(AgencyStateRequest request,IReadOnlyDictionary<Guid,string> labels)=>new
    {
        request.Id,request.AgencyId,request.RequestedBy,requestedByLabel=labels[request.RequestedBy],reason=request.RequestReason,
        baseVersion=AgencyDraftService.Etag(request.BaseVersion),inputFingerprint=request.ProposedInputFingerprint,request.CreatedAt,request.State,
        request.DecisionBy,decisionByLabel=request.DecisionBy is Guid actor?labels[actor]:null,request.DecisionReason,request.DecidedAt,
        requestKind=request.Kind,stateRequested=request.RequestedState,etag=AgencyDraftService.Etag(request.RowVersion)
    };
    private static IResult Response(HttpContext context,CommandOutcome result)
    {context.Response.Headers.ETag=result.Etag;context.Response.Headers.Location="/api/v1/agency-state-requests/"+result.ResourceId;return Results.Content(result.Body,"application/json",statusCode:result.Status);}
    private static bool IsError(Exception ex)=>ex is AgencyCommandException or AgencyNotificationProviderException||ClientEndpoints.IsCommandError(ex);
    private static IResult Error(HttpContext context,Exception ex)=>ex is AgencyCommandException agency?IdentityEndpoints.Problem(context,agency.Status,agency.Code,"Check the agency proposal and its current state."):ex is AgencyNotificationProviderException?IdentityEndpoints.Problem(context,503,"agency-delivery-unavailable","Agency changes could not be applied."):ClientEndpoints.CommandError(context,ex,"agency-state");
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"agency-state-request-not-found","Agency state request not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Use supported request list pagination.");
    private sealed record ReasonInput([property:JsonRequired]string Reason);
    private sealed record DecisionInput([property:JsonRequired]string Outcome,[property:JsonRequired]string Reason);
}
