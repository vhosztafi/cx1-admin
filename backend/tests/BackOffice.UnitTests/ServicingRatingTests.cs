using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingRatingTests
{
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static ResolvedQuoteTerm Term(string start = "2026-01-01T00:00:00Z", string end = "2027-01-01T00:00:00Z", string kind = "annual")
        => new(kind, At(start), At(end), "Europe/London");
    private static ServicingAnnualSlice Slice(string date, decimal annual, params Guid[] ids) => new(At(date), annual, ids.Length == 0 ? [Guid.NewGuid()] : ids);

    [Fact]
    public void MixedDateFixtureUsesIncrementalCumulativePricesAndOneFee()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var r = ServicingRatingRules.Calculate(Term(), 1200m,
            [Slice("2026-09-14T23:00:00Z", 1800m, a), Slice("2026-09-30T23:00:00Z", 1500m, a, b)], 1200, 1000, 15m);
        Assert.Equal(600m, r.Slices[0].AnnualDelta); Assert.Equal(-300m, r.Slices[1].AnnualDelta);
        Assert.Equal(108, r.Slices[0].RemainingDays); Assert.Equal(92, r.Slices[1].RemainingDays);
        Assert.Equal(177.53m, r.Slices[0].Premium); Assert.Equal(-75.62m, r.Slices[1].Premium);
        Assert.Equal(101.91m, r.Premium); Assert.Equal(12.23m, r.Tax); Assert.Equal(10.19m, r.BrokerCommission);
        Assert.Equal(15m, r.Fee); Assert.Equal(129.14m, r.GrossPayable); Assert.Equal(118.95m, r.NetDue);
        Assert.Equal(Term().EndsAt, r.Slices[0].CoverageEndsAt);
    }

    [Fact]
    public void ReturnPremiumProducesSignedComponentsWithoutImplyingCashRefund()
    {
        var r = ServicingRatingRules.Calculate(Term(), 1200m, [Slice("2026-09-14T23:00:00Z", 600m)], 1200, 1000, 15m);
        Assert.Equal(-177.53m, r.Premium); Assert.Equal(-21.30m, r.Tax); Assert.Equal(-17.75m, r.BrokerCommission);
        Assert.Equal(-183.83m, r.GrossPayable); Assert.Equal(-166.08m, r.NetDue);
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00Z", "2025-01-01T00:00:00Z", "annual", "2024-07-01T23:00:00Z", 183, "600.00")]
    [InlineData("2026-01-01T00:00:00Z", "2026-03-31T23:00:00Z", "short-period", "2026-01-01T00:00:00Z", 90, "295.89")]
    [InlineData("2026-01-01T00:00:00Z", "2027-01-01T00:00:00Z", "annual", "2026-03-28T23:30:00Z", 279, "917.26")]
    public void LocalCalendarDaysIgnoreDstElapsedHoursAndKeepAnnualDenominator(string start, string end, string kind, string effective, int days, string expected)
    {
        var r = ServicingRatingRules.Calculate(Term(start, end, kind), 1200m, [Slice(effective, 2400m)], 0, 0, 0m);
        Assert.Equal(days, r.Slices[0].RemainingDays);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), r.Premium);
    }

    [Theory]
    [InlineData(1, "0.01")]
    [InlineData(-1, "-0.01")]
    public void HalfPennyRoundsAwayFromZeroPerMovement(int sign, string expected)
    {
        var r = ServicingRatingRules.Calculate(Term("2024-01-01T00:00:00Z", "2025-01-01T00:00:00Z"), 10m,
            [Slice("2024-07-01T23:00:00Z", 10m + sign * 0.01m)], 5000, 5000, 0m);
        var value = decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(value, r.Premium); Assert.Equal(value, r.Tax); Assert.Equal(value, r.BrokerCommission);
    }

    [Fact]
    public void NoPremiumChangeStillHasExactlyOneFee()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var r = ServicingRatingRules.Calculate(Term(), 1200m, [Slice("2026-09-15T00:00:00Z", 1200m, a), Slice("2026-10-01T00:00:00Z", 1200m, a, b)], 1200, 1000, 15m);
        Assert.Equal(0m, r.Premium); Assert.Equal(15m, r.GrossPayable);
    }

    [Fact]
    public void RejectsUnorderedRepeatedOutOfTermOrNonCumulativeSlices()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var first = Slice("2026-09-15T00:00:00Z", 1800m, a);
        foreach (var slices in new ServicingAnnualSlice[][] {
            [], [first, first], [Slice("2027-01-01T00:00:00Z", 1800m)],
            [Slice("2025-12-31T00:00:00Z", 1800m)], [first, Slice("2026-09-14T00:00:00Z", 1500m, a, b)],
            [first, Slice("2026-10-01T00:00:00Z", 1500m, b)], [first, Slice("2026-10-01T00:00:00Z", 1500m, a)],
            [Slice("2026-09-15T00:00:00Z", 1500m, a, a)], [Slice("2026-09-15T00:00:00Z", 1500m, Guid.Empty)] })
            Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term(), 1200m, slices, 1200, 1000, 15m));
    }

    [Fact]
    public void RejectsInvalidMoneyRatesTermAndOversizedAggregates()
    {
        var slices = new[] { Slice("2026-09-15T00:00:00Z", 1800m) };
        foreach (var value in new[] { -1m, 0m, 0.001m, decimal.MaxValue })
            Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term(), value, slices, 1200, 1000, 15m));
        Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term(), 1200m, slices, -1, 1000, 15m));
        Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term(), 1200m, slices, 1200, 10001, 15m));
        Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term(), 1200m, slices, 1200, 1000, -1m));
        Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term() with { TimeZone = "UTC" }, 1200m, slices, 1200, 1000, 15m));
        Assert.Throws<ArgumentException>(() => ServicingRatingRules.Calculate(Term(), 1m, [Slice("2026-01-01T00:00:00Z", 9999999999999.99m)], 10000, 10000, 15m));
    }

    [Theory]
    [InlineData("motor-trade-road-risks", 600, "62.90")]
    [InlineData("motor-trade-combined", 1200, "62.90")]
    public void RatesCompleteRisksThroughSharedRulesWithSingleExplicitServicingFee(string product, decimal baseline, string expected)
    {
        var facts = QuoteRatingRulesTests.Facts(product) with { ToolsSelected = true };
        var r = ServicingRatingRules.Rate(QuoteRatingRulesTests.Definition("rating", product), Term(), baseline,
            [new ServicingRiskSlice(At("2026-09-14T23:00:00Z"), [Guid.NewGuid()], facts)], 1000, 15m);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), r.Premium);
        Assert.Equal(15m, r.Fee); Assert.Single(r.Slices);
    }
}
