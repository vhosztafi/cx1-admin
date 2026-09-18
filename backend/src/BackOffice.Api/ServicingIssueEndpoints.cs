using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class ServicingIssueEndpoints
{
    public static void MapServicingIssue(this WebApplication app) =>
        app.MapPost("/api/v1/drafts/{draftId:guid}/issue", Issue).RequireAuthorization("policy-issue-within-authority");

    private static async Task<IResult> Issue(Guid draftId,HttpContext context,ServicingIssueService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(draftId); QuoteHttpInput.NoQuery(context.Request);
            var version=QuoteHttpInput.Version(context.Request); var lease=ServicingEndpoints.Fence(context.Request);
            var key=QuoteHttpInput.Key(context.Request); var token=context.RequestAborted;
            using var document=await QuoteHttpInput.Read(context.Request,token,16384); var root=document.RootElement;
            QuoteHttpInput.Keys(root,"cycleId","ratingId","termsVersionId","acceptanceId","termsHash","assuranceHash","reason");
            var outcome=await service.IssueAsync(LocalIdentityService.Actor(context.User),draftId,version,lease,
                new(QuoteHttpInput.Id(root,"cycleId"),QuoteHttpInput.Id(root,"ratingId"),QuoteHttpInput.Id(root,"termsVersionId"),
                    QuoteHttpInput.Id(root,"acceptanceId"),QuoteReferralEndpoints.Text(root,"termsHash",64),
                    QuoteReferralEndpoints.Text(root,"assuranceHash",64),QuoteReferralEndpoints.Text(root,"reason",1000)),key,Guid.NewGuid(),token);
            context.Response.Headers.ETag=outcome.Etag;
            return Results.Content(outcome.Body,"application/json",statusCode:outcome.Status);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context,error); }
    }
}
