using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Infrastructure.Agencies;
public sealed record AgencySharedPolicy(Guid Id, string Reference, string ClientName, string ProductCode, string State, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public static partial class AgencySharingService
{
    public static Task<AgencySharingPage<AgencySharedPolicy>> Policies(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, false, "policies", PolicyRows, token);
    public static Task<AgencySharingPage<AgencySharedPolicy>> PreviewPolicies(BackOfficeDbContext db, ActorContext actor, Guid agencyId, AgencySharingQuery query, CancellationToken token = default)
        => Read(db, actor, agencyId, query, true, "policies", PolicyRows, token);
    private static IQueryable<AgencySharedPolicy> PolicyRows(BackOfficeDbContext db, Guid agencyId, AgencySharingQuery query)
    {
        var relationships = Relationships(db, agencyId, query);
        // Search, count and cursor scope use only the visible allowlist. No risk,
        // registration, financial or underwriting facts can influence this query.
        return PolicyDiscoveryService.Rows(db, DateTimeOffset.UtcNow)
            .Where(x => x.AgencyId == agencyId && relationships.Any(r => r.Id == x.RelationshipId && r.ClientId == x.ClientId) &&
                (query.PolicyId == null || x.Id == query.PolicyId) &&
                (string.IsNullOrEmpty(query.Search) || x.Reference.Contains(query.Search) || x.ClientName.Contains(query.Search)))
            .OrderBy(x => x.Reference).ThenBy(x => x.Id)
            .Select(x => new AgencySharedPolicy(x.Id, x.Reference, x.ClientName, x.ProductCode, x.State, x.StartsAt, x.EndsAt));
    }
}
