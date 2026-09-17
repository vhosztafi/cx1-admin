using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyDiscoveryRow(Guid Id, string Reference, Guid ClientId, Guid AgencyId, Guid RelationshipId,
    string ClientName, string ClientReference, string AgencyName, string ProductCode, string State, Guid CurrentTermId,
    Guid CurrentVersionId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset IssuedAt, DateOnly InceptionDate);

public sealed class PolicyDiscoveryService
{
    public async Task AuthorizeAsync(BackOfficeDbContext db, ActorContext actor, CancellationToken token = default)
    {
        if (!actor.HasCapability("policy-discovery-read")) throw new QuoteOperationException(403, "policy-access-denied");
        await QuoteDiscovery.AuthorizeAsync(db, actor, token);
    }
    public static IQueryable<PolicyDiscoveryRow> Rows(BackOfficeDbContext db, DateTimeOffset effectiveAt, DateTimeOffset? knownAt = null)
        => PolicyTemporalSelector.DiscoveryRows(db, effectiveAt, knownAt ?? effectiveAt);
    public static IQueryable<PolicyDiscoveryRow> Search(BackOfficeDbContext db, IQueryable<PolicyDiscoveryRow> rows, string search)
    {
        if (search.Length == 0) return rows;
        var text = search.Trim().ToUpperInvariant(); var registration = NormalizeRegistration(text); var registrationSearch = registration.Length is >= 1 and <= 12 && registration.All(char.IsAsciiLetterOrDigit);
        return rows.Where(x => x.Reference.Contains(text) || x.ClientReference.Contains(text) || x.ClientName.ToUpper().Contains(text) || x.AgencyName.ToUpper().Contains(text) ||
            registrationSearch && db.Set<PolicyRegistration>().Any(r => r.PolicyId == x.Id && r.VersionId == x.CurrentVersionId && r.NormalizedRegistration.Contains(registration)));
    }
    public static string NormalizeRegistration(string value) => value.Replace(" ", "").Replace("-", "").ToUpperInvariant();
    public static IOrderedQueryable<PolicyDiscoveryRow> Order(IQueryable<PolicyDiscoveryRow> rows, string sort, bool descending) => (sort, descending) switch {
        ("inception", true) => rows.OrderByDescending(x => x.StartsAt).ThenBy(x => x.Id),
        ("inception", false) => rows.OrderBy(x => x.StartsAt).ThenBy(x => x.Id),
        ("issued", true) => rows.OrderByDescending(x => x.IssuedAt).ThenBy(x => x.Id),
        ("issued", false) => rows.OrderBy(x => x.IssuedAt).ThenBy(x => x.Id),
        ("reference", true) => rows.OrderByDescending(x => x.Reference).ThenBy(x => x.Id),
        _ => rows.OrderBy(x => x.Reference).ThenBy(x => x.Id)
    };
}
