using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyTermsEndpoints
{
    public static void MapAgencyTerms(this WebApplication app)
    {
        app.MapPost("/api/v1/agencies/{agencyId:guid}/terms-requests",Propose).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/terms-requests",List).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agency-terms-requests/{requestId:guid}",Get).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agency-terms-requests/{requestId:guid}/decision",Decide).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/terms",Versions).RequireAuthorization("agency-read");
    }
    private static async Task<IResult> Propose(Guid agencyId,HttpContext context,AgencyTermsService service)
    {
        try
        {
            var body=await AgencyEndpoints.ReadBody(context);
            return Response(context,await service.Propose(LocalIdentityService.Actor(context.User),agencyId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),body,context.RequestAborted));
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Decide(Guid requestId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies,AgencyTermsService service)
    {
        try
        {
            var input=ClientEndpoints.Input<DecisionInput>(await AgencyEndpoints.ReadBody(context));if(input.Outcome is not ("approve" or "reject"))throw new AgencyCommandException(422,"invalid-decision");
            var actor=LocalIdentityService.Actor(context.User);await agencies.Authorize(actor,null,context.RequestAborted);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
            var request=await db.Set<AgencyTermsRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId,context.RequestAborted);if(request is null)return Missing(context);
            return Response(context,await service.Decide(actor,request.AgencyId,requestId,ClientEndpoints.Key(context),ClientEndpoints.Version(context),input.Outcome=="approve",input.Reason,context.RequestAborted));
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Get(Guid requestId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies)
    {
        try
        {
            if(context.Request.Query.Count>0)return BadQuery(context);await agencies.Authorize(LocalIdentityService.Actor(context.User),null,context.RequestAborted);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);var row=await db.Set<AgencyTermsRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId,context.RequestAborted);if(row is null)return Missing(context);
            var labels=await Labels(db,[row],context.RequestAborted);context.Response.Headers.ETag=AgencyDraftService.Etag(row.RowVersion);return Results.Json(RequestView(row,labels),ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> List(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies,PartyPaging paging)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);await agencies.Authorize(actor,agencyId,context.RequestAborted);
            var page=paging.Read(context,actor,"createdAt-desc,id");if(page is null)return BadQuery(context);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);var query=db.Set<AgencyTermsRequest>().AsNoTracking().Where(x=>x.AgencyId==agencyId&&x.CreatedAt<=page.AsOf);
            var total=await query.CountAsync(context.RequestAborted);var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(context.RequestAborted);
            var labels=await Labels(db,rows,context.RequestAborted);return Results.Json(new{items=rows.Select(x=>RequestView(x,labels)),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static async Task<IResult> Versions(Guid agencyId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        try
        {
            var page=paging.Read(context,LocalIdentityService.Actor(context.User),"version-desc,id");if(page is null)return BadQuery(context);
            await using var db=await factory.CreateDbContextAsync(context.RequestAborted);await using var transaction=await db.Database.BeginTransactionAsync(context.RequestAborted);
            // The same parent update lock as publication keeps metadata and versions coherent.
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,ROWLOCK) WHERE Id={agencyId}").SingleOrDefaultAsync(context.RequestAborted);if(agency is null)return Missing(context);
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(page.AsOf,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            var query=db.Set<AgencyTermsVersion>().AsNoTracking().Where(x=>x.AgencyId==agencyId&&x.CreatedAt<=page.AsOf);var total=await query.CountAsync(context.RequestAborted);
            var current=await query.Where(x=>x.EffectiveFrom<=today).OrderByDescending(x=>x.EffectiveFrom).ThenByDescending(x=>x.Version).Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(context.RequestAborted);
            var rows=await query.OrderByDescending(x=>x.Version).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size)
                .Select(x=>new{Row=x,End=query.Where(n=>n.EffectiveFrom>x.EffectiveFrom).Min(n=>(DateOnly?)n.EffectiveFrom)}).ToListAsync(context.RequestAborted);
            var items=rows.Select(x=>Fields(new{x.Row.Id,x.Row.AgencyId,x.Row.Version,approvedRequestId=x.Row.ApprovedStateRequestId??x.Row.ApprovedTermsRequestId,
                approvedRequestKind=x.Row.ApprovedStateRequestId!=null?"activation":"terms",effectiveTo=x.End,x.Row.CreatedAt,status=x.Row.EffectiveFrom>today?"scheduled":x.Row.Id==current?"current":"historical"},x.Row.Snapshot)).ToList();
            context.Response.Headers.ETag=AgencyDraftService.Etag(agency.RowVersion);await transaction.CommitAsync(context.RequestAborted);
            return Results.Json(new{items,totalCount=total,asOf=page.AsOf,nextCursor=paging.Next(page,page.Offset+items.Count<total)},ClientEndpoints.Json);
        }
        catch(Exception ex)when(IsError(ex)){return Error(context,ex);}
    }
    private static JsonObject RequestView(AgencyTermsRequest row,IReadOnlyDictionary<Guid,string> labels)=>Fields(new
    {
        row.Id,row.AgencyId,row.RequestedBy,requestedByLabel=labels[row.RequestedBy],reason=row.RequestReason,baseVersion=AgencyDraftService.Etag(row.BaseVersion),inputFingerprint=row.ProposedInputFingerprint,
        row.CreatedAt,row.State,row.DecisionBy,decisionByLabel=row.DecisionBy is Guid actor?labels[actor]:null,row.DecisionReason,row.DecidedAt,etag=AgencyDraftService.Etag(row.RowVersion)
    },row.ProposedSnapshot);
    private static JsonObject Fields(object metadata,string snapshot)
    {
        using var document=JsonDocument.Parse(snapshot);var terms=AgencyTermsRules.ReadPublished(document.RootElement);using var normalized=JsonDocument.Parse(terms.SnapshotJson);
        var result=JsonSerializer.SerializeToNode(metadata,ClientEndpoints.Json)!.AsObject();
        foreach(var key in new[]{"effectiveFrom","commercialTerms","settlement","paymentTermsDays","creditLimit","products"})result[key]=JsonNode.Parse(normalized.RootElement.GetProperty(key).GetRawText());
        return result;
    }
    private static async Task<Dictionary<Guid,string>> Labels(BackOfficeDbContext db,IReadOnlyList<AgencyTermsRequest> rows,CancellationToken token)
    {var ids=rows.Select(x=>x.RequestedBy).Concat(rows.Where(x=>x.DecisionBy!=null).Select(x=>x.DecisionBy!.Value)).Distinct().ToArray();return await db.Set<StaffUser>().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,token);}
    private static IResult Response(HttpContext context,CommandOutcome result){context.Response.Headers.ETag=result.Etag;context.Response.Headers.Location="/api/v1/agency-terms-requests/"+result.ResourceId;return Results.Content(result.Body,"application/json",statusCode:result.Status);}
    private static bool IsError(Exception ex)=>ex is AgencyCommandException||ClientEndpoints.IsCommandError(ex);
    private static IResult Error(HttpContext context,Exception ex)=>ex is AgencyCommandException agency?IdentityEndpoints.Problem(context,agency.Status,agency.Code,"Check the agreed terms and current proposal."):ClientEndpoints.CommandError(context,ex,"agency-terms");
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"agency-terms-request-not-found","Agency or terms request not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Use supported terms pagination.");
    private sealed record DecisionInput([property:JsonRequired]string Outcome,[property:JsonRequired]string Reason);
}
