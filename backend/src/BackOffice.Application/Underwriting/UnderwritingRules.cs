using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Domain;

namespace BackOffice.Application.Underwriting;

public sealed record UnderwritingDriver(Guid Id, int Age, int LicenceYears, bool HasConvictions, bool HasClaims);
public sealed record UnderwritingRisk(decimal AnnualPremium, decimal StockLimit, decimal VehicleLimit, int TradingYears,
    bool HasClaims, bool HasValeting, IReadOnlyList<int> TradeValues, IReadOnlyList<UnderwritingDriver> Drivers,
    IReadOnlyDictionary<string, decimal> CoverLimits)
{
    public bool HasSalvageOrBreaking { get; init; }
}
public sealed record UnderwritingRequirement(string RuleCode, string Dimension, Guid? TargetId = null,
    decimal? RequestedAmount = null, decimal? AuthorisedAmount = null);

public static class UnderwritingRules
{
    private static readonly string[] Covers = ["road-risks", "stock-custody", "premises", "tools-equipment"];

    // A caller assesses actor and binder separately and retains both outcomes.
    // Passing this predicate is never a substitute for a current identity/grant,
    // active same-cycle decisions, evidence or a held quote lock.
    public static IReadOnlyList<UnderwritingRequirement> AssessAuthority(JsonElement config, UnderwritingRisk risk, int minimumTradingYears = 5)
    {
        var kind = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("kind", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (kind is not ("authority" or "binder") || !UnderwritingConfiguration.Valid(config, kind))
            throw new ArgumentException("Complete typed authority is required.");
        ValidateRisk(risk);
        if (minimumTradingYears is < 0 or > 100) throw new ArgumentException("Invalid trading threshold.");
        var limits = config.GetProperty("limits"); var result = new List<UnderwritingRequirement>();
        if (risk.HasSalvageOrBreaking && !limits.GetProperty("allowSalvage").GetBoolean()) result.Add(new("salvage-breaking", "trade-restriction"));
        void Amount(string code, decimal requested, decimal allowed) { if (requested > allowed) result.Add(new(code, code, RequestedAmount: requested, AuthorisedAmount: allowed)); }
        Amount("premium-limit", risk.AnnualPremium, UnderwritingConfiguration.Amount(limits, "annualPremiumLimit"));
        Amount("stock-limit", risk.StockLimit, UnderwritingConfiguration.Amount(limits, "stockLimit"));
        Amount("vehicle-limit", risk.VehicleLimit, UnderwritingConfiguration.Amount(limits, "vehicleLimit"));
        var trades = limits.GetProperty("allowedTradeValues").EnumerateArray().Select(x => x.GetInt32()).ToHashSet();
        foreach (var trade in risk.TradeValues.Where(x => !trades.Contains(x)).Order())
            result.Add(new("trade-" + trade.ToString(CultureInfo.InvariantCulture), "trade-restriction"));
        foreach (var cover in risk.CoverLimits.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var allowed = UnderwritingConfiguration.Amount(limits.GetProperty("coverLimits"), cover.Key);
            if (cover.Value > allowed) result.Add(new("cover-" + cover.Key, "cover-restriction", RequestedAmount: cover.Value, AuthorisedAmount: allowed));
        }
        foreach (var driver in risk.Drivers.OrderBy(x => x.Id))
        {
            if (driver.Age < limits.GetProperty("minimumDriverAge").GetInt32() || driver.Age > limits.GetProperty("maximumDriverAge").GetInt32())
                result.Add(new("driver-age", "driver-age", driver.Id));
            if (driver.LicenceYears < limits.GetProperty("minimumLicenceYears").GetInt32()) result.Add(new("licence-years", "licence-years", driver.Id));
            if (driver.HasConvictions && !limits.GetProperty("reviewConvictions").GetBoolean()) result.Add(new("conviction-history", "conviction-history", driver.Id));
            if (driver.HasClaims && !limits.GetProperty("reviewClaims").GetBoolean()) result.Add(new("claims-history", "claims-history", driver.Id));
        }
        if (risk.HasClaims && !risk.Drivers.Any(x => x.HasClaims) && !limits.GetProperty("reviewClaims").GetBoolean())
            result.Add(new("claims-history", "claims-history"));
        if (risk.TradingYears < minimumTradingYears && !limits.GetProperty("reviewTradingHistory").GetBoolean()) result.Add(new("UW-22", "trading-history"));
        return result.AsReadOnly();
    }

    // Review needs exist independently of an actor's ability to resolve them.
    public static IReadOnlyList<UnderwritingRequirement> SourceReferrals(UnderwritingRisk risk, int minimumTradingYears)
    {
        ValidateRisk(risk);
        if (minimumTradingYears is < 0 or > 100) throw new ArgumentException("Invalid trading threshold.");
        var result = new List<UnderwritingRequirement>();
        if (risk.TradingYears < minimumTradingYears) result.Add(new("UW-22", "trading-history"));
        if (risk.HasValeting && risk.VehicleLimit > 50000m) result.Add(new("UW-09", "vehicle-limit", RequestedAmount: risk.VehicleLimit, AuthorisedAmount: 50000m));
        foreach (var driver in risk.Drivers.OrderBy(x => x.Id))
        {
            if (driver.HasConvictions) result.Add(new("conviction-history", "conviction-history", driver.Id));
            if (driver.HasClaims) result.Add(new("claims-history", "claims-history", driver.Id));
        }
        if (risk.HasClaims && !risk.Drivers.Any(x => x.HasClaims)) result.Add(new("claims-history", "claims-history"));
        return result.AsReadOnly();
    }

    public static IReadOnlyList<QuoteFieldIssue> ValidateSections(JsonElement cover, string productCode, IReadOnlyCollection<Guid> currentPremises)
    {
        var issues = new List<QuoteFieldIssue>();
        void Add(string code, string path) { if (issues.Count < QuoteCaptureShape.MaximumIssues) issues.Add(new(code, path)); }
        const string root = "/cover/requestedSections";
        if (productCode is not ("motor-trade-road-risks" or "motor-trade-combined")) return [new("unsupported-product", "/productCode")];
        if (cover.ValueKind != JsonValueKind.Object || !cover.TryGetProperty("requestedSections", out var sections)) return [new("requested-sections-required", root)];
        if (sections.ValueKind != JsonValueKind.Array || sections.GetArrayLength() > 3) Add("invalid-requested-sections", root);
        if (sections.ValueKind != JsonValueKind.Array) return issues;
        var codes = new HashSet<string>(StringComparer.Ordinal); var ids = new HashSet<Guid>(); var index = 0;
        foreach (var section in sections.EnumerateArray())
        {
            var path = root + "/" + index++;
            if (section.ValueKind != JsonValueKind.Object) { Add("invalid-requested-section", path); continue; }
            string? Text(string key) => section.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
            var code = Text("code");
            if (code is null || !Covers.Skip(1).Contains(code) || (productCode == "motor-trade-road-risks" && code != "tools-equipment")) Add("section-not-supported", path + "/code");
            if (code is not null && !codes.Add(code)) Add("duplicate-requested-section", path + "/code");
            if (!Guid.TryParseExact(Text("id"), "D", out var id) || id == Guid.Empty || !ids.Add(id)) Add("invalid-section-identity", path + "/id");
            if (!section.TryGetProperty("selected", out var selected) || selected.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { Add("section-selection-required", path + "/selected"); continue; }
            var keys = new HashSet<string>(["id", "code", "selected"], StringComparer.Ordinal);
            if (selected.GetBoolean())
            {
                keys.UnionWith(["limit", "excess"]);
                void MoneyField(string key, bool positive)
                {
                    try { var amount = Money.Parse(Text(key)!); if (amount.Pence < 0 || (positive && amount.Pence == 0)) Add("invalid-section-amount", path + "/" + key); }
                    catch (Exception error) when (error is ArgumentException or FormatException or OverflowException) { Add("invalid-section-amount", path + "/" + key); }
                }
                MoneyField("limit", true); MoneyField("excess", false);
                if (code == "stock-custody") { keys.Add("anyOneVehicleLimit"); MoneyField("anyOneVehicleLimit", true); }
                if (code == "premises")
                {
                    keys.Add("premisesIds");
                    if (!section.TryGetProperty("premisesIds", out var targets) || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < 1 or > 100) Add("section-premises-required", path + "/premisesIds");
                    else
                    {
                        var seen = new HashSet<Guid>();
                        foreach (var target in targets.EnumerateArray())
                            if (target.ValueKind != JsonValueKind.String || !Guid.TryParseExact(target.GetString(), "D", out var targetId) || !seen.Add(targetId) || !currentPremises.Contains(targetId))
                                Add("foreign-section-premises", path + "/premisesIds");
                    }
                }
            }
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in section.EnumerateObject()) if (!keys.Contains(field.Name) || !seenKeys.Add(field.Name)) Add("invalid-section-field", path);
        }
        foreach (var code in productCode == "motor-trade-combined" ? Covers.Skip(1) : ["tools-equipment"])
            if (!codes.Contains(code)) Add("section-selection-required", root + "/" + code);
        return issues.AsReadOnly();
    }

    private static void ValidateRisk(UnderwritingRisk risk)
    {
        bool MoneyValue(decimal value) => value >= 0 && value <= QuoteRatingRules.MaximumMoney && decimal.Round(value, 2) == value;
        if (!MoneyValue(risk.AnnualPremium) || risk.AnnualPremium == 0 || !MoneyValue(risk.StockLimit) || !MoneyValue(risk.VehicleLimit) ||
            risk.TradingYears is < 0 or > 300 || risk.TradeValues.Count == 0 || risk.TradeValues.Any(x => x <= 0) || risk.TradeValues.Distinct().Count() != risk.TradeValues.Count ||
            risk.Drivers.Any(x => x.Id == Guid.Empty || x.Age is < 16 or > 100 || x.LicenceYears < 0 || x.LicenceYears > x.Age - 16) || risk.Drivers.Select(x => x.Id).Distinct().Count() != risk.Drivers.Count ||
            !risk.CoverLimits.ContainsKey("road-risks") || risk.CoverLimits.Any(x => !Covers.Contains(x.Key) || !MoneyValue(x.Value)))
            throw new ArgumentException("Validated underwriting facts are required.");
    }
}
