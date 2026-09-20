using System.Data;
using System.Globalization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;
public static partial class PolicyEndpoints
{
    internal static async Task<IResult> List(HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging,
        PolicyDiscoveryService service, TimeProvider time, Guid? fixedClientId = null)
    {
        var query = context.Request.Query; var actor = LocalIdentityService.Actor(context.User); var token = context.RequestAborted;
        var search = query["q"].ToString(); var product = query["productCode"].ToString(); var state = query["state"].ToString(); var registration = query["registration"].ToString();
        var sort = query["sort"].ToString(); if (sort.Length == 0) sort = "reference";
        var direction = query["direction"].ToString(); if (direction.Length == 0) direction = "asc";
        bool Id(string key, out Guid? id) { id = null; if (!query.ContainsKey(key)) return true; if (!Guid.TryParseExact(query[key], "D", out var parsed) || parsed == Guid.Empty) return false; id = parsed; return true; }
        bool Date(string key, out DateOnly? date) { date = null; if (!query.ContainsKey(key)) return true; if (!DateOnly.TryParseExact(query[key], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false; date = parsed; return true; }
        if (search.Length > 200 || search.Any(char.IsControl) || search.Length > 0 && string.IsNullOrWhiteSpace(search) ||
            product.Length > 0 && product is not ("motor-trade-road-risks" or "motor-trade-combined" or "commercial-combined") || state.Length > 0 && state is not ("scheduled" or "active" or "expired" or "cancelled") ||
            sort is not ("reference" or "inception" or "issued") || direction is not ("asc" or "desc") ||
            !Id("clientId", out var clientId) || !Id("agencyId", out var agencyId) || !Date("inceptionFrom", out var from) || !Date("inceptionTo", out var to) || from > to ||
            registration.Length > 12 || registration.Length > 0 && (PolicyDiscoveryService.NormalizeRegistration(registration).Length == 0 || registration.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not (' ' or '-'))))
            return BadPolicyQuery(context);
        try {
            await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            await service.AuthorizeAsync(db, actor, token);
            if (fixedClientId is Guid owner && !await db.Set<ClientAccount>().AnyAsync(x => x.Id == owner, token)) throw new QuoteOperationException(404, "client-not-found");
            var filters = fixedClientId == null ? new[] { "q", "productCode", "state", "sort", "direction", "clientId", "agencyId", "registration", "inceptionFrom", "inceptionTo" } : ["kind"];
            var page = paging.ReadBound(context, actor, sort + ":" + direction, await QuoteDiscovery.ListVersionAsync(db, token), filters);
            if (page is null) return BadPolicyQuery(context);
            var rows = PolicyDiscoveryService.Search(db, PolicyDiscoveryService.Rows(db, page.AsOf), search); clientId = fixedClientId ?? clientId;
            if (clientId != null) rows = rows.Where(x => x.ClientId == clientId);
            if (agencyId != null) rows = rows.Where(x => x.AgencyId == agencyId);
            if (product.Length > 0) rows = rows.Where(x => x.ProductCode == product);
            if (state.Length > 0) rows = rows.Where(x => x.State == state);
            if (from != null) rows = rows.Where(x => x.InceptionDate >= from);
            if (to != null) rows = rows.Where(x => x.InceptionDate <= to);
            if (registration.Length > 0) { var normalized = PolicyDiscoveryService.NormalizeRegistration(registration); rows = rows.Where(x => db.Set<PolicyRegistration>().Any(r => r.PolicyId == x.Id && r.VersionId == x.CurrentVersionId && r.NormalizedRegistration.Contains(normalized))); }
            var count = await rows.CountAsync(token); var items = await PolicyDiscoveryService.Order(rows, sort, direction == "desc").Skip(page.Offset).Take(page.Size).ToArrayAsync(token);
            await tx.CommitAsync(token);
            if (fixedClientId != null) return Results.Json(new { items = items.Select(x => new { x.Id, kind = "policy", x.Reference, x.RelationshipId, x.AgencyName, x.ProductCode, x.State }), totalCount = count, nextCursor = paging.Next(page, page.Offset + items.Length < count) }, ClientEndpoints.Json);
            return Results.Json(new { items, totalCount = count, nextCursor = paging.Next(page, page.Offset + items.Length < count) }, ClientEndpoints.Json);
        } catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static IResult BadPolicyQuery(HttpContext context) => IdentityEndpoints.Problem(context, 400, "invalid-query", "Refresh the policy list and use its current filters.");
}
