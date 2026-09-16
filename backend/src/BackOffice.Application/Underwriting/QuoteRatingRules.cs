using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Underwriting;

// These facts must come from the held, validated proposal and trusted reference
// projection. They are never accepted as an HTTP request or provider override.
public sealed record RatingFacts(string ProductCode, int DriverCount, int VehicleCount, decimal StockLimit,
    bool PremisesSelected, bool ToolsSelected, bool HasClaims, bool HasValeting, int YoungestDriverAge, int NoClaimsYears);
public sealed record RatingFactor(string Code, decimal Amount, string Direction, decimal BasisAmount, int? BasisPoints = null);
public sealed record CalculatedQuoteRating(decimal AnnualPremium, decimal TermPremium, decimal Tax, decimal Fee,
    decimal GrossPayable, decimal BrokerCommission, decimal CivilDays, decimal AnnualCivilDays, IReadOnlyList<RatingFactor> Factors);

public static class QuoteRatingRules
{
    public const decimal MaximumMoney = 9999999999999.99m;
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    public static CalculatedQuoteRating Calculate(JsonElement config, RatingFacts facts, ResolvedQuoteTerm term,
        int commissionRateBps, decimal? approvedMinimumPremium = null)
    {
        if (!UnderwritingConfiguration.Valid(config, "rating") || config.GetProperty("productCode").GetString() != facts.ProductCode ||
            facts.DriverCount is < 0 or > 1000 || facts.VehicleCount is < 0 or > 10000 ||
            facts.StockLimit is < 0 or > MaximumMoney || decimal.Round(facts.StockLimit, 2) != facts.StockLimit ||
            facts.YoungestDriverAge is < 16 or > 100 || facts.NoClaimsYears is < 0 or > 100 || commissionRateBps is < 0 or > 10000 ||
            approvedMinimumPremium is < 0 or > MaximumMoney ||
            (approvedMinimumPremium.HasValue && Round(approvedMinimumPremium.Value) != approvedMinimumPremium.Value) ||
            (facts.ProductCode == "motor-trade-road-risks" && (facts.StockLimit != 0 || facts.PremisesSelected)))
            throw new ArgumentException("Invalid typed rating input or configuration.");
        var (days, annualDays) = CivilDuration(term);
        decimal Amount(string key) => UnderwritingConfiguration.Amount(config, key);
        int Rate(string key) => config.GetProperty(key).GetInt32();
        var factors = new List<RatingFactor>();
        void Charge(string code, decimal amount) { if (amount > 0) factors.Add(new(code, amount, "charge", amount)); }
        Charge("base", Amount("basePremium"));
        Charge("additional-drivers", checked(Math.Max(0, facts.DriverCount - 1) * Amount("driverAdditionalPremium")));
        Charge("additional-vehicles", checked(Math.Max(0, facts.VehicleCount - 1) * Amount("vehicleAdditionalPremium")));
        Charge("stock", Round(checked(facts.StockLimit * Rate("stockRateBps") / 10000m)));
        if (facts.PremisesSelected) Charge("premises", Amount("premisesPremium"));
        if (facts.ToolsSelected) Charge("tools-equipment", Amount("toolsPremium"));
        var subtotal = factors.Sum(x => x.Amount);
        void Adjustment(string code, string setting, bool applies, bool discount = false)
        {
            if (!applies) return;
            var rate = Rate(setting); var amount = Round(checked(subtotal * rate / 10000m));
            if (amount > 0) factors.Add(new(code, amount, discount ? "discount" : "charge", subtotal, rate));
        }
        Adjustment("claims", "claimsLoadingBps", facts.HasClaims);
        Adjustment("valeting", "valetingLoadingBps", facts.HasValeting);
        Adjustment("young-driver", "youngDriverLoadingBps", facts.YoungestDriverAge < Rate("youngDriverAge"));
        Adjustment("no-claims-discount", "noClaimsDiscountBps", !facts.HasClaims && facts.NoClaimsYears >= Rate("noClaimsDiscountYears"), true);
        var raw = factors.Sum(x => x.Direction == "discount" ? -x.Amount : x.Amount);
        var minimum = approvedMinimumPremium ?? Amount("minimumPremium");
        if (minimum > raw) Charge("minimum-premium", minimum - raw);
        var annual = Math.Max(raw, minimum);
        if (annual > Amount("maximumAnnualPremium") || annual > MaximumMoney || annual <= 0)
            throw new ArgumentException("Annual premium is outside the configured range.");
        var premium = term.Kind == "annual" ? annual : Round(checked(annual * days / annualDays));
        var tax = Round(checked(premium * Rate("taxRateBps") / 10000m));
        var fee = Amount("fee"); var gross = checked(premium + tax + fee);
        if (gross > MaximumMoney || premium <= 0) throw new ArgumentException("Rated amount is outside the supported range.");
        return new(annual, premium, tax, fee, gross, Round(checked(premium * commissionRateBps / 10000m)), days, annualDays, factors.AsReadOnly());
    }

    public static (decimal Days, decimal AnnualDays) CivilDuration(ResolvedQuoteTerm term)
    {
        if (term.TimeZone != "Europe/London" || term.Kind is not ("annual" or "short-period") || term.StartsAt >= term.EndsAt)
            throw new ArgumentException("Invalid insurance term.");
        var start = TimeZoneInfo.ConvertTime(term.StartsAt, London).DateTime;
        var end = TimeZoneInfo.ConvertTime(term.EndsAt, London).DateTime;
        if (start.Year == 9999) throw new ArgumentException("Invalid insurance anniversary.");
        var anniversary = start.AddYears(1);
        if (end <= start || end > anniversary || (term.Kind == "annual" && end != anniversary))
            throw new ArgumentException("Term must end by its London civil anniversary.");
        return ((end.Ticks - start.Ticks) / (decimal)TimeSpan.TicksPerDay, (anniversary.Ticks - start.Ticks) / (decimal)TimeSpan.TicksPerDay);
    }
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
