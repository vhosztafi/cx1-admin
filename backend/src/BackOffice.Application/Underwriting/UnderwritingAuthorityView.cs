using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Underwriting;

public sealed record UnderwritingAuthorityRow(string Code, string Label, string Requested,
    string ActorLimit, string BinderLimit, bool ActorAllows, bool BinderAllows);

// Display each current grant separately. Combining maxima across grants could
// falsely suggest a single authority covers the entire proposed risk.
public static class UnderwritingAuthorityView
{
    public static IReadOnlyList<UnderwritingAuthorityRow> Rows(UnderwritingRisk risk, JsonElement binder, JsonElement? actor, int minimumTradingYears = 5)
    {
        var binderFailures = UnderwritingRules.AssessAuthority(binder, risk, minimumTradingYears);
        var actorFailures = actor is JsonElement grant ? UnderwritingRules.AssessAuthority(grant, risk, minimumTradingYears) : null;
        var result = new List<UnderwritingAuthorityRow>();
        string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
        void Add(string code, string label, string requested, Func<JsonElement, string> limit,
            Func<UnderwritingRequirement, bool> failed)
        {
            result.Add(new(code, label, requested, actor is JsonElement a ? limit(a.GetProperty("limits")) : "No current authority grant",
                limit(binder.GetProperty("limits")), actorFailures is not null && !actorFailures.Any(failed), !binderFailures.Any(failed)));
        }
        void Amount(string code, string label, decimal value, string field) => Add(code, label, "GBP " + Number(value),
            limits => "GBP " + limits.GetProperty(field).GetString(), x => x.RuleCode == code);
        Amount("premium-limit", "Annual premium", risk.AnnualPremium, "annualPremiumLimit");
        Amount("stock-limit", "Stock exposure", risk.StockLimit, "stockLimit");
        Amount("vehicle-limit", "Single vehicle exposure", risk.VehicleLimit, "vehicleLimit");
        foreach (var cover in risk.CoverLimits.OrderBy(x => x.Key, StringComparer.Ordinal))
            Add("cover-" + cover.Key, cover.Key.Replace('-', ' ') + " cover", "GBP " + Number(cover.Value),
                limits => "GBP " + limits.GetProperty("coverLimits").GetProperty(cover.Key).GetString(), x => x.RuleCode == "cover-" + cover.Key);
        string Age(JsonElement limits) => Integer(limits.GetProperty("minimumDriverAge").GetInt32()) + "–" + Integer(limits.GetProperty("maximumDriverAge").GetInt32()) + " years";
        string Licence(JsonElement limits) => "At least " + Integer(limits.GetProperty("minimumLicenceYears").GetInt32()) + " complete years";
        void Review(string code, string label, bool required, string field, Guid? target = null) => Add(code + (target is Guid id ? ":" + id : ""), label,
            required ? "Review required" : "No history declared", limits => limits.GetProperty(field).GetBoolean() ? "Review permitted" : "Review outside authority",
            x => x.RuleCode == code && x.TargetId == target);
        foreach (var driver in risk.Drivers)
        {
            Add("driver-age:" + driver.Id, "Named driver age", Integer(driver.Age) + " years", Age, x => x.RuleCode == "driver-age" && x.TargetId == driver.Id);
            Add("licence-years:" + driver.Id, "Named driver licence experience", Integer(driver.LicenceYears) + " complete years", Licence, x => x.RuleCode == "licence-years" && x.TargetId == driver.Id);
            Review("conviction-history", "Named driver convictions", driver.HasConvictions, "reviewConvictions", driver.Id);
            Review("claims-history", "Named driver claims", driver.HasClaims, "reviewClaims", driver.Id);
        }
        if (risk.AnyDriverCount > 0)
        {
            Add("any-driver-age", "Unnamed driver age range", Integer(risk.AnyDriverMinimumAge!.Value) + "–" + Integer(risk.AnyDriverMaximumAge!.Value) + " years", Age, x => x.RuleCode == "any-driver-age");
            Add("any-driver-licence-years", "Unnamed driver licence experience", "Not established by captured age range", Licence, x => x.RuleCode == "any-driver-licence-years");
        }
        foreach (var trade in risk.TradeValues)
            Add("trade-" + trade, "Trade category " + Integer(trade), "Requested", limits => limits.GetProperty("allowedTradeValues").EnumerateArray().Any(x => x.GetInt32() == trade) ? "Permitted" : "Outside authority", x => x.RuleCode == "trade-" + trade);
        if (risk.HasSalvageOrBreaking) Add("salvage-breaking", "Salvage or breaking", "Declared", limits => limits.GetProperty("allowSalvage").GetBoolean() ? "Permitted" : "Outside authority", x => x.RuleCode == "salvage-breaking");
        Add("UW-22", "Trading history", Integer(risk.TradingYears) + " complete years", limits => limits.GetProperty("reviewTradingHistory").GetBoolean() ? "Trading-history review permitted" : "At least " + Integer(minimumTradingYears) + " complete years", x => x.RuleCode == "UW-22");
        if (risk.HasClaims && !risk.Drivers.Any(x => x.HasClaims)) Review("claims-history", "Business claims", true, "reviewClaims");
        return result.AsReadOnly();
    }
}
