using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingRulesTests
{
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Start = At("2026-01-01T00:00:00Z"), End = At("2027-01-01T00:00:00Z");

    [Fact]
    public void CutoffsExcludeFutureUnknownAndDraftVersionsAndBreakTiesByTransaction()
    {
        var first = new ServicingVersionCandidate(Guid.NewGuid(), Start, Start, 1, 1, true);
        var backdated = new ServicingVersionCandidate(Guid.NewGuid(), At("2026-03-01T00:00:00Z"), At("2026-04-01T00:00:00Z"), 2, 1, true);
        var replacement = backdated with { Id = Guid.NewGuid(), TransactionSequence = 3 };
        var future = replacement with { Id = Guid.NewGuid(), EffectiveAt = At("2026-09-01T00:00:00Z"), TransactionSequence = 4 };
        var draft = replacement with { Id = Guid.NewGuid(), TransactionSequence = 5, Issued = false };
        ServicingVersionCandidate[] all = [future, draft, replacement, first, backdated];
        Assert.Equal(first.Id, ServicingRules.SelectVersion(all, At("2026-03-15T00:00:00Z"), At("2026-03-20T00:00:00Z"))!.Id);
        Assert.Equal(replacement.Id, ServicingRules.SelectVersion(all, At("2026-03-15T00:00:00Z"), At("2026-04-01T00:00:00Z"))!.Id);
        Assert.Equal(future.Id, ServicingRules.SelectVersion(all, future.EffectiveAt, future.ProcessedAt)!.Id);
        Assert.Null(ServicingRules.SelectVersion(all, Start.AddTicks(-1), End));
    }

    [Theory]
    [InlineData("2026-03-29", "01:30", "nonexistent-local-time")]
    [InlineData("2026-10-25", "01:30", "ambiguous-local-time")]
    public void ExistingLondonResolverRejectsGapAndRequiresFoldChoice(string date, string time, string code)
        => Assert.Equal(code, QuoteTerm.ResolveLondonTime(date, time).Code);

    [Fact]
    public void FoldChoicesAreDistinctInstants()
    {
        var early = QuoteTerm.ResolveLondonTime("2026-10-25", "01:30", 60);
        var late = QuoteTerm.ResolveLondonTime("2026-10-25", "01:30", 0);
        Assert.Null(early.Code); Assert.Null(late.Code);
        Assert.Equal(TimeSpan.FromHours(1), late.Instant!.Value - early.Instant!.Value);
    }

    [Theory]
    [InlineData(600, "2026-09-15", "2027-01-01", "177.53")]
    [InlineData(-300, "2026-10-01", "2027-01-01", "-75.62")]
    [InlineData(1200, "2026-01-01", "2026-04-01", "295.89")]
    [InlineData(1200, "2024-01-01", "2025-01-01", "1200.00")]
    public void AnnualDeltaUsesLocalCalendarDenominator(decimal annual, string effective, string end, string expected)
    {
        var date = DateOnly.Parse(effective, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            ServicingRules.ProrateAnnualDelta(annual, new(date.Year, 1, 1), date, DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void MixedDateCancellationReversesOriginalPostedComponentsIncludingNegativeMovement()
    {
        DateOnly cancel = new(2026, 10, 15), end = new(2027, 1, 1);
        var premium = ServicingRules.ReverseUnearned(1200m, new(2026, 1, 1), end, cancel)
            + ServicingRules.ReverseUnearned(177.53m, new(2026, 9, 15), end, cancel)
            + ServicingRules.ReverseUnearned(-75.62m, new(2026, 10, 1), end, cancel);
        var tax = ServicingRules.ReverseUnearned(144m, new(2026, 1, 1), end, cancel)
            + ServicingRules.ReverseUnearned(21.30m, new(2026, 9, 15), end, cancel)
            + ServicingRules.ReverseUnearned(-9.07m, new(2026, 10, 1), end, cancel);
        var commission = ServicingRules.ReverseUnearned(120m, new(2026, 1, 1), end, cancel)
            + ServicingRules.ReverseUnearned(17.75m, new(2026, 9, 15), end, cancel)
            + ServicingRules.ReverseUnearned(-7.56m, new(2026, 10, 1), end, cancel);
        Assert.Equal(-320.55m, premium); Assert.Equal(-38.46m, tax); Assert.Equal(-32.05m, commission);
        Assert.Equal(-326.96m, premium + tax - commission);
    }

    [Fact]
    public void ReturnsClampAtOriginalIntervalAndRoundHalfAwayFromZero()
    {
        DateOnly start = new(2026, 1, 1), end = new(2026, 1, 3);
        Assert.Equal(-0.01m, ServicingRules.ReverseUnearned(0.01m, start, end, start.AddDays(1)));
        Assert.Equal(0.01m, ServicingRules.ReverseUnearned(-0.01m, start, end, start.AddDays(1)));
        Assert.Equal(-100m, ServicingRules.ReverseUnearned(100m, start, end, start.AddDays(-1)));
        Assert.Equal(0m, ServicingRules.ReverseUnearned(100m, start, end, end));
    }

    [Fact]
    public void ScheduleUsesOnlyCoverOverridesAndProducesCumulativeOrderedChangeSets()
    {
        var common = At("2026-09-15T00:00:00Z"); var later = At("2026-10-01T00:00:00Z");
        var driver = new ServicingScheduledChange(Guid.NewGuid(), Guid.NewGuid(), "driver", common);
        var cover = new ServicingScheduledChange(Guid.NewGuid(), Guid.NewGuid(), "cover", later);
        var slices = ServicingRules.BuildSchedule(Start, End, Start, common, common, false, [cover, driver]);
        Assert.Equal(2, slices.Count); Assert.Equal(common, slices[0].EffectiveAt);
        Assert.Equal([driver.ChangeId], slices[0].ChangeIds);
        Assert.Equal(2, slices[1].ChangeIds.Count); Assert.Contains(driver.ChangeId, slices[1].ChangeIds);
        Assert.Contains(cover.ChangeId, slices[1].ChangeIds);
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, Start, common, common, false, [driver with { EffectiveAt = later }]));
    }

    [Fact]
    public void ScheduleRejectsDuplicateTargetsIdsOutOfTermAndOutOfSequence()
    {
        var common = At("2026-09-15T00:00:00Z");
        var c = new ServicingScheduledChange(Guid.NewGuid(), Guid.NewGuid(), "cover", common);
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, Start, common, common, false, [c, c]));
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, Start, common, common, false, [c, c with { ChangeId = Guid.NewGuid() }]));
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, Start, common, common, false, [c with { EffectiveAt = End }]));
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, common.AddDays(1), common, common, false, [c]));
    }

    [Fact]
    public void DriverBackdateCannotBeOverriddenBySeniorAuthority()
    {
        var common = At("2026-09-15T00:00:00Z"); var now = common.AddDays(1);
        var c = new ServicingScheduledChange(Guid.NewGuid(), Guid.NewGuid(), "cover", common);
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, Start, common, now, false, [c]));
        Assert.Single(ServicingRules.BuildSchedule(Start, End, Start, common, now, true, [c]));
        Assert.Throws<ArgumentException>(() => ServicingRules.BuildSchedule(Start, End, Start, common, now, true, [c with { Kind = "driver" }]));
    }

    [Fact]
    public void UnknownExperienceIsNotAZeroLossRatioAndThresholdUsesExactDecimal()
    {
        Assert.Equal("information-required", ServicingRules.AssessExperience(null, null, null, false));
        Assert.Equal("information-required", ServicingRules.AssessExperience(0m, 0m, 0m, true));
        Assert.Equal("information-required", ServicingRules.AssessExperience(0m, 0m, 100m, false));
        Assert.Equal("within-threshold", ServicingRules.AssessExperience(20m, 30m, 100m, true));
        Assert.Equal("senior-referral", ServicingRules.AssessExperience(20.01m, 30m, 100m, true));
        Assert.Throws<ArgumentException>(() => ServicingRules.AssessExperience(-1m, 0m, 100m, true));
    }

    [Fact]
    public void CancellationBlocksLaterTermsFutureSlicesAndInsufficientLocalNotice()
    {
        var effective=At("2026-09-15T00:00:00Z");
        Assert.Equal("later-term-issued", ServicingRules.CancellationDateBlock(Start, End, Start, effective, true, new(2026,9,1), new(2026,9,15), 7));
        Assert.Equal("before-latest-issued-slice", ServicingRules.CancellationDateBlock(Start, End, effective.AddDays(1), effective, false, new(2026,9,1), new(2026,9,15), 7));
        Assert.Equal("notice-period-incomplete", ServicingRules.CancellationDateBlock(Start, End, Start, effective, false, new(2026,9,10), new(2026,9,15), 7));
        Assert.Null(ServicingRules.CancellationDateBlock(Start, End, Start, effective, false, new(2026,9,8), new(2026,9,15), 7));
    }

    [Fact]
    public void MixedDateAdjustmentRoundsComponentsSeparatelyAndChargesOneFee()
    {
        DateOnly start=new(2026,1,1), end=new(2027,1,1);
        var a=ServicingRules.ProrateAnnualDelta(600m,start,new(2026,9,15),end);
        var b=ServicingRules.ProrateAnnualDelta(-300m,start,new(2026,10,1),end);
        var tax=decimal.Round(a*0.12m,2,MidpointRounding.AwayFromZero)+decimal.Round(b*0.12m,2,MidpointRounding.AwayFromZero);
        var commission=decimal.Round(a*0.10m,2,MidpointRounding.AwayFromZero)+decimal.Round(b*0.10m,2,MidpointRounding.AwayFromZero);
        var total=ServicingRules.TotalMovement([a,b],[21.30m,-9.07m],[17.75m,-7.56m],15m);
        Assert.Equal(12.23m,tax); Assert.Equal(10.19m,commission);
        Assert.Equal(129.14m,total.Gross); Assert.Equal(118.95m,total.Net);
    }

    [Fact]
    public void RejectsUnroundedOrOutOfBoundsMoneyAndInvalidIntervals()
    {
        DateOnly start = new(2026, 1, 1), end = start.AddYears(1);
        Assert.Throws<ArgumentException>(() => ServicingRules.ReverseUnearned(0.001m, start, end, start));
        Assert.Throws<ArgumentException>(() => ServicingRules.ReverseUnearned(decimal.MaxValue, start, end, start));
        Assert.Throws<ArgumentException>(() => ServicingRules.ReverseUnearned(1m, start, start, start));
        Assert.Throws<ArgumentException>(() => ServicingRules.ProrateAnnualDelta(1m, start, start, end.AddDays(1)));
    }
}
