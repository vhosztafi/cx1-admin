using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class ServicingSubmissionEndpoints
{
    public static void MapServicingSubmissions(this WebApplication app)
    {
        app.MapPost("/api/v1/drafts/{draftId:guid}/submit",Submit).RequireAuthorization("policy-draft-write");
        app.MapGet("/api/v1/drafts/{draftId:guid}/submissions",History).RequireAuthorization("policy-read");
    }

    private static async Task<IResult> Submit(Guid draftId,HttpContext context,ServicingSubmissionService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);QuoteHttpInput.NoQuery(context.Request);var token=context.RequestAborted;
            var key=QuoteHttpInput.Key(context.Request);var version=QuoteHttpInput.Version(context.Request);
            var lease=ServicingEndpoints.Fence(context.Request);
            using var document=await QuoteHttpInput.Read(context.Request,token,16384);var root=document.RootElement;
            QuoteHttpInput.Keys(root,"cycleId","revisionId","reason");
            var outcome=await service.SubmitAsync(LocalIdentityService.Actor(context.User),draftId,QuoteHttpInput.Id(root,"cycleId"),
                QuoteHttpInput.Id(root,"revisionId"),version,lease,QuoteReferralEndpoints.Text(root,"reason",2000),key,Guid.NewGuid(),token);
            context.Response.Headers.ETag=outcome.Etag;
            return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }

    private static async Task<IResult> History(Guid draftId,HttpContext context,ServicingSubmissionService service,
        ServicingEvidenceService evidence,PartyPaging paging)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId);var actor=LocalIdentityService.Actor(context.User);var token=context.RequestAborted;
            var version=await evidence.HistoryVersionAsync(actor,draftId,token);
            var page=paging.ReadBound(context,actor,"servicing-submissions",version);
            if(page is null || page.Size>50)throw new QuoteHttpException(400,"invalid-query");
            var history=await service.ReadAsync(actor,draftId,page.KeyId,page.Size,token);
            if(history.DraftEtag!=version || await evidence.HistoryVersionAsync(actor,draftId,token)!=version)
                throw new QuoteHttpException(409,"stale-cursor");
            return Results.Json(new {history.DraftId,history.DraftEtag,history.AssessedAt,history.CurrentCycleId,history.Current,
                history.Items,nextCursor=paging.NextGuid(page,history.NextBeforeId)});
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
