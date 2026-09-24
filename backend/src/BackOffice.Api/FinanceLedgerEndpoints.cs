using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceLedgerEndpoints
{
    public static void MapFinanceLedger(this WebApplication app)
    {
        app.MapGet("/api/v1/finance/ledger", List).RequireAuthorization("finance-read");
        app.MapGet("/api/v1/finance/accounts/{agencyId:guid}", Account).RequireAuthorization("finance-read");
        app.MapGet("/api/v1/finance/transactions/{transactionId:guid}", Transaction).RequireAuthorization("finance-read");
        // Policy staff use their current policy scope; finance users use current finance scope.
        app.MapGet("/api/v1/policies/{policyId:guid}/finance", Policy).RequireAuthorization();
    }

    private static async Task<IResult> List(HttpContext context, FinanceLedgerService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var query = context.Request.Query;
            if (query.Keys.Except(["agencyId", "relationshipId", "from", "to", "page", "pageSize"], StringComparer.Ordinal).Any() ||
                !Guid.TryParse(query["agencyId"], out var agencyId) || agencyId == Guid.Empty)
                throw new QuoteOperationException(400, "finance-query-invalid");
            Guid? relationship = null;
            if (query.ContainsKey("relationshipId"))
            {
                if (!Guid.TryParse(query["relationshipId"], out var parsed) || parsed == Guid.Empty)
                    throw new QuoteOperationException(400, "finance-query-invalid");
                relationship = parsed;
            }
            DateOnly? Date(string name)
            {
                if (!query.ContainsKey(name)) return null;
                if (!DateOnly.TryParseExact(query[name], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date)) throw new QuoteOperationException(400, "finance-query-invalid");
                return date;
            }
            int Number(string name, int fallback)
            {
                if (!query.ContainsKey(name)) return fallback;
                if (!int.TryParse(query[name], out var number)) throw new QuoteOperationException(400, "finance-query-invalid");
                return number;
            }
            return Results.Json(await service.ListAsync(LocalIdentityService.Actor(context.User), agencyId, relationship,
                Date("from"), Date("to"), Number("page", 1), Number("pageSize", 50), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Account(Guid agencyId, HttpContext context, FinanceLedgerService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Keys.Except(["relationshipId"], StringComparer.Ordinal).Any()) throw new QuoteOperationException(400, "finance-query-invalid");
            Guid? relationship = null;
            if (context.Request.Query.ContainsKey("relationshipId"))
            {
                if (!Guid.TryParse(context.Request.Query["relationshipId"], out var id) || id == Guid.Empty)
                    throw new QuoteOperationException(400, "finance-query-invalid");
                relationship = id;
            }
            return Results.Json(await service.AccountAsync(LocalIdentityService.Actor(context.User), agencyId, relationship, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Transaction(Guid transactionId, HttpContext context, FinanceLedgerService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "finance-query-invalid");
            return Results.Json(await service.TransactionAsync(LocalIdentityService.Actor(context.User), transactionId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Policy(Guid policyId, HttpContext context, FinanceLedgerService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "finance-query-invalid");
            return Results.Json(await service.PolicyAsync(LocalIdentityService.Actor(context.User), policyId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
