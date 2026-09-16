using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencySharedQuote(Guid Id, string Reference, string ClientName, string ProductCode, string State,
    DateTimeOffset UpdatedAt, DateOnly? StartDate);
public sealed record AgencySharedQuoteSection(string State, int TotalCount);

public static partial class AgencySharingService
{
    public static Task<AgencySharingPage<AgencySharedQuote>> Quotes(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, false, "quotes", QuoteRows, token);
    public static Task<AgencySharingPage<AgencySharedQuote>> PreviewQuotes(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, true, "quotes", QuoteRows, token);

    private static IQueryable<AgencySharedQuote> QuoteRows(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
    {
        var relationships = Relationships(db, agencyId, query);
        // Search only values present in this allowlist. In particular, searching
        // registrations must not reveal hidden risk data through result counts.
        return QuoteDiscovery.Rows(db).Where(x => x.AgencyId == agencyId && relationships.Any(r => r.Id == x.RelationshipId && r.ClientId == x.ClientId) &&
                (string.IsNullOrEmpty(query.Search) || x.Reference.Contains(query.Search) || x.ClientName.Contains(query.Search)))
            .OrderBy(x => x.Reference).ThenBy(x => x.Id)
            .Select(x => new AgencySharedQuote(x.Id, x.Reference, x.ClientName, x.ProductCode, x.State, x.UpdatedAt, x.StartDate));
    }
}
