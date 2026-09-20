using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;
using System.Globalization;

namespace BackOffice.Api;

public static class QuoteIssueEndpoints
{
    public static void MapQuoteIssue(this WebApplication app) => app.MapPost("/api/v1/quotes/{quoteId:guid}/issue", Issue).RequireAuthorization("policy-issue-within-authority");
    private static async Task<IResult> Issue(Guid quoteId, HttpContext context, QuoteIssueService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "ratingId", "acceptanceId", "termsHash", "assuranceHash", "reason");
            var input = new QuoteIssueInput(QuoteHttpInput.Id(root, "cycleId"), QuoteHttpInput.Id(root, "ratingId"), QuoteHttpInput.Id(root, "acceptanceId"),
                QuoteReferralEndpoints.Text(root, "termsHash", 64), QuoteReferralEndpoints.Text(root, "assuranceHash", 64), QuoteReferralEndpoints.Text(root, "reason", 1000));
            var result = await service.IssueAsync(LocalIdentityService.Actor(context.User), quoteId, version, input, key, Guid.NewGuid(), context.RequestAborted);
            var response = QuoteEndpoints.Outcome(context, result);
            context.Response.Headers.Location = $"/api/v1/policies/{result.ResourceId:D}"; return response;
        }
        catch (CommercialExposureConflictException error)
        {
            string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
            var blocked = error.Assessment.Intervals.Where(x => x.Blocker is not null).ToArray();
            var intervals = blocked.Take(100).Select(row => {
                var item = new Dictionary<string, object> { ["district"] = row.District, ["startsAt"] = row.From, ["endsAt"] = row.To,
                    ["ownSumInsured"] = Money(row.ProposedPropertySum), ["otherSumInsured"] = Money(row.OtherPropertySum),
                    ["resultingSumInsured"] = Money(row.ResultingPropertySum), ["policyCount"] = row.PolicyCount, ["code"] = row.Blocker! };
                if (row.Limit is decimal limit) {
                    var effective = Math.Min(limit, error.AuthorityLimit);
                    item["publishedLimit"] = Money(limit); item["effectiveLimit"] = Money(effective); item["headroom"] = Money(effective - row.ResultingPropertySum);
                    item["limitVersionId"] = row.LimitVersionId!.Value; item["limitHash"] = row.LimitHash!;
                }
                return item;
            }).ToArray();
            return Results.Problem(type: "about:blank", statusCode: 409, title: "The policy was not issued because district capacity is unavailable.",
                extensions: new Dictionary<string, object?> { ["code"] = blocked[0].Blocker, ["traceId"] = context.TraceIdentifier,
                    ["commercialCapacity"] = new { format = "commercial-issue-capacity-1", bookId = error.BookId, observedAt = error.AssessedAt,
                        authorityLimit = Money(error.AuthorityLimit), intervals, truncated = blocked.Length > intervals.Length } });
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
