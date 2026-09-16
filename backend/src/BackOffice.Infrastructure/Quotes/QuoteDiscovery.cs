using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteDiscoveryRow(Guid Id, string Reference, Guid RelationshipId, Guid ClientId, Guid AgencyId,
    string ClientReference, string ClientName, string AgencyName, string ProductCode, string State,
    Guid RevisionId, int RevisionNumber, DateTimeOffset UpdatedAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateOnly? StartDate);

public static class QuoteDiscovery
{
    public static async Task AuthorizeAsync(BackOfficeDbContext db, ActorContext actor, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Quote discovery requires a held transaction.");
        if (!actor.HasCapability("quote-read")) throw new QuoteOperationException(403, "quote-access-denied");
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code)) ||
            !new ActorContext(identity.User.Id, identity.User.TeamId, identity.User.AgencyId, identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal)).HasCapability("quote-read"))
            throw new QuoteOperationException(403, "quote-access-denied");
    }

    // Cursor invalidation is deliberately conservative: any versioned database
    // change requires refreshing the list instead of silently skipping/repeating
    // rows after quote, client or agency edits affect its ordering or filters.
    public static Task<string> VersionAsync(BackOfficeDbContext db, CancellationToken token) =>
        db.Database.SqlQueryRaw<string>("SELECT CONVERT(varchar(18),@@DBTS,1) AS [Value]").SingleAsync(token);

    // Call only under current authority; agency sharing applies its own trusted
    // AgencyId predicate and projects a smaller allowlist before materialization.
    public static IQueryable<QuoteDiscoveryRow> Rows(BackOfficeDbContext db) => db.Database.SqlQueryRaw<QuoteDiscoveryRow>("""
        SELECT q.Id,q.Reference,q.RelationshipId,q.ClientId,q.AgencyId,c.Reference AS ClientReference,
            c.LegalName AS ClientName,a.LegalName AS AgencyName,p.Code AS ProductCode,q.State,
            r.Id AS RevisionId,r.Number AS RevisionNumber,q.UpdatedAt,
            TRY_CONVERT(date,JSON_VALUE(r.TermIntentJson,'$.localStartDate')) AS StartDate
        FROM Quote q
        JOIN QuoteRevision r ON r.Id=q.CurrentRevisionId AND r.QuoteId=q.Id
        JOIN ClientAccount c ON c.Id=q.ClientId
        JOIN ClientAgencyRelationship rel ON rel.Id=q.RelationshipId AND rel.ClientId=q.ClientId AND rel.AgencyId=q.AgencyId
        JOIN Agency a ON a.Id=q.AgencyId
        JOIN Product p ON p.Id=q.ProductId
        """);

    public static IQueryable<QuoteDiscoveryRow> Search(BackOfficeDbContext db, IQueryable<QuoteDiscoveryRow> rows, string search)
    {
        if (search.Length == 0) return rows;
        var text = search.Trim().ToUpperInvariant();
        var registration = string.Concat(text.Where(c => c != ' '));
        var registrationSearch = registration.Length is >= 2 and <= 12 && registration.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');
        return rows.Where(x => x.Reference.Contains(text) || x.ClientReference.Contains(text) || x.ClientName.ToUpper().Contains(text) ||
            x.AgencyName.ToUpper().Contains(text) || registrationSearch && db.Set<QuoteRegistration>().Any(r => r.QuoteId == x.Id && r.NormalizedRegistration.Contains(registration)));
    }

    public static IOrderedQueryable<QuoteDiscoveryRow> Order(IQueryable<QuoteDiscoveryRow> rows, string sort, bool descending) => (sort, descending) switch
    {
        ("updated", true) => rows.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id),
        ("updated", false) => rows.OrderBy(x => x.UpdatedAt).ThenBy(x => x.Id),
        ("start", true) => rows.OrderBy(x => x.StartDate == null).ThenByDescending(x => x.StartDate).ThenBy(x => x.Id),
        ("start", false) => rows.OrderBy(x => x.StartDate == null).ThenBy(x => x.StartDate).ThenBy(x => x.Id),
        ("reference", true) => rows.OrderByDescending(x => x.Reference).ThenBy(x => x.Id),
        _ => rows.OrderBy(x => x.Reference).ThenBy(x => x.Id)
    };
}
