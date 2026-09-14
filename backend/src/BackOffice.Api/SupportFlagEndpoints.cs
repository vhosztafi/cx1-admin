using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class SupportFlagEndpoints
{
    public static void MapSupportFlags(this WebApplication app)
    {
        const string person="/api/v1/relationships/{relationshipId:guid}/people/{personId:guid}/flags";
        app.MapGet(person,List).RequireAuthorization("support-internal-read");app.MapPost(person,Create).RequireAuthorization("support-write");
        app.MapGet("/api/v1/flags/{flagId:guid}",Get).RequireAuthorization("support-internal-read");
        app.MapPut("/api/v1/flags/{flagId:guid}",Update).RequireAuthorization("support-write");
        app.MapPost("/api/v1/flags/{flagId:guid}/end",End).RequireAuthorization("support-write");
        app.MapGet("/api/v1/flags/{flagId:guid}/history",History).RequireAuthorization("support-internal-read");
        app.MapGet("/api/v1/relationships/{relationshipId:guid}/support-instructions",Safe).RequireAuthorization("support-safe-read-explicit-grant");
        app.MapGet("/api/v1/relationships/{relationshipId:guid}/support-instructions/preview",Safe).RequireAuthorization("relationship-read");
    }
    private static async Task<IResult> List(Guid relationshipId,Guid personId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var scope=new PartyScope(actor);var token=context.RequestAborted;
        await using var db=await factory.CreateDbContextAsync(token);
        var relationship=await scope.Relationships(db).SingleOrDefaultAsync(x=>x.Id==relationshipId,token);
        if(relationship is null || !await scope.Contacts(db,includeEnded:true).AnyAsync(x=>x.RelationshipId==relationshipId && x.PersonId==personId,token))return Missing(context);
        var page=paging.Read(context,actor,"id");if(page is null)return BadQuery(context);
        var query=new SupportFlagScope(actor).InternalFlags(db).Where(x=>x.ClientId==relationship.ClientId && x.PersonId==personId && x.CreatedAt<=page.AsOf);
        var total=await query.CountAsync(token);var rows=await query.OrderBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(token);
        var ids=rows.Select(x=>x.Id).ToArray();var grants=await db.Set<FlagVisibility>().Where(x=>ids.Contains(x.FlagId)).Select(x=>new {x.FlagId,x.RelationshipId}).ToListAsync(token);
        return Results.Json(new {items=rows.Select(x=>SupportFlagService.View(x,grants.Where(g=>g.FlagId==x.Id).Select(g=>g.RelationshipId))),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
    }
    private static async Task<IResult> Get(Guid flagId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var flag=await new SupportFlagScope(LocalIdentityService.Actor(context.User)).InternalFlags(db).SingleOrDefaultAsync(x=>x.Id==flagId,context.RequestAborted);
        if(flag is null)return Missing(context);
        var grants=await db.Set<FlagVisibility>().Where(x=>x.FlagId==flagId).Select(x=>x.RelationshipId).ToArrayAsync(context.RequestAborted);
        context.Response.Headers.ETag=Etag(flag.RowVersion);return Results.Json(SupportFlagService.View(flag,grants),ClientEndpoints.Json);
    }
    private static async Task<IResult> History(Guid flagId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var scope=new SupportFlagScope(actor);var token=context.RequestAborted;
        await using var db=await factory.CreateDbContextAsync(token);
        if(!await scope.InternalFlags(db).AnyAsync(x=>x.Id==flagId,token))return Missing(context);
        var page=paging.Read(context,actor,"occurredAt-desc,id");if(page is null)return BadQuery(context);
        var query=scope.InternalHistory(db).Where(x=>x.FlagId==flagId && x.OccurredAt<=page.AsOf);var total=await query.CountAsync(token);
        var rows=await query.OrderByDescending(x=>x.OccurredAt).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(token);
        return Results.Json(new {items=rows.Select(x=>new {x.Id,x.FlagId,actorLabel="Back office staff",x.OccurredAt,x.Action,x.Reason,
            snapshot=JsonSerializer.Deserialize<SupportFlagView>(x.Snapshot,ClientEndpoints.Json)}),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
    }
    private static async Task<IResult> Safe(Guid relationshipId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging,TimeProvider time)
    {
        var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
        await using var db=await factory.CreateDbContextAsync(token);
        if(!await new PartyScope(actor).Relationships(db).AnyAsync(x=>x.Id==relationshipId && x.State=="active",token))return Missing(context);
        var page=paging.Read(context,actor,"id");if(page is null)return BadQuery(context);
        var rows=await new SupportFlagScope(actor).SafeInstructions(db,relationshipId,page.AsOf).OrderBy(x=>x.Id).Skip(page.Offset).Take(page.Size+1).ToListAsync(token);
        db.Add(new AuditEvent {ActorId=actor.UserId,CreatedBy=actor.UserId,OccurredAt=time.GetUtcNow(),EventType="support.instructions-inspected",CorrelationId=Guid.NewGuid(),
            After=JsonSerializer.Serialize(new {relationshipId})});await db.SaveChangesAsync(token);
        // Never count hidden flags. Cursor presence is based solely on this exact granted projection.
        return Results.Json(new {items=rows.Take(page.Size),nextCursor=paging.Next(page,rows.Count>page.Size)},ClientEndpoints.Json);
    }
    private static Task<IResult> Create(Guid relationshipId,Guid personId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(null,relationshipId,personId,false,input,context,factory,commands,time);
    private static Task<IResult> Update(Guid flagId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(flagId,null,null,false,input,context,factory,commands,time);
    private static Task<IResult> End(Guid flagId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
        =>Write(flagId,null,null,true,input,context,factory,commands,time);
    private static async Task<IResult> Write(Guid? flagId,Guid? originId,Guid? personId,bool end,JsonElement input,HttpContext context,
        IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;ValidatedFlag? fields=null;string? reason=null;
            if(end)
            {
                reason=ClientEndpoints.Input<EndInput>(input).Reason;
                if(string.IsNullOrWhiteSpace(reason) || reason.Length>1000 || reason.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t')))throw new SupportFlagOperationException(422,"end-reason-required");
                reason=reason.Replace("\r\n","\n").Replace('\r','\n').Trim();
            }
            // Normalize the immutable shape before replay; today's review-date prerequisite belongs in the handler.
            else fields=SupportFlagRules.Validate(ClientEndpoints.Input<FlagWrite>(input));
            await using var read=await factory.CreateDbContextAsync(token);
            if(flagId is Guid existing)await SupportFlagService.AuthorizeFlagAsync(read,actor,existing,fields?.VisibleRelationshipIds ?? [],token);
            else await SupportFlagService.AuthorizeCreateAsync(read,actor,originId!.Value,personId!.Value,fields!.VisibleRelationshipIds,token);
            var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);
            var route=flagId is null ? $"/api/v1/relationships/{originId}/people/{personId}/flags" : $"/api/v1/flags/{flagId}"+(end ? "/end" : "");
            var eventType=flagId is null ? "support-flag.created" : end ? "support-flag.ended" : "support-flag.changed";
            var result=await commands.ExecuteAsync(new CommandIdentity(actor.UserId,route,key,Guid.NewGuid()),new {fields,reason},eventType,async(db,ct)=>
            {
                var now=time.GetUtcNow();var flag=flagId is null ? await SupportFlagService.CreateAsync(db,actor,originId!.Value,personId!.Value,version,fields!,now,ct)
                    : end ? await SupportFlagService.EndAsync(db,actor,flagId.Value,version,reason!,now,ct) : await SupportFlagService.UpdateAsync(db,actor,flagId.Value,version,fields!,now,ct);
                return new CommandOutcome(flag.Id,flagId is null ? 201 : 200,JsonSerializer.Serialize(new {id=flag.Id}),Etag:Etag(flag.RowVersion));
            },token);
            context.Response.Headers.ETag=result.Etag;if(result.Status==201)context.Response.Headers.Location="/api/v1/flags/"+result.ResourceId;
            return Results.Content(result.Body,"application/json",statusCode:result.Status);
        }
        catch(SupportFlagAccessException){return Missing(context);}
        catch(SupportFlagOperationException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"Refresh the support record and check the requested change.");}
        catch(Exception error) when(ClientEndpoints.IsCommandError(error)){return ClientEndpoints.CommandError(context,error,"support-flag");}
    }
    private static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"support-record-not-found","Support record not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Use supported paging options.");
    private sealed record EndInput([property:JsonRequired]string Reason);
}
