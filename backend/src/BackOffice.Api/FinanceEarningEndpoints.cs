using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceEarningEndpoints
{
    public static void MapFinanceEarnings(this WebApplication app)
    {
        app.MapGet("/api/v1/finance/agencies/{agencyId:guid}/earned-premium", Review)
            .RequireAuthorization("finance-read");
    }

    private static async Task<IResult> Review(Guid agencyId, HttpContext context, FinanceEarningService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var query = context.Request.Query;
            if (query.Count != 1 || !query.TryGetValue("periodId", out var raw) || raw.Count != 1 ||
                !Guid.TryParse(raw, out var periodId) || periodId == Guid.Empty)
                throw new QuoteOperationException(400, "finance-earning-query-invalid");
            return Results.Json(await service.ReviewAsync(LocalIdentityService.Actor(context.User),
                agencyId, periodId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error))
        {
            return QuoteEndpoints.Failure(context, error);
        }
    }
}
