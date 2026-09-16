using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record EffectiveUnderwritingGrant(UserAuthorityGrant Grant, AuthorityVersion Version, JsonElement Definition);

public static class QuoteUnderwritingScope
{
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "quote-rate", "quote-submit", "quote-revise", "quote-terms", "quote-acceptance",
        "underwriting-evidence-write", "underwriting-evidence-review", "underwriting-decide-within-authority", "underwriting-escalate", "underwriting-record-capacity", "policy-issue-within-authority" };

    public static async Task<OwnedQuoteScope> HoldAsync(BackOfficeDbContext db, ActorContext actor, Guid quoteId, string action, CancellationToken token = default)
    {
        if (!Actions.Contains(action)) throw new ArgumentException("Unsupported underwriting action.");
        if (!actor.HasCapability(action)) throw new QuoteOperationException(403, "underwriting-access-denied");
        // Common lock ordering and identity reload are shared with capture and
        // matching. Underwriting writes do not reopen proposal editing.
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Underwriting, token);
        if (!owned.Scope.Actor.HasCapability(action)) throw new QuoteOperationException(403, "underwriting-access-denied");
        return owned;
    }

    public static async Task<IReadOnlyList<EffectiveUnderwritingGrant>> GrantsAsync(BackOfficeDbContext db, OwnedQuoteScope owned,
        Guid productVersionId, BinderVersion binder, string productCode, ResolvedQuoteTerm term, DateTimeOffset now, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Underwriting grants require held authority.");
        var userId = owned.Scope.Actor.UserId;
        var grants = await db.Set<UserAuthorityGrant>().FromSqlInterpolated($"SELECT * FROM UserAuthorityGrant WITH(HOLDLOCK) WHERE UserId={userId}").AsNoTracking().ToArrayAsync(token);
        using var binderJson = JsonDocument.Parse(binder.DefinitionJson);
        if (binder.ProductId != owned.Quote.ProductId || binder.State != "published" || binder.EffectiveFrom > now || now >= binder.EffectiveTo ||
            binder.EffectiveFrom > term.StartsAt || term.EndsAt > binder.EffectiveTo ||
            !UnderwritingConfiguration.Current(binderJson.RootElement, "binder", productCode, now, term)) return [];
        var result = new List<EffectiveUnderwritingGrant>();
        foreach (var grant in grants.Where(x => x.RevokedAt is null && x.EffectiveFrom <= now && now < x.EffectiveTo && x.EffectiveFrom <= term.StartsAt && term.EndsAt <= x.EffectiveTo))
        {
            var authority = await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE Id={grant.AuthorityVersionId}").AsNoTracking().SingleAsync(token);
            if (authority.ProductId != owned.Quote.ProductId || authority.ProductVersionId != productVersionId || authority.BinderVersionId != binder.Id || authority.State != "published" ||
                authority.EffectiveFrom > now || now >= authority.EffectiveTo || authority.EffectiveFrom > term.StartsAt || term.EndsAt > authority.EffectiveTo) continue;
            using var definition = JsonDocument.Parse(authority.DefinitionJson);
            if (UnderwritingConfiguration.Current(definition.RootElement, "authority", productCode, now, term) && UnderwritingConfiguration.WithinBinder(definition.RootElement, binderJson.RootElement))
                result.Add(new(grant, authority, definition.RootElement.Clone()));
        }
        return result.AsReadOnly();
    }
}
