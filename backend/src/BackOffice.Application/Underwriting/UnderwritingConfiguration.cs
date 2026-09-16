using System.Globalization;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;
using Json.Schema;

namespace BackOffice.Application.Underwriting;

// Publication and every consumer use the same closed bundled definition. JSON
// validity alone never grants publication/currentness or user authority.
public static class UnderwritingConfiguration
{
    private static readonly Lazy<JsonSchema> Schema = new(() => QuoteCaptureShape.BuildBundled("Underwriting.ConfigSchema"));
    public static bool Valid(JsonElement definition, string kind)
    {
        if (definition.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(definition.GetRawText()) > 65536 ||
            !UniqueKeys(definition) || !Schema.Value.Evaluate(definition, new EvaluationOptions { RequireFormatValidation = true }).IsValid ||
            definition.GetProperty("kind").GetString() != kind) return false;
        var from = Instant(definition, "effectiveFrom"); var to = Instant(definition, "effectiveTo");
        if (from >= to) return false;
        if (kind == "rating") return Amount(definition, "maximumAnnualPremium") >= Amount(definition, "minimumPremium") &&
            definition.GetProperty("referenceVersion").GetString() == QuoteCatalogueIdentity.Version;
        var limits = definition.GetProperty("limits");
        return limits.GetProperty("minimumDriverAge").GetInt32() <= limits.GetProperty("maximumDriverAge").GetInt32();
    }
    public static bool Current(JsonElement definition, string kind, string product, DateTimeOffset now, ResolvedQuoteTerm term) =>
        Valid(definition, kind) && definition.GetProperty("productCode").GetString() == product &&
        Instant(definition, "effectiveFrom") <= now && now < Instant(definition, "effectiveTo") &&
        Instant(definition, "effectiveFrom") <= term.StartsAt && term.EndsAt <= Instant(definition, "effectiveTo") && term.StartsAt < term.EndsAt;

    public static bool WithinBinder(JsonElement authority, JsonElement binder)
    {
        if (!Valid(authority, "authority") || !Valid(binder, "binder") ||
            authority.GetProperty("productCode").GetString() != binder.GetProperty("productCode").GetString() ||
            Instant(authority, "effectiveFrom") < Instant(binder, "effectiveFrom") || Instant(authority, "effectiveTo") > Instant(binder, "effectiveTo")) return false;
        var grant = authority.GetProperty("limits"); var ceiling = binder.GetProperty("limits");
        foreach (var amount in new[] { "annualPremiumLimit", "stockLimit", "vehicleLimit" }) if (Amount(grant, amount) > Amount(ceiling, amount)) return false;
        foreach (var amount in new[] { "road-risks", "stock-custody", "premises", "tools-equipment" })
            if (Amount(grant.GetProperty("coverLimits"), amount) > Amount(ceiling.GetProperty("coverLimits"), amount)) return false;
        if (grant.GetProperty("minimumDriverAge").GetInt32() < ceiling.GetProperty("minimumDriverAge").GetInt32() ||
            grant.GetProperty("maximumDriverAge").GetInt32() > ceiling.GetProperty("maximumDriverAge").GetInt32() ||
            grant.GetProperty("minimumLicenceYears").GetInt32() < ceiling.GetProperty("minimumLicenceYears").GetInt32()) return false;
        foreach (var flag in new[] { "reviewConvictions", "reviewClaims", "reviewTradingHistory", "allowSalvage" })
            if (grant.GetProperty(flag).GetBoolean() && !ceiling.GetProperty(flag).GetBoolean()) return false;
        var allowed = ceiling.GetProperty("allowedTradeValues").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        return grant.GetProperty("allowedTradeValues").EnumerateArray().All(x => allowed.Contains(x.GetInt32()));
    }

    internal static decimal Amount(JsonElement value, string name) => decimal.Parse(value.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
    internal static DateTimeOffset Instant(JsonElement value, string name) => DateTimeOffset.Parse(value.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
    private static bool UniqueKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return value.EnumerateObject().All(p => names.Add(p.Name) && UniqueKeys(p.Value));
        }
        return value.ValueKind != JsonValueKind.Array || value.EnumerateArray().All(UniqueKeys);
    }
}
