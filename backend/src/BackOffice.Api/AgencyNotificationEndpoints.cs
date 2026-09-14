using System.Text.Json.Serialization;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyNotificationEndpoints
{
    public static void MapAgencyNotifications(this WebApplication app)
    {
        app.MapGet("/api/v1/agencies/{agencyId:guid}/notifications",(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)=>Read(agencyId,null,context,factory,paging)).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/notifications/{notificationId:guid}",(Guid agencyId,Guid notificationId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)=>Read(agencyId,notificationId,context,factory,paging)).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/notifications/{notificationId:guid}/retry",Retry).RequireAuthorization("agency-admin");
    }
    private static async Task<IResult> Read(Guid agencyId,Guid? notificationId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var state=await db.Set<Agency>().Where(x=>x.Id==agencyId).Select(x=>x.State).SingleOrDefaultAsync(context.RequestAborted);
        if(state is null)return Missing(context);
        var page=paging.Read(context,LocalIdentityService.Actor(context.User),"createdAt-desc,id");if(page is null)return IdentityEndpoints.Problem(context,400,"invalid-query","Refresh the notification list.");
        var query=from message in db.Set<AgencyNotification>() join work in db.Set<OutboxWork>() on message.WorkId equals work.Id
            where message.AgencyId==agencyId&&message.CreatedAt<=page.AsOf&&(notificationId==null||message.Id==notificationId)
            orderby message.CreatedAt descending,message.Id
            select new{message.Id,message.AgencyId,message.Purpose,message.CreatedAt,work.State,work.Attempts,work.AttemptLimit,work.CompletedAt,work.ErrorCode,work.RowVersion};
        var total=await query.CountAsync(context.RequestAborted);var rows=await query.Skip(notificationId is null?page.Offset:0).Take(notificationId is null?page.Size:1).ToListAsync(context.RequestAborted);
        var items=rows.Select(x=>new{x.Id,x.AgencyId,kind=x.Purpose=="agency-activated"?"activation":"invitation",
            state=x.State=="succeeded"?"demo-delivered":x.State=="leased"?"processing":x.State=="pending"?"queued":x.ErrorCode is "provider-unavailable" or "provider-timeout" or "attempts-exhausted"?"exhausted":"rejected",
            x.Attempts,x.CreatedAt,x.CompletedAt,lastResultCode=x.ErrorCode,etag=AgencyDraftService.Etag(x.RowVersion),
            retryAllowed=state=="active"&&x.Purpose=="agency-activated"&&JobRetryBudget.ExpandedLimit(x.State,x.ErrorCode,x.Attempts,x.AttemptLimit) is not null}).ToList();
        context.Response.Headers.CacheControl="no-store";
        if(notificationId is not null){if(items.Count==0)return Missing(context);context.Response.Headers.ETag=items[0].etag;return Results.Json(items[0],ClientEndpoints.Json);}
        return Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},ClientEndpoints.Json);
    }
    private static async Task<IResult> Retry(Guid agencyId,Guid notificationId,HttpContext context,AgencyNotificationRetry retry)
    {
        try
        {
            if(!context.Request.HasJsonContentType())throw new AgencyCommandException(415,"json-required");
            if(context.Request.ContentLength>8192)throw new AgencyCommandException(413,"notification-body-size");
            using var buffer=new MemoryStream();var bytes=new byte[4096];int read;
            while((read=await context.Request.Body.ReadAsync(bytes,context.RequestAborted))>0)
            {if(buffer.Length+read>8192)throw new AgencyCommandException(413,"notification-body-size");buffer.Write(bytes,0,read);}
            buffer.Position=0;using var document=await JsonDocument.ParseAsync(buffer,new JsonDocumentOptions{MaxDepth=4},context.RequestAborted);
            var input=ClientEndpoints.Input<RetryInput>(document.RootElement);
            var outcome=await retry.Execute(LocalIdentityService.Actor(context.User),agencyId,notificationId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.Reason,context.RequestAborted);
            context.Response.Headers.ETag=outcome.Etag;context.Response.Headers.CacheControl="no-store";context.Response.Headers.Location=$"/api/v1/agencies/{agencyId}/notifications/{notificationId}";
            return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);
        }
        catch(Exception ex)when(ex is AgencyCommandException||ClientEndpoints.IsCommandError(ex))
        {return ex is AgencyCommandException error?IdentityEndpoints.Problem(context,error.Status,error.Code,"Check the notification state and retry reason."):ClientEndpoints.CommandError(context,ex,"notification");}
    }
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"notification-not-found","Agency notification not found.");
    private sealed record RetryInput([property:JsonRequired]string Reason);
}
