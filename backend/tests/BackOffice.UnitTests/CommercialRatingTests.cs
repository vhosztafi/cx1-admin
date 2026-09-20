using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Underwriting;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialRatingTests
{
    [Fact]
    public void RetainedMotorTradeFactorSerializationDoesNotAcquireANullMultiplier()
    {
        const string json = "{\"code\":\"base\",\"amount\":600,\"direction\":\"charge\",\"basisAmount\":600,\"basisPoints\":null}";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var factor = JsonSerializer.Deserialize<RatingFactor>(json, options)!;
        Assert.Equal(json, JsonSerializer.Serialize(factor, options));
    }

    [Fact]
    public void ProjectionUsesOnlyValidatedSavedQuestionsAndRejectsForgedPremiumOrReference()
    {
        using var stream = typeof(CommercialRatingTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var document = JsonDocument.Parse(stream);
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", CommercialCaptureRules.QuestionVersion, CommercialCaptureRules.ReferenceVersion);
        var projected = CommercialUnderwritingInput.Project(document.RootElement, pins, Configuration(), new(2026, 9, 16));
        Assert.Equal(2, projected.Rating.Locations.Count);
        Assert.Equal(2, projected.Rating.Wages.Count);
        Assert.Equal(500000.01m, projected.Rating.BiSumInsured);
        Assert.Equal(24, projected.Rating.IndemnityMonths);
        Assert.Contains(projected.Rating.Extensions, x => x.Code == "named-suppliers" && x.Limit == 10000.01m);
        Assert.True(CommercialRatingRules.Calculate(Configuration(), projected.Rating, projected.Term, 1500).AnnualPremium > 0);
        var forged = JsonNode.Parse(document.RootElement.GetRawText())!; forged["premium"] = "1.00";
        Assert.Throws<QuoteValidationException>(() => CommercialUnderwritingInput.Project(JsonSerializer.SerializeToElement(forged), pins, Configuration(), new(2026, 9, 16)));
        forged = JsonNode.Parse(document.RootElement.GetRawText())!;
        forged["risk"]!["wages"]![0]!["category"]!["label"] = "Invented authority";
        Assert.Throws<QuoteValidationException>(() => CommercialUnderwritingInput.Project(JsonSerializer.SerializeToElement(forged), pins, Configuration(), new(2026, 9, 16)));
    }

    internal static JsonElement Configuration()
    {
        using var stream = typeof(CommercialRatingTests).Assembly.GetManifestResourceStream("CommercialExamples.Underwriting")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("rating").Clone();
    }

    internal static CommercialRatingFacts Facts() => new(
        [new(Guid.Parse("10000000-0000-0000-0000-000000000001"), 1500000m, 350000m, 150000m)],
        true, 500000m, 12, true, 10000000m,
        [new(Guid.Parse("20000000-0000-0000-0000-000000000001"), "manual-on-premises", 300000m, 50000m, 100000m)],
        1500000m, 5000000m, 5000000m, 0m, 0m, 0m, false, [], false, false, false);

    [Fact]
    public void IndependentAnnualExampleReconcilesEverySettlementAmount()
    {
        var result = CommercialRatingRules.Calculate(Configuration(), Facts(), QuoteRatingRulesTests.Term(), 1500);
        Assert.Equal(4195m, result.AnnualPremium);
        Assert.Equal(4195m, result.TermPremium);
        Assert.Equal(503.40m, result.Tax);
        Assert.Equal(75m, result.Fee);
        Assert.Equal(4773.40m, result.GrossPayable);
        Assert.Equal(629.25m, result.BrokerCommission);
        Assert.Equal(4144.15m, result.GrossPayable - result.BrokerCommission);
        Assert.Equal(result.AnnualPremium, result.Factors.Sum(x => x.Amount));
    }

    [Fact]
    public void DisabledElAndBiDoNotChargeRetainedDeclaredDetails()
    {
        var result = CommercialRatingRules.Calculate(Configuration(), Facts() with { EmployersSelected = false, BiSelected = false }, QuoteRatingRulesTests.Term(), 1500);
        Assert.Equal(2770m, result.AnnualPremium);
        Assert.Equal(332.40m, result.Tax);
        Assert.Equal(3177.40m, result.GrossPayable);
        Assert.Equal(415.50m, result.BrokerCommission);
        Assert.DoesNotContain(result.Factors, x => x.Code.StartsWith("wage/") || x.Code == "business-interruption");
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z", "2026-06-29T23:00:00Z", 180, 365, 2068.77)]
    [InlineData("2028-01-01T00:00:00Z", "2028-01-02T12:00:00Z", 1.5, 366, 17.19)]
    public void SharedCivilTermHandlesDstLeapAndPartialDays(string start, string end, decimal days, decimal year, decimal premium)
    {
        var result = CommercialRatingRules.Calculate(Configuration(), Facts(), QuoteRatingRulesTests.Term(start, end, "short-period"), 1500);
        Assert.Equal(days, result.CivilDays); Assert.Equal(year, result.AnnualCivilDays);
        Assert.Equal(premium, result.TermPremium); Assert.Equal(75m, result.Fee);
    }

    [Fact]
    public void LoadingsUseTheSameSubtotalWithoutCompounding()
    {
        var result = CommercialRatingRules.Calculate(Configuration(), Facts() with { ReferredConstruction = true, ReferredFlood = true, LossHistory = true }, QuoteRatingRulesTests.Term(), 1500);
        Assert.Equal(6082.75m, result.AnnualPremium); //4195 +629.25 +839 +419.50
    }

    [Fact]
    public void UnknownCategoriesDuplicateRowsAndSubPennyMoneyAreRejected()
    {
        var facts = Facts();
        foreach (var invalid in new[] {
            facts with { Wages = [facts.Wages[0] with { Category = "invented" }] },
            facts with { Locations = [facts.Locations[0], facts.Locations[0]] },
            facts with { Turnover = 1.001m },
            facts with { Locations = [facts.Locations[0] with { Buildings = decimal.MaxValue }] },
            facts with { PublicLimit = 123456m },
            facts with { BiSelected = true, IndemnityMonths = 13 }
        }) Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), invalid, QuoteRatingRulesTests.Term(), 1500));
    }

    [Fact]
    public void ConfigurationIsClosedAndCannotOmitACategoryOrChangeTheProduct()
    {
        Assert.True(CommercialUnderwritingConfiguration.Valid(Configuration(), "rating"));
        foreach (var field in new[] { "unexpected", "productCode", "wageRates" })
        {
            var config = JsonNode.Parse(Configuration().GetRawText())!;
            if (field == "wageRates") config[field]!.AsObject().Remove("warehouse");
            else config[field] = field == "productCode" ? "motor-trade-combined" : "untrusted";
            Assert.False(CommercialUnderwritingConfiguration.Valid(JsonSerializer.SerializeToElement(config), "rating"));
        }
    }

    [Fact]
    public void EachLocationRoundsHalfPenniesBeforeSummingAndMinimumIsAnExplicitFactor()
    {
        var facts = Facts() with {
            Locations = [new(Guid.NewGuid(), 10m, 0m, 0m), new(Guid.NewGuid(), 10m, 0m, 0m)],
            BiSelected = false, EmployersSelected = false, PublicLimit = 0m, ProductsLimit = 0m
        };
        var calculated = CommercialRatingRules.Calculate(Configuration(), facts, QuoteRatingRulesTests.Term(), 0, 0m);
        Assert.Equal(250.02m, calculated.AnnualPremium);
        Assert.Equal(2, calculated.Factors.Count(x => x.Code.EndsWith("/buildings") && x.Amount == .01m));
        var minimum = CommercialRatingRules.Calculate(Configuration(), facts, QuoteRatingRulesTests.Term(), 0);
        Assert.Equal(500m, minimum.AnnualPremium);
        Assert.Equal(249.98m, Assert.Single(minimum.Factors, x => x.Code == "minimum-premium").Amount);
    }

    [Theory]
    [InlineData("clerical", 265)]
    [InlineData("warehouse", 285)]
    [InlineData("drivers", 295)]
    [InlineData("woodworking", 320)]
    [InlineData("height", 350)]
    [InlineData("heat-away", 360)]
    [InlineData("manual-on-premises", 310)]
    [InlineData("manual-away", 330)]
    public void EverySourceWageCategoryHasDistinctPublishedEmployeeAndLoscRates(string category, decimal expected)
    {
        var facts = Facts() with { Locations = [new(Guid.NewGuid(), 0m, 0m, 0m)], BiSelected = false,
            Wages = [new(Guid.NewGuid(), category, 10000m, 10000m, 999999m)], PublicLimit = 0m, ProductsLimit = 0m };
        var result = CommercialRatingRules.Calculate(Configuration(), facts, QuoteRatingRulesTests.Term(), 0, 0m);
        Assert.Equal(expected, result.AnnualPremium);
        Assert.Equal(0m, Assert.Single(result.Factors, x => x.Code.EndsWith("/bona-fide")).Amount);
    }

    [Fact]
    public void SelectedAdditionalCoversAndBiExtensionsArePricedAndBounded()
    {
        var facts = Facts() with { ContractWorks = 100000m, GoodsInTransit = 5000m, Money = 1000m, Glass = true,
            Extensions = [new("denial-of-access", 100000m)] };
        var result = CommercialRatingRules.Calculate(Configuration(), facts, QuoteRatingRulesTests.Term(), 1500);
        Assert.Equal(4550m, result.AnnualPremium); //4195+150+50+20+35+100
        Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), facts with { Extensions = [new("denial-of-access", 100000.01m)] }, QuoteRatingRulesTests.Term(), 0));
        Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), facts with { Extensions = [new("invented", 10m)] }, QuoteRatingRulesTests.Term(), 0));
    }

    [Fact]
    public void LargeMoneyAndCommissionBoundariesFailWithoutOverflowOrSilentClamping()
    {
        var facts = Facts() with { Locations = [new(Guid.NewGuid(), QuoteRatingRules.MaximumMoney, QuoteRatingRules.MaximumMoney, QuoteRatingRules.MaximumMoney)] };
        Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), facts, QuoteRatingRulesTests.Term(), 1500));
        Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), Facts(), QuoteRatingRulesTests.Term(), 10001));
        Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), Facts(), QuoteRatingRulesTests.Term(), 1500, 100000.01m));
        Assert.Throws<ArgumentException>(() => CommercialRatingRules.Calculate(Configuration(), Facts(), QuoteRatingRulesTests.Term(), 1500, .001m));
    }
}
