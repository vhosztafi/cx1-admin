using System.Data;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class QuoteDiscoveryEndpoints
{
    public static void MapQuoteDiscovery(this WebApplication app) =>
        app.MapGet("/api/v1/quotes", List).RequireAuthorization("quote-read");

    private static async Task<IResult> List(HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging)
    {
        var token = context.RequestAborted; var actor = LocalIdentityService.Actor(context.User); var query = context.Request.Query;
        var search = query["q"].ToString(); var product = query["productCode"].ToString(); var state = query["status"].ToString();
        var sort = query["sort"].ToString(); if (sort.Length == 0) sort = "reference";
        var direction = query["direction"].ToString(); if (direction.Length == 0) direction = "asc";
        Guid? clientId = null, agencyId = null;
        bool Identifier(string key, out Guid? id)
        {
            id = null; if (!query.ContainsKey(key)) return true;
            if (!Guid.TryParseExact(query[key], "D", out var value) || value == Guid.Empty) return false;
            id = value; return true;
        }
        if (search.Length > 200 || search.Any(char.IsControl) || search.Length > 0 && string.IsNullOrWhiteSpace(search) ||
            product.Length > 0 && product is not ("motor-trade-road-risks" or "motor-trade-combined") ||
            state.Length > 0 && state is not ("draft" or "rating-pending" or "rated" or "referred" or "approved" or "sent" or "accepted" or "declined" or "bound" or "withdrawn") || sort is not ("reference" or "updated" or "start") ||
            direction is not ("asc" or "desc") || !Identifier("clientId", out clientId) || !Identifier("agencyId", out agencyId)) return BadQuery(context);
        try
        {
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await QuoteDiscovery.AuthorizeAsync(db, actor, token);
            var page = paging.ReadBound(context, actor, sort + ":" + direction, await QuoteDiscovery.ListVersionAsync(db, token), "q", "productCode", "status", "sort", "direction", "clientId", "agencyId");
            if (page is null) return BadQuery(context);
            var rows = QuoteDiscovery.Search(db, QuoteDiscovery.Rows(db), search);
            if (clientId is not null) rows = rows.Where(x => x.ClientId == clientId);
            if (agencyId is not null) rows = rows.Where(x => x.AgencyId == agencyId);
            if (product.Length > 0) rows = rows.Where(x => x.ProductCode == product);
            if (state.Length > 0) rows = rows.Where(x => x.State == state);
            var count = await rows.CountAsync(token);
            var items = await QuoteDiscovery.Order(rows, sort, direction == "desc").Skip(page.Offset).Take(page.Size).ToListAsync(token);
            await transaction.CommitAsync(token);
            return Results.Json(new { items, totalCount = count, nextCursor = paging.Next(page, page.Offset + items.Count < count) }, ClientEndpoints.Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static IResult BadQuery(HttpContext context) => IdentityEndpoints.Problem(context, 400, "invalid-query", "Refresh the quote list and use supported filters.");
}
