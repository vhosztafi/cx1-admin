using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record EligibleQuoteCapture(Product Product, ProductVersion ProductVersion, AgencyTermsVersion Terms,
    QuoteVersionPins Pins, Guid CaptureSettingId, Guid DistributionSettingId);
internal sealed record QuoteCaptureSettings(SettingVersion Capture, SettingVersion Distribution,
    IReadOnlyDictionary<Guid, QuoteCaptureVersion> Products, IReadOnlySet<Guid> DistributedProducts);

// Invoke after held QuoteScope authorization, before command receipt lookup.
// Explicit capture settings supply capture-only question/reference pins. Existing
// product metadata and approved distribution/terms are never rewritten here.
public static class QuoteCaptureEligibility
{
    public static async Task<EligibleQuoteCapture> ResolveAsync(BackOfficeDbContext db, QuoteRelationshipScope scope,
        Guid productVersionId, DateTimeOffset now, Guid? pinnedTermsId = null, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Capture eligibility requires held quote authority.");
        if (!scope.Actor.HasCapability("quote-capture") || scope.Agency.State != "active" || scope.Client.IdentityState != "active" || scope.Relationship.State != "active")
            throw new QuoteOperationException(403, "quote-access-denied");
        var settings = await LoadSettingsAsync(db, now, token);
        if (!settings.Products.TryGetValue(productVersionId, out var pin) || !settings.DistributedProducts.Contains(productVersionId)) throw Unavailable();
        var version = await db.Set<ProductVersion>().FromSqlInterpolated($"SELECT * FROM ProductVersion WITH(HOLDLOCK) WHERE Id={productVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Unavailable();
        var product = await db.Set<Product>().FromSqlInterpolated($"SELECT * FROM Product WITH(HOLDLOCK) WHERE Id={version.ProductId}")
            .AsNoTracking().SingleAsync(token);
        var provider = await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={version.ProviderId}")
            .AsNoTracking().SingleAsync(token);
        if (product.Code is not ("motor-trade-road-risks" or "motor-trade-combined") || version.State == "retired" ||
            version.JsonSchemaVersion != pin.SchemaVersion || version.EffectiveFrom > now || version.EffectiveTo <= now || provider.State != "active") throw Unavailable();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var terms = await db.Set<AgencyTermsVersion>().FromSqlInterpolated($"SELECT * FROM AgencyTermsVersion WITH(HOLDLOCK) WHERE AgencyId={scope.Agency.Id}")
            .AsNoTracking().ToListAsync(token);
        var current = terms.Where(x => x.EffectiveFrom <= today).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version).FirstOrDefault() ?? throw Unavailable();
        var retained = pinnedTermsId is null ? current : terms.SingleOrDefault(x => x.Id == pinnedTermsId && x.EffectiveFrom <= today) ?? throw Unavailable();
        var grants = await db.Set<AgencyProduct>().FromSqlInterpolated($"SELECT * FROM AgencyProduct WITH(HOLDLOCK) WHERE AgencyTermsVersionId={current.Id} OR AgencyTermsVersionId={retained.Id}")
            .AsNoTracking().ToListAsync(token);
        bool Granted(Guid termsId) => grants.Any(x => x.AgencyTermsVersionId == termsId && x.ProductVersionId == productVersionId && x.EffectiveFrom <= today);
        if (!Granted(current.Id) || !Granted(retained.Id)) throw Unavailable();
        return new(product, version, retained, new(version.Id, retained.Id, pin.SchemaVersion, pin.QuestionSetVersion, pin.ReferenceVersion), settings.Capture.Id, settings.Distribution.Id);
    }

    internal static async Task<QuoteCaptureSettings> LoadSettingsAsync(BackOfficeDbContext db, DateTimeOffset now, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Capture settings require held authority.");
        var settings = await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope IN (N'quote-capture',N'agency-distribution')")
            .AsNoTracking().ToListAsync(token);
        SettingVersion? Current(string name) => settings.Where(x => x.Scope == name && x.EffectiveFrom <= now).OrderByDescending(x => x.Version).FirstOrDefault();
        var capture = Current("quote-capture"); var distribution = Current("agency-distribution");
        var configured = capture is null ? null : QuoteCaptureConfiguration.Parse(capture.Values);
        var distributed = distribution is null ? null : AgencyDistributionRules.Parse(distribution.Values);
        if (configured is null || distributed is null) throw new QuoteOperationException(503, "quote-capture-configuration-unavailable");
        return new(capture!, distribution!, configured, distributed);
    }

    private static QuoteOperationException Unavailable() => new(409, "quote-product-unavailable");
}
