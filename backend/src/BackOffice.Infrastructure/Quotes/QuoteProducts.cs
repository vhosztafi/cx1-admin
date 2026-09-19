using System.Globalization;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteProductSelection(Guid ProductVersionId, string ProductCode, string DisplayName, string VersionLabel,
    string QuestionSetVersion, string ReferenceDataVersion, bool CaptureEligible, string? UnavailableReason);

public sealed class QuoteProducts(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public async Task<IReadOnlyList<QuoteProductSelection>> ListAsync(ActorContext actor, Guid relationshipId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var scope = await QuoteScope.ForRelationshipAsync(db, actor, relationshipId, QuoteAccess.Read, token);
        var now = time.GetUtcNow();
        var settings = await QuoteCaptureEligibility.LoadSettingsAsync(db, now, token);
        var result = new List<QuoteProductSelection>();
        // The explicit capture catalogue is the offer set, not all rating versions.
        // A valid empty catalogue is revocation; a broken catalogue is a 503.
        foreach (var pin in settings.Products.Values.OrderBy(x => x.ProductVersionId))
        {
            string? unavailable = !scope.Actor.HasCapability("quote-capture") ? "Quote capture is not permitted for this account."
                : scope.Agency.State != "active" ? "The agency is not active."
                : scope.Client.IdentityState != "active" || scope.Relationship.State != "active" ? "The client relationship is not active." : null;
            EligibleQuoteCapture? eligible = null;
            if (unavailable is null)
            {
                try { eligible = await QuoteCaptureEligibility.ResolveAsync(db, scope, pin.ProductVersionId, now, token: token); }
                catch (QuoteOperationException error) when (error.Code == "quote-product-unavailable")
                { unavailable = "This product is not currently available under the agency's approved access."; }
            }
            var version = eligible?.ProductVersion ?? await db.Set<ProductVersion>()
                .FromSqlInterpolated($"SELECT * FROM ProductVersion WITH(HOLDLOCK) WHERE Id={pin.ProductVersionId}")
                .AsNoTracking().SingleOrDefaultAsync(token) ?? throw ConfigurationUnavailable();
            var product = eligible?.Product ?? await db.Set<Product>()
                .FromSqlInterpolated($"SELECT * FROM Product WITH(HOLDLOCK) WHERE Id={version.ProductId}")
                .AsNoTracking().SingleAsync(token);
            if (product.Code is not ("motor-trade-road-risks" or "motor-trade-combined" or "commercial-combined") || string.IsNullOrWhiteSpace(product.Name))
                throw ConfigurationUnavailable();
            result.Add(new(version.Id, product.Code, product.Name, "v" + version.Version.ToString(CultureInfo.InvariantCulture),
                pin.QuestionSetVersion, pin.ReferenceVersion, eligible is not null, unavailable));
        }
        var ordered = result.OrderBy(x => x.ProductCode, StringComparer.Ordinal).ThenBy(x => x.ProductVersionId).ToArray();
        await transaction.CommitAsync(token);
        return ordered;
    }

    private static QuoteOperationException ConfigurationUnavailable() => new(503, "quote-capture-configuration-unavailable");
}
