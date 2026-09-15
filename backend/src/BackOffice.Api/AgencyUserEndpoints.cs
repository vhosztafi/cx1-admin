using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyUserEndpoints
{
    public static void MapAgencyUsers(this WebApplication app)
    {
        app.MapGet("/api/v1/agencies/{agencyId:guid}/users",(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> db,PartyPaging paging)=>Users(agencyId,null,context,db,paging)).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/users/{userId:guid}",(Guid agencyId,Guid userId,HttpContext context,IDbContextFactory<BackOfficeDbContext> db,PartyPaging paging)=>Users(agencyId,userId,context,db,paging)).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/invitations",(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> db,PartyPaging paging)=>Invitations(agencyId,null,context,db,paging)).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/invitations/{invitationId:guid}",(Guid agencyId,Guid invitationId,HttpContext context,IDbContextFactory<BackOfficeDbContext> db,PartyPaging paging)=>Invitations(agencyId,invitationId,context,db,paging)).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/invitations",Create).RequireAuthorization("agency-admin");
        app.MapPut("/api/v1/agencies/{agencyId:guid}/users/{userId:guid}",Edit).RequireAuthorization("agency-admin");
        foreach(var action in new[]{"deactivate","reactivate"})
            app.MapPost($"/api/v1/agencies/{{agencyId:guid}}/users/{{userId:guid}}/{action}",(Guid agencyId,Guid userId,HttpContext context,AgencyUserLifecycle service)=>ChangeUser(agencyId,userId,action,context,service)).RequireAuthorization("agency-admin");
        foreach(var action in new[]{"resend","revoke"})
            app.MapPost($"/api/v1/invitations/{{invitationId:guid}}/{action}",(Guid invitationId,HttpContext context,AgencyInvitationCommands service)=>ChangeInvitation(invitationId,action,context,service)).RequireAuthorization("agency-admin");
    }

    private static async Task<IResult> Users(Guid agencyId,Guid? userId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        try
        {
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,context.RequestAborted);
            var actor=LocalIdentityService.Actor(context.User);
            var scope=await AgencyUserAuthority.ReadScope(db,actor,agencyId,context.RequestAborted);
            var page=paging.ReadBound(context,actor,"createdAt-desc,id",scope);if(page is null)return InvalidQuery(context);
            var query=from user in db.Set<StaffUser>() join link in db.Set<UserRole>() on user.Id equals link.UserId join role in db.Set<Role>() on link.RoleId equals role.Id
                where user.AgencyId==agencyId&&role.Scope=="agency"&&(userId==null||user.Id==userId)&&user.CreatedAt<=page.AsOf
                orderby user.CreatedAt descending,user.Id select new{user.Id,user.AgencyId,user.DisplayName,user.Email,role=role.Code,state=user.State=="suspended"?"inactive":user.State,user.CreatedAt,user.RowVersion,lastSeenAt=db.Set<UserSession>().Where(session=>session.UserId==user.Id).Max(session=>(DateTimeOffset?)session.LastSeenAt)};
            var total=await query.CountAsync(context.RequestAborted);var rows=await query.Skip(userId is null?page.Offset:0).Take(userId is null?page.Size:1).ToListAsync(context.RequestAborted);
            var items=rows.Select(x=>new{x.Id,x.AgencyId,x.DisplayName,x.Email,x.role,x.state,x.CreatedAt,x.lastSeenAt,etag=AgencyDraftService.Etag(x.RowVersion)}).ToList();
            if(userId is not null){if(items.Count==0)return Missing(context);context.Response.Headers.ETag=items[0].etag;await transaction.CommitAsync(context.RequestAborted);return Results.Json(items[0],ClientEndpoints.Json);}
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }

    private static async Task<IResult> Invitations(Guid agencyId,Guid? invitationId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        try
        {
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,context.RequestAborted);
            var actor=LocalIdentityService.Actor(context.User);
            var scope=await AgencyUserAuthority.ReadScope(db,actor,agencyId,context.RequestAborted);
            var page=paging.ReadBound(context,actor,"createdAt-desc,id",scope,"userId");if(page is null)return InvalidQuery(context);
            Guid? userId=null;if(context.Request.Query.TryGetValue("userId",out var value)){if(!Guid.TryParse(value,out var parsed)||parsed==Guid.Empty)return InvalidQuery(context);userId=parsed;}
            var now=DateTimeOffset.UtcNow;
            var query=from invitation in db.Set<AgencyInvitation>() join user in db.Set<StaffUser>() on invitation.UserId equals user.Id join link in db.Set<UserRole>() on user.Id equals link.UserId join role in db.Set<Role>() on link.RoleId equals role.Id
                where invitation.AgencyId==agencyId&&user.AgencyId==agencyId&&role.Scope=="agency"&&(invitationId==null||invitation.Id==invitationId)&&(userId==null||user.Id==userId)&&invitation.CreatedAt<=page.AsOf
                orderby invitation.CreatedAt descending,invitation.Id
                select new{invitation.Id,invitation.UserId,invitation.AgencyId,user.Email,role=role.Code,invitation.CreatedAt,state=invitation.State=="pending"&&invitation.ExpiresAt<=now?"expired":invitation.State,invitation.IssuedAt,invitation.ExpiresAt,invitation.NotificationId,invitation.AcceptedAt,invitation.RevokedAt,invitation.RowVersion};
            var total=await query.CountAsync(context.RequestAborted);var rows=await query.Skip(invitationId is null?page.Offset:0).Take(invitationId is null?page.Size:1).ToListAsync(context.RequestAborted);
            var items=rows.Select(x=>new{x.Id,x.UserId,x.AgencyId,x.Email,x.role,x.CreatedAt,x.state,x.IssuedAt,x.ExpiresAt,x.NotificationId,x.AcceptedAt,x.RevokedAt,etag=AgencyDraftService.Etag(x.RowVersion)}).ToList();
            if(invitationId is not null){if(items.Count==0)return Missing(context);context.Response.Headers.ETag=items[0].etag;await transaction.CommitAsync(context.RequestAborted);return Results.Json(items[0],ClientEndpoints.Json);}
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Json(new{items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }

    private static async Task<IResult> Create(Guid agencyId,HttpContext context,AgencyUserService service)
    {
        try{var input=ClientEndpoints.Input<InviteInput>(await Body(context));var result=await service.Invite(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),AgencyUserRules.Validate(input.Email,input.DisplayName,input.Role),context.RequestAborted);return Response(context,result,$"/api/v1/agencies/{agencyId}/users/{result.ResourceId}");}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Edit(Guid agencyId,Guid userId,HttpContext context,AgencyUserLifecycle service)
    {
        try{var input=ClientEndpoints.Input<EditInput>(await Body(context));return Response(context,await service.Edit(LocalIdentityService.Actor(context.User),agencyId,userId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.DisplayName,input.Role,input.Reason,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> ChangeUser(Guid agencyId,Guid userId,string action,HttpContext context,AgencyUserLifecycle service)
    {
        try{var input=ClientEndpoints.Input<ReasonInput>(await Body(context));var actor=LocalIdentityService.Actor(context.User);var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);return Response(context,action=="deactivate"?await service.Deactivate(actor,agencyId,userId,key,version,input.Reason,context.RequestAborted):await service.Reactivate(actor,agencyId,userId,key,version,input.Reason,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> ChangeInvitation(Guid invitationId,string action,HttpContext context,AgencyInvitationCommands service)
    {
        try{var input=ClientEndpoints.Input<ReasonInput>(await Body(context));var actor=LocalIdentityService.Actor(context.User);var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);return Response(context,action=="resend"?await service.Resend(actor,invitationId,key,version,input.Reason,context.RequestAborted):await service.Revoke(actor,invitationId,key,version,input.Reason,context.RequestAborted));}
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<JsonElement> Body(HttpContext context)
    {
        if(!context.Request.HasJsonContentType())throw new AgencyCommandException(415,"json-required");
        if(context.Request.ContentLength>8192)throw new AgencyCommandException(413,"agency-user-body-size");
        using var stream=new MemoryStream();var bytes=new byte[4096];int read;
        while((read=await context.Request.Body.ReadAsync(bytes,context.RequestAborted))>0){if(stream.Length+read>8192)throw new AgencyCommandException(413,"agency-user-body-size");stream.Write(bytes,0,read);}
        stream.Position=0;using var document=await JsonDocument.ParseAsync(stream,new JsonDocumentOptions{MaxDepth=4},context.RequestAborted);return document.RootElement.Clone();
    }
    private static IResult Response(HttpContext context,CommandOutcome outcome,string? location=null){context.Response.Headers.ETag=outcome.Etag;if(location is not null)context.Response.Headers.Location=location;return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);}
    private static bool IsError(Exception ex)=>ex is AgencyCommandException or AgencyNotificationProviderException||ClientEndpoints.IsCommandError(ex);
    private static IResult Error(HttpContext context,Exception ex)=>ex is AgencyCommandException error?IdentityEndpoints.Problem(context,error.Status,error.Code,"Check the user or invitation state and retry."):ex is AgencyNotificationProviderException?IdentityEndpoints.Problem(context,503,"invitation-delivery-unavailable","Invitation could not be queued. No changes were saved."):ClientEndpoints.CommandError(context,ex,"agency-user");
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"agency-user-not-found","Agency user or invitation not found.");
    private static IResult InvalidQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Refresh the user or invitation list.");
    private sealed record InviteInput([property:JsonRequired]string Email,[property:JsonRequired]string DisplayName,[property:JsonRequired]string Role);
    private sealed record EditInput([property:JsonRequired]string DisplayName,[property:JsonRequired]string Role,[property:JsonRequired]string Reason);
    private sealed record ReasonInput([property:JsonRequired]string Reason);
}
