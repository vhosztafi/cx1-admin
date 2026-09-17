using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class QuoteIssueEndpoints
{
    public static void MapQuoteIssue(this WebApplication app) => app.MapPost("/api/v1/quotes/{quoteId:guid}/issue", Issue).RequireAuthorization("policy-issue-within-authority");
    private static async Task<IResult> Issue(Guid quoteId, HttpContext context, QuoteIssueService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "ratingId", "acceptanceId", "termsHash", "assuranceHash", "reason");
            var input = new QuoteIssueInput(QuoteHttpInput.Id(root, "cycleId"), QuoteHttpInput.Id(root, "ratingId"), QuoteHttpInput.Id(root, "acceptanceId"),
                QuoteReferralEndpoints.Text(root, "termsHash", 64), QuoteReferralEndpoints.Text(root, "assuranceHash", 64), QuoteReferralEndpoints.Text(root, "reason", 1000));
            var result = await service.IssueAsync(LocalIdentityService.Actor(context.User), quoteId, version, input, key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/policies/{result.ResourceId:D}"; return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
