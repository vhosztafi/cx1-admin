using System.Globalization;
using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Underwriting;

// Produced only from a held, validated CC revision. These records are not an API request.
public sealed record CommercialLocationRating(Guid Id, decimal Buildings, decimal Contents, decimal Stock);
public sealed record CommercialWageRating(Guid Id, string Category, decimal Employees, decimal LabourOnly, decimal BonaFide);
public sealed record CommercialExtensionRating(string Code, decimal Limit);
public sealed record CommercialRatingFacts(IReadOnlyList<CommercialLocationRating> Locations,
    bool BiSelected, decimal BiSumInsured, int IndemnityMonths, bool EmployersSelected, decimal EmployersLimit,
    IReadOnlyList<CommercialWageRating> Wages, decimal Turnover, decimal PublicLimit, decimal ProductsLimit,
    decimal ContractWorks, decimal GoodsInTransit, decimal Money, bool Glass,
    IReadOnlyList<CommercialExtensionRating> Extensions, bool ReferredConstruction, bool ReferredFlood, bool LossHistory);

public static class CommercialRatingRules
{
    public static CalculatedQuoteRating Calculate(JsonElement config, CommercialRatingFacts facts, ResolvedQuoteTerm term,
        int commissionRateBps, decimal? approvedMinimumPremium = null)
    {
        if (!CommercialUnderwritingConfiguration.Valid(config, "rating") || commissionRateBps is < 0 or > 10000 ||
            (approvedMinimumPremium.HasValue && !Money(approvedMinimumPremium.Value))) throw Invalid();
        Validate(config, facts);
        var (days, annualDays) = QuoteRatingRules.CivilDuration(term);
        decimal Amount(string key) => UnderwritingConfiguration.Amount(config, key);
        int Rate(string key) => config.GetProperty(key).GetInt32();
        int Multiplier(string table, decimal limit) => config.GetProperty(table).GetProperty(limit.ToString("F2", CultureInfo.InvariantCulture)).GetInt32();
        var factors = new List<RatingFactor>();
        void Charge(string code, decimal basis, int rate, int multiplier = 10000)
        {
            var amount = Round(checked(basis * rate * multiplier / 100000000m));
            factors.Add(new(code, amount, "charge", basis, rate, multiplier == 10000 ? null : multiplier));
        }
        factors.Add(new("base", Amount("basePremium"), "charge", Amount("basePremium")));
        foreach (var row in facts.Locations.OrderBy(x => x.Id))
        {
            Charge($"location/{row.Id:D}/buildings", row.Buildings, Rate("buildingsRateBps"));
            Charge($"location/{row.Id:D}/contents", row.Contents, Rate("contentsRateBps"));
            Charge($"location/{row.Id:D}/stock", row.Stock, Rate("stockRateBps"));
        }
        if (facts.BiSelected)
        {
            Charge("business-interruption", facts.BiSumInsured, Rate("businessInterruptionRateBps"),
                config.GetProperty("indemnityMonthsFactorsBps").GetProperty(facts.IndemnityMonths.ToString(CultureInfo.InvariantCulture)).GetInt32());
            foreach (var extension in facts.Extensions.OrderBy(x => x.Code, StringComparer.Ordinal))
                Charge("bi-extension/" + extension.Code, extension.Limit, config.GetProperty("extensions").GetProperty(extension.Code).GetProperty("rateBps").GetInt32());
        }
        if (facts.EmployersSelected)
        {
            var multiplier = Multiplier("employersLimitFactorsBps", facts.EmployersLimit);
            foreach (var row in facts.Wages.OrderBy(x => x.Id))
            {
                var rates = config.GetProperty("wageRates").GetProperty(row.Category);
                Charge($"wage/{row.Id:D}/employees", row.Employees, rates.GetProperty("employees").GetInt32(), multiplier);
                Charge($"wage/{row.Id:D}/labour-only", row.LabourOnly, rates.GetProperty("labourOnly").GetInt32(), multiplier);
                Charge($"wage/{row.Id:D}/bona-fide", row.BonaFide, Rate("bonaFideRateBps"), multiplier);
            }
        }
        if (facts.PublicLimit > 0) Charge("public-liability", facts.Turnover, Rate("publicLiabilityTurnoverRateBps"), Multiplier("liabilityLimitFactorsBps", facts.PublicLimit));
        if (facts.ProductsLimit > 0) Charge("products-liability", facts.Turnover, Rate("productsLiabilityTurnoverRateBps"), Multiplier("liabilityLimitFactorsBps", facts.ProductsLimit));
        if (facts.ContractWorks > 0) Charge("contract-works", facts.ContractWorks, Rate("contractWorksRateBps"));
        if (facts.GoodsInTransit > 0) Charge("goods-in-transit", facts.GoodsInTransit, Rate("goodsInTransitRateBps"));
        if (facts.Money > 0) Charge("money", facts.Money, Rate("moneyRateBps"));
        if (facts.Glass) factors.Add(new("glass", Amount("glassPremium"), "charge", Amount("glassPremium")));
        var subtotal = factors.Sum(x => x.Amount);
        if (facts.ReferredConstruction) Charge("construction-loading", subtotal, Rate("referredConstructionLoadingBps"));
        if (facts.ReferredFlood) Charge("flood-loading", subtotal, Rate("referredFloodLoadingBps"));
        if (facts.LossHistory) Charge("loss-history-loading", subtotal, Rate("lossHistoryLoadingBps"));
        var annual = factors.Sum(x => x.Amount);
        var minimum = approvedMinimumPremium ?? Amount("minimumPremium");
        if (annual < minimum) { factors.Add(new("minimum-premium", minimum - annual, "charge", annual)); annual = minimum; }
        if (annual <= 0 || annual > Amount("maximumAnnualPremium") || annual > QuoteRatingRules.MaximumMoney) throw Invalid();
        var premium = term.Kind == "annual" ? annual : Round(checked(annual * days / annualDays));
        var tax = Round(checked(premium * Rate("taxRateBps") / 10000m));
        var fee = Amount("newBusinessFee");
        var gross = checked(premium + tax + fee);
        if (premium <= 0 || gross > QuoteRatingRules.MaximumMoney) throw Invalid();
        return new(annual, premium, tax, fee, gross, Round(checked(premium * commissionRateBps / 10000m)), days, annualDays, factors.AsReadOnly());
    }

    private static void Validate(JsonElement config, CommercialRatingFacts facts)
    {
        if (facts is null || facts.Locations is null || facts.Wages is null || facts.Extensions is null ||
            facts.Locations.Count is < 1 or > 100 || facts.Wages.Count > 100 || facts.Extensions.Count > 5 ||
            facts.Locations.Any(x => x is null || x.Id == Guid.Empty || !Money(x.Buildings) || !Money(x.Contents) || !Money(x.Stock)) ||
            facts.Locations.Select(x => x.Id).Distinct().Count() != facts.Locations.Count ||
            facts.Wages.Any(x => x is null || x.Id == Guid.Empty || string.IsNullOrEmpty(x.Category) || !config.GetProperty("wageRates").TryGetProperty(x.Category, out _) || !Money(x.Employees) || !Money(x.LabourOnly) || !Money(x.BonaFide)) ||
            facts.Wages.Select(x => x.Id).Distinct().Count() != facts.Wages.Count ||
            new[] { facts.BiSumInsured, facts.EmployersLimit, facts.Turnover, facts.PublicLimit, facts.ProductsLimit, facts.ContractWorks, facts.GoodsInTransit, facts.Money }.Any(x => !Money(x))) throw Invalid();
        bool Limit(string table, decimal value) => config.GetProperty(table).TryGetProperty(value.ToString("F2", CultureInfo.InvariantCulture), out _);
        if (facts.BiSelected && (facts.BiSumInsured <= 0 || !config.GetProperty("indemnityMonthsFactorsBps").TryGetProperty(facts.IndemnityMonths.ToString(CultureInfo.InvariantCulture), out _)) ||
            facts.EmployersSelected && (facts.Wages.Count == 0 || !Limit("employersLimitFactorsBps", facts.EmployersLimit)) ||
            facts.PublicLimit > 0 && !Limit("liabilityLimitFactorsBps", facts.PublicLimit) ||
            facts.ProductsLimit > 0 && !Limit("liabilityLimitFactorsBps", facts.ProductsLimit)) throw Invalid();
        if (facts.Extensions.Select(x => x?.Code).Distinct(StringComparer.Ordinal).Count() != facts.Extensions.Count ||
            facts.Extensions.Any(x => x is null || string.IsNullOrEmpty(x.Code) || !Money(x.Limit) || x.Limit == 0 ||
                !config.GetProperty("extensions").TryGetProperty(x.Code, out var rule) || x.Limit > UnderwritingConfiguration.Amount(rule, "limit"))) throw Invalid();
    }
    private static bool Money(decimal value) => value is >= 0 and <= QuoteRatingRules.MaximumMoney && Round(value) == value;
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static ArgumentException Invalid() => new("Invalid Commercial Combined rating input or configuration.");
}
