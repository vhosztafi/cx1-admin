using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteRatingRulesTests
{
    internal static JsonElement Definition(string kind, string product = "motor-trade-road-risks")
    {
        using var stream = typeof(QuoteRatingRulesTests).Assembly.GetManifestResourceStream("UnderwritingExamples.Demo")!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("configurations").EnumerateArray()
            .First(x => x.GetProperty("kind").GetString() == kind && x.GetProperty("productCode").GetString() == product).Clone();
    }
    internal static RatingFacts Facts(string product = "motor-trade-road-risks") => new(product, 1, 1,
        product == "motor-trade-combined" ? 150000m : 0m, product == "motor-trade-combined", false,
        false, false, 35, 0);
    internal static ResolvedQuoteTerm Term(string start = "2026-01-01T00:00:00Z", string end = "2027-01-01T00:00:00Z", string kind = "annual") =>
        new(kind, DateTimeOffset.Parse(start), DateTimeOffset.Parse(end), "Europe/London");

    [Theory]
    [InlineData("motor-trade-road-risks",600,72,707)]
    [InlineData("motor-trade-combined",1200,144,1379)]
    public void SourceWorkedPricesReconcile(string product, decimal premium, decimal tax, decimal gross)
    {
        var result = QuoteRatingRules.Calculate(Definition("rating", product), Facts(product), Term(), 1000);
        Assert.Equal(premium, result.AnnualPremium); Assert.Equal(premium, result.TermPremium);
        Assert.Equal(tax, result.Tax); Assert.Equal(gross, result.GrossPayable);
        Assert.Equal(premium / 10, result.BrokerCommission);
    }

    [Fact]
    public void FactorsAreIndependentlyRoundedAgainstSubtotalAndClaimsRemoveDiscount()
    {
        var config = Definition("rating");
        var result = QuoteRatingRules.Calculate(config, Facts() with { HasValeting = true, YoungestDriverAge = 24, NoClaimsYears = 5 }, Term(), 0);
        Assert.Equal(732m, result.AnnualPremium); //600 +72 +108 -48; no compounding
        Assert.Equal(4, result.Factors.Count);
        var claims = QuoteRatingRules.Calculate(config, Facts() with { HasClaims = true, NoClaimsYears = 10 }, Term(), 0);
        Assert.Equal(660m, claims.AnnualPremium);
        Assert.DoesNotContain(claims.Factors, x => x.Code == "no-claims-discount");
        Assert.Equal(812.58m, QuoteRatingRules.Calculate(config, Facts() with { ToolsSelected = true }, Term(), 0).AnnualPremium);
    }

    [Fact]
    public void ShortTermUsesLondonCivilDaysAcrossDstAndRetainsExactFraction()
    {
        var result = QuoteRatingRules.Calculate(Definition("rating", "motor-trade-combined"), Facts("motor-trade-combined"),
            Term("2026-01-01T00:00:00Z", "2026-06-29T23:00:00Z", "short-period"), 1000);
        Assert.Equal(180m, result.CivilDays); Assert.Equal(365m, result.AnnualCivilDays);
        Assert.Equal(591.78m, result.TermPremium); Assert.Equal(71.01m, result.Tax);
        Assert.Equal(697.79m, result.GrossPayable); Assert.Equal(59.18m, result.BrokerCommission);
    }

    [Fact]
    public void PartialDaysLeapAnniversaryAndPositiveHalfPennyUseExactArithmetic()
    {
        var result = QuoteRatingRules.Calculate(Definition("rating"), Facts(),
            Term("2028-01-01T00:00:00Z", "2028-01-02T12:00:00Z", "short-period"), 1000);
        Assert.Equal(1.5m, result.CivilDays); Assert.Equal(366m, result.AnnualCivilDays);
        Assert.Equal(2.46m, result.TermPremium);
        Assert.Equal(6.01m, QuoteRatingRules.Calculate(Definition("rating"), Facts(), Term(), 100, 600.50m).BrokerCommission);
    }

    [Fact]
    public void RejectsLongShortPeriodWrongProductNegativeFactsAndExcessiveExposure()
    {
        Assert.Throws<ArgumentException>(() => QuoteRatingRules.Calculate(Definition("rating"), Facts(), Term("2026-01-01T00:00:00Z","2027-01-02T00:00:00Z","short-period"), 0));
        Assert.Throws<ArgumentException>(() => QuoteRatingRules.Calculate(Definition("rating"), Facts("motor-trade-combined"), Term(), 0));
        Assert.Throws<ArgumentException>(() => QuoteRatingRules.Calculate(Definition("rating"), Facts() with { DriverCount = -1 }, Term(), 0));
        Assert.Throws<ArgumentException>(() => QuoteRatingRules.Calculate(Definition("rating"), Facts() with { StockLimit = decimal.MaxValue }, Term(), 0));
        Assert.Throws<ArgumentException>(() => QuoteRatingRules.Calculate(Definition("rating"), Facts(), Term(), 10001));
    }
}
