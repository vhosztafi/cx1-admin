using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Agencies;
using BackOffice.Application.Parties;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record EligibleQuoteRating(EligibleQuoteCapture Capture, SettingVersion RuntimeVersion,
    UnderwritingRuntimeSettings Runtime, SettingVersion ScenarioVersion, string Scenario,
    RatingRuleVersion RatingVersion, BinderVersion BinderVersion, AuthorityVersion AuthorityVersion,
    JsonElement Rating, JsonElement Binder, JsonElement Authority, int CommissionBasisPoints, decimal? MinimumPremium);

// Called inside held current quote scope before resolving a command receipt.
// Baseline referral assessment is deliberately independent of the requesting
// user's decision authority: servicing staff can request a price.
public static class QuoteRatingEligibility
{
    public static async Task<EligibleQuoteRating> ResolveAsync(BackOfficeDbContext db, OwnedQuoteScope owned,
        Guid productVersionId, Guid termsId, ResolvedQuoteTerm term, DateTimeOffset now, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Rating eligibility requires held quote scope.");
        var capture = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, productVersionId, now, termsId, token);
        var version = capture.ProductVersion;
        if (owned.Quote.ProductId != capture.Product.Id || version.State != "published" || version.QuestionSetVersion != capture.Pins.QuestionSetVersion ||
            !Interval(version.EffectiveFrom, version.EffectiveTo, now, term) || !PublishedProduct(version.Definition, capture.Product.Code)) throw Unavailable();

        // Current commercial terms must be adopted explicitly into a new
        // revision. Reusing an old grant must never silently change commission.
        var currentCapture = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, productVersionId, now, null, token);
        if (currentCapture.Terms.Id != termsId) throw new QuoteOperationException(409, "quote-terms-refresh-required");
        var inceptionDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(term.StartsAt, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var commercial = Commercial(capture.Terms, productVersionId, inceptionDay);
        var rows = await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'underwriting-runtime'")
            .AsNoTracking().ToArrayAsync(token);
        var setting = rows.Where(x => x.EffectiveFrom <= now).OrderByDescending(x => x.Version).FirstOrDefault();
        var runtime = setting is null ? null : UnderwritingRuntimeConfiguration.Parse(setting.Values);
        if (runtime is null) throw Configuration();
        if (!runtime.Products.TryGetValue(productVersionId, out var choice)) throw Unavailable();
        var scenario = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Id={runtime.ScenarioVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token);
        var scenarioName = scenario is null ? null : UnderwritingRuntimeConfiguration.Scenario(scenario.Values);
        if (scenario is null || scenario.EffectiveFrom > now || scenarioName is null || scenario.Scope != "quote-rating/" + scenarioName) throw Configuration();
        if (!await db.Set<Team>().FromSqlInterpolated($"SELECT * FROM Team WITH(HOLDLOCK) WHERE Id={runtime.RoutingTeamId}").AsNoTracking().AnyAsync(token)) throw Configuration();
        var rating = await db.Set<RatingRuleVersion>().FromSqlInterpolated($"SELECT * FROM RatingRuleVersion WITH(HOLDLOCK) WHERE Id={choice.RatingRuleVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Unavailable();
        var binder = await db.Set<BinderVersion>().FromSqlInterpolated($"SELECT * FROM BinderVersion WITH(HOLDLOCK) WHERE Id={choice.BinderVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Unavailable();
        var authority = await db.Set<AuthorityVersion>().FromSqlInterpolated($"SELECT * FROM AuthorityVersion WITH(HOLDLOCK) WHERE Id={choice.AuthorityVersionId}")
            .AsNoTracking().SingleOrDefaultAsync(token) ?? throw Unavailable();
        if (rating.ProductId != capture.Product.Id || binder.ProductId != capture.Product.Id || binder.ProviderId != version.ProviderId ||
            authority.ProductId != capture.Product.Id || authority.ProductVersionId != version.Id || authority.BinderVersionId != binder.Id ||
            rating.State != "published" || binder.State != "published" || authority.State != "published" ||
            !Interval(rating.EffectiveFrom, rating.EffectiveTo, now, term) || !Interval(binder.EffectiveFrom, binder.EffectiveTo, now, term) ||
            !Interval(authority.EffectiveFrom, authority.EffectiveTo, now, term)) throw Unavailable();
        var ratingJson = Definition(rating.DefinitionJson, "rating", capture.Product.Code, now, term);
        var binderJson = Definition(binder.DefinitionJson, "binder", capture.Product.Code, now, term);
        var authorityJson = Definition(authority.DefinitionJson, "authority", capture.Product.Code, now, term);
        if (binderJson.GetProperty("providerId").GetGuid() != binder.ProviderId || !UnderwritingConfiguration.WithinBinder(authorityJson, binderJson)) throw Unavailable();
        return new(capture, setting!, runtime, scenario, scenarioName, rating, binder, authority, ratingJson, binderJson, authorityJson, commercial.Commission, commercial.Minimum);
    }

    private static (int Commission, decimal? Minimum) Commercial(AgencyTermsVersion terms, Guid productVersionId, DateOnly inceptionDay)
    {
        try
        {
            using var doc = JsonDocument.Parse(terms.Snapshot);
            var published = AgencyTermsRules.ReadPublished(doc.RootElement);
            if (published.EffectiveFrom != terms.EffectiveFrom) throw Unavailable();
            var product = published.Products.SingleOrDefault(x => x.ProductVersionId == productVersionId) ?? throw Unavailable();
            if (published.EffectiveFrom > inceptionDay || product.EffectiveFrom > inceptionDay) throw Unavailable();
            var commercial = doc.RootElement.GetProperty("commercialTerms");
            var commission = commercial.GetProperty("commissionBasis").GetString() == "flat-rate"
                ? commercial.GetProperty("flatCommissionBasisPoints").GetInt32() : product.BrokerCommissionBasisPoints;
            decimal? minimum = commercial.GetProperty("minimumPremiumOverrideMode").GetString() == "capacity-provider-agreed"
                ? decimal.Parse(commercial.GetProperty("minimumPremiumOverride").GetString()!, CultureInfo.InvariantCulture) : null;
            return (commission, minimum);
        }
        catch (Exception ex) when (ex is JsonException or AgencyCommandException or PartyValidationException or FormatException or InvalidOperationException or KeyNotFoundException)
        { throw new QuoteOperationException(409, "quote-commercial-terms-unavailable"); }
    }

    private static JsonElement Definition(string json, string kind, string product, DateTimeOffset now, ResolvedQuoteTerm term)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!UnderwritingConfiguration.Current(doc.RootElement, kind, product, now, term)) throw Unavailable();
            return doc.RootElement.Clone();
        }
        catch (JsonException) { throw Unavailable(); }
    }

    private static bool PublishedProduct(string json, string code)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var expected = new HashSet<string>(["demo", "kind", "schemaVersion", "productCode", "referenceVersion", "requestedSectionsRequired", "ratingAvailable"], StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject()) if (!expected.Remove(property.Name)) return false;
            return expected.Count == 0 && root.GetProperty("demo").ValueKind == JsonValueKind.True &&
                root.GetProperty("kind").GetString() == "motor-trade-underwriting" && root.GetProperty("schemaVersion").GetString() == "1" &&
                root.GetProperty("productCode").GetString() == code && root.GetProperty("referenceVersion").GetString() == QuoteCatalogueIdentity.Version &&
                root.GetProperty("requestedSectionsRequired").ValueKind == JsonValueKind.True && root.GetProperty("ratingAvailable").ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return false; }
    }

    private static bool Interval(DateTimeOffset from, DateTimeOffset? to, DateTimeOffset now, ResolvedQuoteTerm term) =>
        from <= now && to is { } end && now < end && from <= term.StartsAt && term.EndsAt <= end && term.StartsAt < term.EndsAt;
    private static QuoteOperationException Configuration() => new(503, "underwriting-configuration-unavailable");
    private static QuoteOperationException Unavailable() => new(409, "underwriting-product-unavailable");
}
