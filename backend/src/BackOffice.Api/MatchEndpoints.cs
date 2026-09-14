using System.Text.Json;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class MatchEndpoints
{
    public static void MapMatches(this WebApplication app)
    {
        app.MapGet("/api/v1/matches",List).RequireAuthorization("match-read");
        app.MapGet("/api/v1/matches/{matchId:guid}",Get).RequireAuthorization("match-read");
        app.MapGet("/api/v1/matches/{matchId:guid}/decisions",Decisions).RequireAuthorization("match-read");
        app.MapGet("/api/v1/matches/{matchId:guid}/information-requests",Requests).RequireAuthorization("match-read");
        app.MapPost("/api/v1/matches/{matchId:guid}/decisions",Decide).RequireAuthorization("match-review");
    }
    private static async Task<IResult> List(HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
        var page=paging.Read(context,actor,"createdAt-desc,id","state","candidateClientId");if(page is null)return BadQuery(context);
        var state=context.Request.Query["state"].ToString();var candidate=context.Request.Query["candidateClientId"].ToString();Guid candidateId=default;
        if(state.Length>0 && state is not ("pending" or "queried" or "linked" or "separate" or "declined") || candidate.Length>0 && (!Guid.TryParseExact(candidate,"D",out candidateId) || candidateId==Guid.Empty))return BadQuery(context);
        await using var db=await factory.CreateDbContextAsync(token);
        var query=new MatchScope(actor).Reviews(db).Where(x=>x.CreatedAt<=page.AsOf);
        if(state.Length>0)query=query.Where(x=>x.State==state);if(candidate.Length>0)query=query.Where(x=>x.CandidateClientId==candidateId);
        var total=await query.CountAsync(token);var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(token);
        return Results.Json(new {items=await Views(db,rows,token),totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
    }
    private static async Task<IResult> Get(Guid matchId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory)
    {
        await using var db=await factory.CreateDbContextAsync(context.RequestAborted);
        var row=await new MatchScope(LocalIdentityService.Actor(context.User)).Reviews(db).SingleOrDefaultAsync(x=>x.Id==matchId,context.RequestAborted);
        if(row is null)return Missing(context);context.Response.Headers.ETag=Etag(row.RowVersion);
        return Results.Json((await Views(db,[row],context.RequestAborted)).Single(),ClientEndpoints.Json);
    }
    private static async Task<object[]> Views(BackOfficeDbContext db,IReadOnlyCollection<MatchReview> rows,CancellationToken token)
    {
        var ids=rows.Select(x=>x.SubmissionId).ToArray();var submissions=await db.Set<MatchSubmission>().AsNoTracking().Where(x=>ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        var agencyIds=submissions.Values.Select(x=>x.AgencyId).ToArray();var agencies=await db.Set<Agency>().AsNoTracking().Where(x=>agencyIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,token);
        return rows.Select(row=>
        {
            var s=submissions[row.SubmissionId];return (object)new {row.Id,row.SubmissionId,row.CandidateClientId,row.Confidence,row.State,row.RuleVersionId,
                signals=JsonSerializer.Deserialize<MatchSignal[]>(row.Signals,ClientEndpoints.Json),rule=JsonSerializer.Deserialize<MatchRuleSnapshot>(row.RuleSnapshot,ClientEndpoints.Json),
                submission=new {s.Id,s.Reference,s.AgencyId,agencyName=agencies[s.AgencyId].LegalName,identity=JsonSerializer.Deserialize<ClientWrite>(s.IdentitySnapshot,ClientEndpoints.Json),s.CreatedAt,s.LinkedClientId,s.LinkedRelationshipId}};
        }).ToArray();
    }
    private static Task<IResult> Decisions(Guid matchId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)=>Trail(matchId,false,context,factory,paging);
    private static Task<IResult> Requests(Guid matchId,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)=>Trail(matchId,true,context,factory,paging);
    private static async Task<IResult> Trail(Guid matchId,bool requests,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,PartyPaging paging)
    {
        var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;var scope=new MatchScope(actor);
        await using var db=await factory.CreateDbContextAsync(token);if(!await scope.Reviews(db).AnyAsync(x=>x.Id==matchId,token))return Missing(context);
        var page=paging.Read(context,actor,"occurredAt-desc,id");if(page is null)return BadQuery(context);
        if(requests)
        {
            var query=scope.InformationRequests(db).Where(x=>x.MatchId==matchId && x.RecordedAt<=page.AsOf);var total=await query.CountAsync(token);
            var rows=await query.OrderByDescending(x=>x.RecordedAt).ThenBy(x=>x.Id).Skip(page.Offset).Take(page.Size).Select(x=>new {x.Id,x.MatchId,x.Description,x.RecordedAt,x.DeliveryState}).ToListAsync(token);
            return Results.Json(new {items=rows,totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
        }
        else
        {
            var query=scope.Decisions(db).Where(x=>x.MatchId==matchId && x.OccurredAt<=page.AsOf);var total=await query.CountAsync(token);
            var rows=await (from decision in query join user in db.Set<StaffUser>() on decision.ActorId equals user.Id
                orderby decision.OccurredAt descending,decision.Id
                select new {decision.Id,decision.MatchId,decision.Outcome,decision.Reason,actorLabel=user.DisplayName,decision.OccurredAt,decision.ClientId,decision.RelationshipId,decision.InformationRequestId})
                .Skip(page.Offset).Take(page.Size).ToListAsync(token);
            return Results.Json(new {items=rows,totalCount=total,nextCursor=paging.Next(page,page.Offset+rows.Count<total)},ClientEndpoints.Json);
        }
    }
    private static async Task<IResult> Decide(Guid matchId,JsonElement input,HttpContext context,IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time)
    {
        try
        {
            var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;var fields=MatchRules.Validate(ClientEndpoints.Input<MatchDecisionWrite>(input));
            await using var read=await factory.CreateDbContextAsync(token);await MatchService.AuthorizeAsync(read,actor,matchId,fields,token);
            var key=ClientEndpoints.Key(context);var version=ClientEndpoints.Version(context);var route=$"/api/v1/matches/{matchId}/decisions";
            var result=await commands.ExecuteAsync(new CommandIdentity(actor.UserId,route,key,Guid.NewGuid()),fields,"match."+fields.Outcome,async(db,ct)=>
            {
                var review=await MatchService.DecideAsync(db,actor,matchId,version,fields,time.GetUtcNow(),ct);
                return new CommandOutcome(review.Id,200,JsonSerializer.Serialize(new {id=review.Id}),Etag:Etag(review.RowVersion));
            },token);
            context.Response.Headers.ETag=result.Etag;return Results.Content(result.Body,"application/json",statusCode:result.Status);
        }
        catch(MatchTransitionException){return IdentityEndpoints.Problem(context,409,"invalid-match-transition","Reload the review and choose an available decision.");}
        catch(MatchOperationException error){return IdentityEndpoints.Problem(context,error.Status,error.Code,"Reload the review and check the requested decision.");}
        catch(Exception error) when(ClientEndpoints.IsCommandError(error)){return ClientEndpoints.CommandError(context,error,"match");}
    }
    private static string Etag(byte[] version)=>"\""+Convert.ToBase64String(version)+"\"";
    private static IResult Missing(HttpContext context)=>IdentityEndpoints.Problem(context,404,"match-not-found","Review not found.");
    private static IResult BadQuery(HttpContext context)=>IdentityEndpoints.Problem(context,400,"invalid-query","Use supported review filters and paging options.");
}
