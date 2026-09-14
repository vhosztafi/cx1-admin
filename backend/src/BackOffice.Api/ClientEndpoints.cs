using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static partial class ClientEndpoints
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web)
    {DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};

    public static void MapClients(this WebApplication app)
    {
        app.MapGet("/api/v1/clients",List).RequireAuthorization("client-read");
        app.MapGet("/api/v1/clients/{clientId:guid}",Get).RequireAuthorization("client-read");
        app.MapPost("/api/v1/clients",Create).RequireAuthorization("client-write");
        app.MapPut("/api/v1/clients/{clientId:guid}",Update).RequireAuthorization("client-write");
        app.MapGet("/api/v1/clients/{clientId:guid}/relationships",Relationships).RequireAuthorization("client-read");
        app.MapPost("/api/v1/clients/{clientId:guid}/relationships",CreateRelationship).RequireAuthorization("client-write");
        app.MapGet("/api/v1/relationships/{relationshipId:guid}",Relationship).RequireAuthorization("relationship-read");
        app.MapGet("/api/v1/relationship-agencies",Agencies).RequireAuthorization("relationship-read");
        app.MapGet("/api/v1/clients/{clientId:guid}/activity",Activity).RequireAuthorization("client-read");
        app.MapGet("/api/v1/clients/{clientId:guid}/records",Records).RequireAuthorization("client-read");
    }

    private static async Task<IResult> List(HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var scope=new PartyScope(actor);
        var page=paging.Read(context,actor,"reference,id","q","entityType");
        var search=context.Request.Query["q"].ToString();var entity=context.Request.Query["entityType"].ToString();
        if(page is null || search.Length>200 || search.Any(char.IsControl) || (search.Length>0 && string.IsNullOrWhiteSpace(search)) ||
            (entity.Length>0 && entity is not ("sole-trader" or "partnership" or "limited-company" or "llp"))) return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var query=scope.Clients(db).Where(x => x.CreatedAt<=page.AsOf);
        if(search.Length>0)
        {
            var term=ClientIdentity.NormalizeName(search);
            // Contains is translated with literal wildcard escaping by the SQL provider.
            query=query.Where(x => x.NormalizedName.Contains(term) || x.Reference.Contains(term) || (x.CompanyNumber!=null && x.CompanyNumber.Contains(term)));
        }
        if(entity.Length>0) query=query.Where(x => x.EntityType==entity);
        var total=await query.CountAsync(context.RequestAborted);
        var rows=await query.OrderBy(x => x.Reference).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        var ids=rows.Select(x => x.Id).ToArray();
        var agencies=await (from r in scope.Relationships(db) join a in scope.Agencies(db) on r.AgencyId equals a.Id
            where ids.Contains(r.ClientId) select new {r.ClientId,a.Id,Name=a.LegalName,a.Reference}).ToListAsync(context.RequestAborted);
        return Results.Json(new {items=rows.Select(x => new {x.Id,x.Reference,x.LegalName,x.EntityType,x.CompanyNumber,
            Address=Address(x),x.CreatedAt,x.IdentityState,agencies=agencies.Where(a => a.ClientId==x.Id).OrderBy(a => a.Name).Select(a => new {a.Id,a.Name,a.Reference}),records=new {state="unavailable"}}),
            totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},Json);
    }

    private static async Task<IResult> Get(Guid clientId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var row=await Scope(context).Clients(db).SingleOrDefaultAsync(x => x.Id==clientId,context.RequestAborted);
        if(row is null) return Missing(context);
        context.Response.Headers.ETag=Etag(row.RowVersion);return Results.Json(View(row),Json);
    }

    private static async Task<IResult> Relationships(Guid clientId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var scope=new PartyScope(actor);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        if(!await scope.Clients(db).AnyAsync(x => x.Id==clientId,context.RequestAborted)) return Missing(context);
        var page=paging.Read(context,actor,"id");if(page is null) return BadQuery(context);
        var query=from r in scope.Relationships(db) join a in scope.Agencies(db) on r.AgencyId equals a.Id
            where r.ClientId==clientId && r.CreatedAt<=page.AsOf select new {r.Id,r.ClientId,r.AgencyId,r.State,AgencyName=a.LegalName,AgencyReference=a.Reference};
        var total=await query.CountAsync(context.RequestAborted);
        var items=await query.OrderBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        return Results.Json(new {items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},Json);
    }

    private static async Task<IResult> Relationship(Guid relationshipId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);var scope=Scope(context);
        var row=await scope.Relationships(db).SingleOrDefaultAsync(x => x.Id==relationshipId,context.RequestAborted);
        if(row is null) return Missing(context);
        var agency=await scope.Agencies(db).SingleOrDefaultAsync(x => x.Id==row.AgencyId,context.RequestAborted);
        if(agency is null) return Missing(context);
        context.Response.Headers.ETag=Etag(row.RowVersion);return Results.Json(RelationshipView(row,agency),Json);
    }

    private static async Task<IResult> Agencies(HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var page=paging.Read(context,actor,"reference,id");if(page is null)return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var query=new PartyScope(actor).Agencies(db).Where(x => x.CreatedAt<=page.AsOf);
        var total=await query.CountAsync(context.RequestAborted);
        var items=await query.OrderBy(x => x.Reference).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).Select(x => new {x.Id,x.Reference,x.LegalName,x.State}).ToListAsync(context.RequestAborted);
        return Results.Json(new {items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},Json);
    }

    private static async Task<IResult> Activity(Guid clientId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var scope=new PartyScope(actor);
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        if(!await scope.Clients(db).AnyAsync(x => x.Id==clientId,context.RequestAborted))return Missing(context);
        var page=paging.Read(context,actor,"occurredAt-desc,id");if(page is null)return BadQuery(context);
        // Only explicitly supported events are published. Future sensitive activity needs its own safe mapping.
        var query=scope.Activity(db).Where(x => x.ClientId==clientId && x.OccurredAt<=page.AsOf &&
            (x.EventType=="client.demo-created" || x.EventType=="client.created" || x.EventType=="client.updated" || x.EventType=="client.relationship-created"));
        var total=await query.CountAsync(context.RequestAborted);
        var rows=await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        return Results.Json(new {items=rows.Select(x => new {x.Id,x.OccurredAt,actorLabel=x.ActorId is null ? "System" : "Back office staff",x.EventType,
            summary=x.EventType switch {"client.updated"=>"Client identity updated.","client.relationship-created"=>"Agency relationship added.",_=>"Client identity created."},
            x.RelationshipId,recordId=x.RecordKind=="client" && x.RecordId==clientId ? (Guid?)clientId : null,
            recordKind=x.RecordKind=="client" && x.RecordId==clientId ? "client" : null}),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},Json);
    }

    private static async Task<IResult> Records(Guid clientId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        return await Scope(context).Clients(db).AnyAsync(x => x.Id==clientId,context.RequestAborted)
            ? IdentityEndpoints.Problem(context,503,"client-records-unavailable","Quote and policy records are not available yet.") : Missing(context);
    }

    private static PartyScope Scope(HttpContext context)=>new(LocalIdentityService.Actor(context.User));
    private static AddressWrite Address(ClientAccount row)=>JsonSerializer.Deserialize<AddressWrite>(row.Address,Json)!;
    private static object View(ClientAccount row)=>new {row.Id,row.Reference,row.LegalName,row.EntityType,row.CompanyNumber,Address=Address(row),row.CreatedAt,row.IdentityState};
    private static object RelationshipView(ClientAgencyRelationship row,Agency agency)=>new {row.Id,row.ClientId,row.AgencyId,row.State,agencyName=agency.LegalName,agencyReference=agency.Reference};
    private static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"client-record-not-found","Record not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Refresh the list and use supported filters.");
}
