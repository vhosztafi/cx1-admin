using System.Data;
using System.Text.Json;
using BackOffice.Infrastructure.Quotes;
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
        var query=scope.SearchClients(db,search.Length>0 ? ClientIdentity.NormalizeName(search) : "").Where(x => x.CreatedAt<=page.AsOf);
        if(entity.Length>0) query=query.Where(x => x.EntityType==entity);
        var total=await query.CountAsync(context.RequestAborted);
        var rows=await query.OrderBy(x => x.Reference).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        var ids=rows.Select(x => x.Id).ToArray();
        var agencies=await (from r in scope.Relationships(db) join a in scope.Agencies(db) on r.AgencyId equals a.Id
            where ids.Contains(r.ClientId) select new {r.ClientId,a.Id,Name=a.LegalName,a.Reference}).ToListAsync(context.RequestAborted);
        var primaries=await scope.Contacts(db).Where(x=>ids.Contains(x.ClientId) && x.IsPrimary).Select(x=>new {x.ClientId,x.DeclaredFullName}).ToListAsync(context.RequestAborted);
        var quoteCounts=actor.HasCapability("quote-read") ? await db.Set<Quote>().AsNoTracking().Where(x=>ids.Contains(x.ClientId) && x.CurrentRevisionId!=null).GroupBy(x=>x.ClientId).Select(x=>new {Id=x.Key,Count=x.Count()}).ToDictionaryAsync(x=>x.Id,x=>x.Count,context.RequestAborted) : new Dictionary<Guid,int>();
        return Results.Json(new {items=rows.Select(x => new {x.Id,x.Reference,x.LegalName,x.EntityType,x.CompanyNumber,
            primaryContactName=agencies.Count(a=>a.ClientId==x.Id)==1 ? primaries.SingleOrDefault(c=>c.ClientId==x.Id)?.DeclaredFullName : null,
            Address=Address(x),x.CreatedAt,x.IdentityState,agencies=agencies.Where(a => a.ClientId==x.Id).OrderBy(a => a.Name).Select(a => new {a.Id,a.Name,a.Reference}),records=actor.HasCapability("quote-read") ? (object)new {state="partial",quoteCount=quoteCounts.GetValueOrDefault(x.Id),policyState="unavailable"} : new {state="unavailable"}}),
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
        var support=actor.HasCapability("support-internal-read");
        var matchRead=actor.HasCapability("match-read");
        var quoteRead=actor.HasCapability("quote-read");
        // Only explicitly supported events are published. Future sensitive activity needs its own safe mapping.
        var query=scope.Activity(db).Where(x => x.ClientId==clientId && x.OccurredAt<=page.AsOf &&
            (x.EventType=="client.demo-created" || x.EventType=="client.created" || x.EventType=="client.updated" || x.EventType=="client.relationship-created" ||
             x.EventType=="contact.created" || x.EventType=="contact.updated" || x.EventType=="contact.primary-changed" || x.EventType=="contact.ended" ||
             (support && (x.EventType=="support-flag.created" || x.EventType=="support-flag.amended" || x.EventType=="support-flag.reviewed" || x.EventType=="support-flag.ended")) ||
             (matchRead && (x.EventType=="match.link" || x.EventType=="match.separate" || x.EventType=="match.decline" || x.EventType=="match.query" || x.EventType=="match.reopen" || x.EventType=="match.created")) ||
             (quoteRead && (x.EventType=="quote.created" || x.EventType=="quote.saved" || x.EventType=="quote.withdrawn" || x.EventType=="quote.reassociated"))));
        var total=await query.CountAsync(context.RequestAborted);
        var rows=await query.OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
        var actorIds=rows.Where(x=>x.ActorId!=null).Select(x=>x.ActorId!.Value).Distinct().ToArray();
        var actors=await db.Set<StaffUser>().AsNoTracking().Where(x=>actorIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,context.RequestAborted);
        var contactIds=rows.Where(x=>x.RecordKind=="contact" && x.RecordId!=null).Select(x=>x.RecordId!.Value).ToArray();
        var contacts=await scope.Contacts(db,includeEnded:true).Where(x=>x.ClientId==clientId && contactIds.Contains(x.Id))
            .Select(x=>new {x.Id,x.RelationshipId}).ToListAsync(context.RequestAborted);
        var matchIds=rows.Where(x=>x.RecordKind=="match" && x.RecordId!=null).Select(x=>x.RecordId!.Value).ToArray();
        var matchScope=new MatchScope(actor);
        var matches=await matchScope.Reviews(db).Where(x=>matchIds.Contains(x.Id)).Select(x=>new {x.Id,x.CandidateClientId,x.CandidateRelationshipId}).ToListAsync(context.RequestAborted);
        var decisions=await matchScope.Decisions(db).Where(x=>matchIds.Contains(x.MatchId) && x.ClientId==clientId).Select(x=>new {x.MatchId,x.RelationshipId}).ToListAsync(context.RequestAborted);
        var quoteIds=rows.Where(x=>x.RecordKind=="quote" && x.RecordId!=null).Select(x=>x.RecordId!.Value).ToArray();
        var quotes=await db.Set<QuoteRevision>().AsNoTracking().Where(x=>quoteRead && quoteIds.Contains(x.QuoteId) && x.ClientId==clientId).Select(x=>new {Id=x.QuoteId,x.RelationshipId}).Distinct().ToListAsync(context.RequestAborted);
        var intakeMatches=matchRead ? await db.Set<MatchSubmission>().AsNoTracking().Where(x=>x.LinkedClientId==clientId).Select(x=>x.Id).ToListAsync(context.RequestAborted) : [];
        var linkedReviews=await matchScope.Reviews(db).Where(x=>intakeMatches.Contains(x.SubmissionId)).Select(x=>x.Id).ToListAsync(context.RequestAborted);
        bool CanLink(ClientActivity x)=>x.RecordKind=="quote" && quoteRead && x.RecordId is Guid quoteId && quotes.Any(q=>q.Id==quoteId && q.RelationshipId==x.RelationshipId) ||
            x.RecordKind=="match" && x.RecordId is Guid reviewId && linkedReviews.Contains(reviewId) ||x.RecordKind=="client" && x.RecordId==clientId ||
            x.RecordKind=="contact" && contacts.Any(c=>c.Id==x.RecordId && c.RelationshipId==x.RelationshipId) ||
            x.RecordKind=="match" && matches.Any(m=>m.Id==x.RecordId && (m.CandidateClientId==clientId && m.CandidateRelationshipId==x.RelationshipId || decisions.Any(d=>d.MatchId==m.Id && d.RelationshipId==x.RelationshipId)));
        return Results.Json(new {items=rows.Select(x => new {x.Id,x.OccurredAt,actorLabel=x.ActorId is Guid actorId ? actors.GetValueOrDefault(actorId,"Unavailable staff identity") : "System",x.EventType,
            summary=x.EventType switch {"client.updated"=>"Client identity updated.","client.relationship-created"=>"Agency relationship added.",
                "contact.created"=>"Relationship contact added.","contact.updated"=>"Relationship contact updated.",
                "contact.primary-changed"=>"Primary contact changed.","contact.ended"=>"Relationship contact ended.",
                "support-flag.created"=>"Support instruction recorded.","support-flag.amended"=>"Support instruction amended.",
                "support-flag.reviewed"=>"Support instruction reviewed.","support-flag.ended"=>"Support instruction ended.",
                "match.created"=>"Account matching review created.","match.link"=>"Intake linked to this client.","match.separate"=>"Intake recorded as a separate client.","match.decline"=>"Intake declined.",
                "match.query"=>"Match information request recorded.","match.reopen"=>"Match review reopened.",
                "quote.created"=>"Quote created.","quote.saved"=>"Quote saved.","quote.withdrawn"=>"Quote withdrawn.","quote.reassociated"=>"Quote account association updated.",_=>"Client identity created."},
            x.RelationshipId,recordId=CanLink(x) ? x.RecordId : null,
            recordKind=CanLink(x) ? x.RecordKind : null}),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},Json);
    }

    private static async Task<IResult> Records(Guid clientId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
        var kind=context.Request.Query["kind"].ToString();
        if(kind.Length>0 && kind is not ("quote" or "policy"))return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(token);
        if(!await Scope(context).Clients(db).AnyAsync(x=>x.Id==clientId,token))return Missing(context);
        if(kind=="policy")return IdentityEndpoints.Problem(context,503,"client-records-unavailable","Policy records are not available yet.");
        try
        {
            await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
            await QuoteDiscovery.AuthorizeAsync(db,actor,token);
            var page=paging.ReadBound(context,actor,"reference,id",await QuoteDiscovery.VersionAsync(db,token),"kind");
            if(page is null)return BadQuery(context);
            var rows=QuoteDiscovery.Rows(db).Where(x=>x.ClientId==clientId);
            var total=await rows.CountAsync(token);
            var items=await rows.OrderBy(x=>x.Reference).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size)
                .Select(x=>new {x.Id,kind="quote",x.Reference,x.RelationshipId,x.AgencyName,x.ProductCode,x.State}).ToListAsync(token);
            await transaction.CommitAsync(token);
            return Results.Json(new {items,totalCount=total,nextCursor=paging.Next(page,page.Offset+items.Count<total)},Json);
        }
        catch(Exception error)when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static PartyScope Scope(HttpContext context)=>new(LocalIdentityService.Actor(context.User));
    private static AddressWrite Address(ClientAccount row)=>JsonSerializer.Deserialize<AddressWrite>(row.Address,Json)!;
    private static object View(ClientAccount row)=>new {row.Id,row.Reference,row.LegalName,row.EntityType,row.CompanyNumber,Address=Address(row),row.CreatedAt,row.IdentityState};
    private static object RelationshipView(ClientAgencyRelationship row,Agency agency)=>new {row.Id,row.ClientId,row.AgencyId,row.State,agencyName=agency.LegalName,agencyReference=agency.Reference};
    private static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"client-record-not-found","Record not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Refresh the list and use supported filters.");
}
