using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;
using Json.Schema;

namespace BackOffice.Application.Underwriting;

public static class CommercialUnderwritingConfiguration
{
    private static readonly Lazy<JsonSchema> Schema = new(() => QuoteCaptureShape.BuildBundled("CommercialUnderwriting.ConfigSchema"));

    public static bool Valid(JsonElement definition, string kind)
    {
        if (kind is not ("rating" or "binder" or "authority") || definition.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(definition.GetRawText()) > 65536 ||
            !Unique(definition) || !Schema.Value.Evaluate(definition, new EvaluationOptions { RequireFormatValidation = true }).IsValid || definition.GetProperty("kind").GetString() != kind) return false;
        return UnderwritingConfiguration.Instant(definition, "effectiveFrom") < UnderwritingConfiguration.Instant(definition, "effectiveTo") &&
            (kind == "rating" ? UnderwritingConfiguration.Amount(definition, "basePremium") > 0 && UnderwritingConfiguration.Amount(definition, "minimumPremium") > 0 &&
            UnderwritingConfiguration.Amount(definition, "maximumAnnualPremium") >= UnderwritingConfiguration.Amount(definition, "minimumPremium") :
            definition.GetProperty("limits").EnumerateObject().All(x => UnderwritingConfiguration.Amount(definition.GetProperty("limits"), x.Name) > 0) &&
            (kind != "binder" || definition.GetProperty("providerId").GetGuid() != Guid.Empty));
    }

    public static bool Current(JsonElement definition, string kind, DateTimeOffset now, ResolvedQuoteTerm term) =>
        Valid(definition, kind) && UnderwritingConfiguration.Instant(definition, "effectiveFrom") <= now &&
        now < UnderwritingConfiguration.Instant(definition, "effectiveTo") &&
        UnderwritingConfiguration.Instant(definition, "effectiveFrom") <= term.StartsAt &&
        term.StartsAt < term.EndsAt && term.EndsAt <= UnderwritingConfiguration.Instant(definition, "effectiveTo");

    public static bool WithinBinder(JsonElement authority, JsonElement binder) =>
        Valid(authority, "authority") && Valid(binder, "binder") &&
        UnderwritingConfiguration.Instant(authority, "effectiveFrom") >= UnderwritingConfiguration.Instant(binder, "effectiveFrom") &&
        UnderwritingConfiguration.Instant(authority, "effectiveTo") <= UnderwritingConfiguration.Instant(binder, "effectiveTo") &&
        authority.GetProperty("limits").EnumerateObject().All(x =>
            UnderwritingConfiguration.Amount(authority.GetProperty("limits"), x.Name) <= UnderwritingConfiguration.Amount(binder.GetProperty("limits"), x.Name));

    private static bool Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            return value.EnumerateObject().All(x => keys.Add(x.Name) && Unique(x.Value));
        }
        return value.ValueKind != JsonValueKind.Array || value.EnumerateArray().All(Unique);
    }
}
