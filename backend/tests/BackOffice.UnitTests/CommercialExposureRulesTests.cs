using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialExposureRulesTests
{
    private static Guid Id(int n) => Guid.Parse($"{n:x8}-0000-4000-8000-000000000001");
    private static readonly Guid Book = Id(1), Policy = Id(2), Term = Id(3);
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2027-01-01T00:00:00Z"), Known = Start.AddMonths(-1);
    private static CommercialExposureSlice Slice(int version, decimal amount, Guid? policy = null) =>
        new(Book, policy ?? Policy, policy is null ? Term : Id(30), Id(version), Start, Start.AddYears(1), Start, Known, 1, 1,
            "new-business", [new(Id(5), "S9", amount)]);
    private static CommercialExposureLimit Limit() => new(Id(100), Book, "*", 1, 40000000m, Start.AddYears(-1), Start.AddYears(3), Known.AddYears(-1), new string('a', 64));

    [Theory]
    [InlineData(" s9 ", "S9")]
    [InlineData("sw1a", "SW1A")]
    [InlineData("GIR", "GIR")]
    [InlineData("EC1A", "EC1A")]
    [InlineData("ZZ9", null)]
    [InlineData("S9 2QT", null)]
    [InlineData("*", null)]
    public void NormalizedOutwardDistrictRejectsPostcodesAndInventedAreas(string input, string? expected) =>
        Assert.Equal(expected, CommercialExposureRules.NormalizeDistrict(input));

    [Fact]
    public void ScheduledExpiredAndCancellationWinnersDoNotContribute()
    {
        var basis = Slice(4, 10000000m);
        var cancellation = basis with { VersionId = Id(6), EffectiveAt = Start.AddDays(14), ProcessedAt = Known.AddDays(1), TransactionSequence = 2,
            TransactionKind = "cancellation", Locations = [] };
        Assert.Empty(CommercialExposureRules.Snapshot([basis, cancellation], Book, Start.AddSeconds(-1), Known.AddDays(2)));
        Assert.Equal(10000000m, Assert.Single(CommercialExposureRules.Snapshot([basis, cancellation], Book, Start, Known.AddDays(2))).PropertySum);
        Assert.Empty(CommercialExposureRules.Snapshot([basis, cancellation], Book, cancellation.EffectiveAt, Known.AddDays(2)));
        Assert.Equal(10000000m, Assert.Single(CommercialExposureRules.Snapshot([basis, cancellation], Book, cancellation.EffectiveAt, Known)).PropertySum);
        Assert.Empty(CommercialExposureRules.Snapshot([basis], Book, basis.TermEndsAt, Known));
    }

    [Fact]
    public void FutureChangesAndEarlyRenewalDoNotReleaseCurrentExposure()
    {
        var basis = Slice(4, 10000000m);
        var change = basis with { VersionId = Id(6), EffectiveAt = Start.AddMonths(1), TransactionSequence = 2, TransactionKind = "adjustment", Locations = [new(Id(5), "S9", 20000000m)] };
        var renewal = basis with { VersionId = Id(7), TermId = Id(8), TermStartsAt = basis.TermEndsAt, TermEndsAt = basis.TermEndsAt.AddYears(1), EffectiveAt = basis.TermEndsAt,
            TransactionKind = "renewal", Locations = [new(Id(5), "S9", 30000000m)] };
        var rows = new[] { basis, change, renewal };
        Assert.Equal(10000000m, Assert.Single(CommercialExposureRules.Snapshot(rows, Book, Start.AddDays(10), Known)).PropertySum);
        Assert.Equal(20000000m, Assert.Single(CommercialExposureRules.Snapshot(rows, Book, change.EffectiveAt, Known)).PropertySum);
        Assert.Equal(30000000m, Assert.Single(CommercialExposureRules.Snapshot(rows, Book, renewal.EffectiveAt, Known)).PropertySum);
    }

    [Fact]
    public void SameTimeTransactionAndSliceOrderMatchPolicyWinnerRules()
    {
        var basis = Slice(4, 100m);
        var higherTransaction = basis with { VersionId = Id(6), TransactionKind = "adjustment", TransactionSequence = 2, Locations = [new(Id(5), "S9", 200m)] };
        var higherSlice = higherTransaction with { VersionId = Id(7), SliceOrdinal = 2, Locations = [new(Id(5), "S9", 300m)] };
        Assert.Equal(300m, Assert.Single(CommercialExposureRules.Snapshot([higherSlice, basis, higherTransaction], Book, Start, Known)).PropertySum);
    }

    [Fact]
    public void SameDistrictLocationsSumButPoliciesRemainDistinctAndBooksIsolated()
    {
        var first = Slice(4, 3m) with { Locations = [new(Id(5), "S9", 3m), new(Id(6), "S9", 4m), new(Id(7), "S1", 0m)] };
        var second = Slice(8, 12m, Id(9));
        var foreign = Slice(10, 100000000m, Id(11)) with { BookId = Id(12) };
        var actual = Assert.Single(CommercialExposureRules.Snapshot([first, second, foreign], Book, Start, Known));
        Assert.Equal("S9", actual.District); Assert.Equal(19m, actual.PropertySum); Assert.Equal(2, actual.PolicyCount);
    }

    [Fact]
    public void AssessmentChecksFutureOtherPolicyBreakpointsAndReplacesOwnContribution()
    {
        var own = Slice(4, 10000000m);
        var other = Slice(6, 20000000m, Id(8));
        var future = other with { VersionId = Id(9), TransactionKind = "adjustment", TransactionSequence = 2, EffectiveAt = Start.AddMonths(6), Locations = [new(Id(5), "S9", 30000000m)] };
        var proposed = own with { VersionId = Id(10), TransactionKind = "adjustment", TransactionSequence = 2, Locations = [new(Id(5), "S9", 15000000m)] };
        var result = CommercialExposureRules.Assess([own, other, future], [proposed], [Limit()], Book, Policy, Start, own.TermEndsAt, Known);
        Assert.False(result.Allowed); Assert.Equal(2, result.Intervals.Count);
        Assert.Equal(35000000m, result.Intervals[0].ResultingPropertySum); Assert.Equal(5000000m, result.Intervals[0].Headroom);
        Assert.Equal(45000000m, result.Intervals[1].ResultingPropertySum); Assert.Equal(-5000000m, result.Intervals[1].Headroom);
        Assert.Equal("commercial-district-capacity-exceeded", result.Intervals[1].Blocker);
        Assert.All(result.Intervals, row => Assert.Equal(2, row.PolicyCount));
    }

    [Fact]
    public void IncompleteProposedTimelineCannotOmitKnownFutureOwnChanges()
    {
        var basis = Slice(4, 100m);
        var future = basis with { VersionId = Id(6), EffectiveAt = Start.AddMonths(6), TransactionSequence = 2, TransactionKind = "adjustment" };
        var proposed = basis with { VersionId = Id(7), TransactionSequence = 3, TransactionKind = "adjustment" };
        Assert.Throws<ArgumentException>(() => CommercialExposureRules.Assess([basis, future], [proposed], [Limit()], Book, Policy, Start, basis.TermEndsAt, Known));
        var carried = proposed with { VersionId = Id(8), EffectiveAt = future.EffectiveAt, SliceOrdinal = 2 };
        Assert.True(CommercialExposureRules.Assess([basis, future], [proposed, carried], [Limit()], Book, Policy, Start, basis.TermEndsAt, Known).Allowed);
    }

    [Fact]
    public void PublishedLimitReplacementPreservesHistoricalKnowledgeAndUnchangedIntervals()
    {
        var basis = Slice(4, 30000000m); var original = Limit();
        var replacement = original with { Id = Id(101), Version = 2, Amount = 20000000m, EffectiveFrom = Start.AddMonths(6), PublishedAt = Known.AddDays(1), SupersedesLimitId = original.Id, ContentHash = new string('b', 64) };
        var before = CommercialExposureRules.Assess([], [basis], [original, replacement], Book, Policy, Start, basis.TermEndsAt, Known);
        Assert.True(before.Allowed); Assert.All(before.Intervals, row => Assert.Equal(original.Id, row.LimitVersionId));
        var after = CommercialExposureRules.Assess([], [basis], [original, replacement], Book, Policy, Start, basis.TermEndsAt, Known.AddDays(2));
        Assert.False(after.Allowed); Assert.Equal(original.Id, after.Intervals[0].LimitVersionId); Assert.Equal(replacement.Id, after.Intervals[1].LimitVersionId);
    }

    [Fact]
    public void BackdatedMoveChecksBothDistrictsUntilItsExclusiveEnd()
    {
        var own = Slice(4, 10_000_000m);
        var other = Slice(6, 35_000_000m, Id(8)) with { Locations = [new(Id(5), "S1", 35_000_000m)] };
        var release = other with { VersionId = Id(9), EffectiveAt = Start.AddMonths(3), TransactionKind = "cancellation", TransactionSequence = 2, Locations = [] };
        var move = own with { VersionId = Id(10), EffectiveAt = Start.AddMonths(2), TransactionKind = "adjustment", TransactionSequence = 2, Locations = [new(Id(5), "S1", 10_000_000m)] };
        var result = CommercialExposureRules.Assess([own, other, release], [move], [Limit()], Book, Policy,
            move.EffectiveAt, Start.AddMonths(4), Known);
        Assert.False(result.Allowed);
        var oldDistrict = result.Intervals.Where(x => x.District == "S9").ToArray();
        Assert.Equal(2, oldDistrict.Length); Assert.All(oldDistrict, x => Assert.Equal(0, x.ResultingPropertySum));
        var destination = result.Intervals.Where(x => x.District == "S1").ToArray();
        Assert.Equal(45_000_000m, destination[0].ResultingPropertySum); Assert.Equal(10_000_000m, destination[1].ResultingPropertySum);
        Assert.Equal(Start.AddMonths(3), destination[0].To); Assert.Equal(Start.AddMonths(4), destination[1].To);
    }

    [Fact]
    public void ZeroPropertyAndCancellationStillRequireAnApplicablePublishedLimit()
    {
        var zero = Slice(4, 0) with { Locations = [] };
        var result = CommercialExposureRules.Assess([], [zero], [Limit()], Book, Policy, Start, zero.TermEndsAt, Known);
        Assert.True(result.Allowed); Assert.Equal("*", Assert.Single(result.Intervals).District);
        var cancelled = zero with { TransactionKind = "cancellation" };
        Assert.False(CommercialExposureRules.Assess([], [cancelled], [], Book, Policy, Start, zero.TermEndsAt, Known).Allowed);
    }

    [Fact]
    public void ReplacementExpiryRestoresEarlierLimitAndCompetingBranchesBlock()
    {
        var basis = Slice(4, 30_000_000m); var original = Limit();
        var temporary = original with { Id = Id(101), Version = 2, Amount = 20_000_000m, SupersedesLimitId = original.Id,
            EffectiveFrom = Start.AddMonths(3), EffectiveTo = Start.AddMonths(6) };
        var result = CommercialExposureRules.Assess([], [basis], [original, temporary], Book, Policy, Start, basis.TermEndsAt, Known);
        Assert.Equal(new[] { original.Id, temporary.Id, original.Id }, result.Intervals.Select(x => x.LimitVersionId!.Value));
        var competing = temporary with { Id = Id(102), Version = 3 };
        var ambiguous = CommercialExposureRules.Assess([], [basis], [original, temporary, competing], Book, Policy, Start, basis.TermEndsAt, Known);
        Assert.Equal("commercial-exposure-limit-ambiguous", ambiguous.Intervals[1].Blocker);
    }

    [Fact]
    public void MalformedAndPartialTimelinesCannotBeAssessed()
    {
        var basis = Slice(4, 100m);
        var invalid = new[] {
            basis with { Locations = [new(Id(5), "S9", -1)] },
            basis with { Locations = [new(Id(5), "S9", 1.001m)] },
            basis with { Locations = [new(Id(5), "s9", 1)] },
            basis with { Locations = [new(Id(5), "S9", 1), new(Id(5), "S9", 2)] },
            basis with { TransactionKind = "cancellation" },
            basis with { EffectiveAt = basis.TermEndsAt },
            basis with { TransactionSequence = 0 }
        };
        foreach (var row in invalid) Assert.Throws<ArgumentException>(() => CommercialExposureRules.Snapshot([row], Book, Start, Known));
        Assert.Throws<ArgumentException>(() => CommercialExposureRules.Snapshot([basis, basis], Book, Start, Known));
        var futureOnly = basis with { EffectiveAt = Start.AddDays(1) };
        Assert.Throws<ArgumentException>(() => CommercialExposureRules.Assess([], [futureOnly], [Limit()], Book, Policy, Start, basis.TermEndsAt, Known));
        var wrongParent = Limit() with { SupersedesLimitId = Id(999) };
        Assert.Throws<ArgumentException>(() => CommercialExposureRules.Assess([], [basis], [wrongParent], Book, Policy, Start, basis.TermEndsAt, Known));
    }

    [Fact]
    public void MissingAndCompetingLimitsBlockWhileSpecificDistrictOverridesDefault()
    {
        var basis = Slice(4, 100m); var original = Limit();
        Assert.Equal("commercial-exposure-limit-missing", Assert.Single(CommercialExposureRules.Assess([], [basis], [], Book, Policy, Start, basis.TermEndsAt, Known).Intervals).Blocker);
        var competing = original with { Id = Id(101), Version = 2 };
        Assert.Equal("commercial-exposure-limit-ambiguous", Assert.Single(CommercialExposureRules.Assess([], [basis], [original, competing], Book, Policy, Start, basis.TermEndsAt, Known).Intervals).Blocker);
        var specific = original with { Id = Id(102), District = "S9", Amount = 50m };
        var result = CommercialExposureRules.Assess([], [basis], [original, specific], Book, Policy, Start, basis.TermEndsAt, Known);
        Assert.Equal(specific.Id, Assert.Single(result.Intervals).LimitVersionId); Assert.False(result.Allowed);
    }
}
