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
    public static IQueryable<PolicyDiscoveryRow> Rows(BackOfficeDbContext db, DateTimeOffset now) => db.Database.SqlQuery<PolicyDiscoveryRow>($"""
        SELECT p.Id,p.Reference,p.ClientId,p.AgencyId,p.RelationshipId,c.LegalName AS ClientName,c.Reference AS ClientReference,
            a.LegalName AS AgencyName,product.Code AS ProductCode,
            CASE WHEN {now}<t.StartsAt THEN 'scheduled' WHEN {now}>=t.EndsAt THEN 'expired' ELSE 'active' END AS State,
            t.Id AS CurrentTermId,v.Id AS CurrentVersionId,t.StartsAt,t.EndsAt,issued.ProcessedAt AS IssuedAt,
            CONVERT(date,JSON_VALUE(t.LocalTermIntentJson,'$.localStartDate')) AS InceptionDate
        FROM Policy p JOIN PolicyTerm t ON t.Id=p.CurrentTermId AND t.PolicyId=p.Id
        JOIN PolicyVersion v ON v.Id=t.CurrentVersionId AND v.PolicyId=p.Id AND v.TermId=t.Id
        JOIN PolicyTransaction issued ON issued.Id=v.TransactionId AND issued.PolicyId=p.Id AND issued.TermId=t.Id
        JOIN ClientAccount c ON c.Id=p.ClientId JOIN Agency a ON a.Id=p.AgencyId
        JOIN ClientAgencyRelationship rel ON rel.Id=p.RelationshipId AND rel.ClientId=p.ClientId AND rel.AgencyId=p.AgencyId
        JOIN Product product ON product.Id=p.ProductId
        """);
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
