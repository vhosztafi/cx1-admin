using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialExposureObservationTests
{
    private static readonly Guid Book = Guid.NewGuid(), Own = Guid.NewGuid(), Other = Guid.NewGuid();
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2027-01-01T00:00:00Z"), Known = Start.AddMonths(-1);
    private static CommercialExposureSlice Row(Guid policy, decimal sum) => new(Book, policy, Guid.NewGuid(), Guid.NewGuid(), Start, Start.AddYears(1), Start, Known,
        1, 1, "new-business", [new(Guid.NewGuid(), "S9", sum)]);
    private static CommercialExposureLimit Limit() => new(Guid.NewGuid(), Book, "*", 1, 1000m, Known.AddYears(-1), Start.AddYears(2), Known.AddDays(-1), new string('a', 64));

    [Fact]
    public void ObservationCountsOnlyActiveWinnersAndNeverAddsASecondOwnContribution()
    {
        var rows = new[] { Row(Own, 100m), Row(Other, 250m) }; var limit = Limit();
        CommercialExposureInterval At(DateTimeOffset at) => Assert.Single(CommercialExposureRules.Observe(rows, [limit], Book, Own, ["S9"], at, Known).Intervals);
        var scheduled = At(Start.AddHours(-1)); Assert.Equal(0m, scheduled.ProposedPropertySum); Assert.Equal(0m, scheduled.ResultingPropertySum); Assert.Equal(Start, scheduled.To);
        var active = At(Start); Assert.Equal(100m, active.ProposedPropertySum); Assert.Equal(250m, active.OtherPropertySum); Assert.Equal(350m, active.ResultingPropertySum); Assert.Equal(2, active.PolicyCount);
        Assert.Equal(0m, At(Start.AddYears(1)).ResultingPropertySum);
    }

    [Fact]
    public void ObservationUsesRequestedKnowledgeAndReportsNextActualBoundary()
    {
        var basis = Row(Own, 100m); var cancelled = basis with { VersionId = Guid.NewGuid(), TransactionSequence = 2, TransactionKind = "cancellation",
            EffectiveAt = Start.AddDays(10), ProcessedAt = Known.AddDays(1), Locations = [] };
        var beforeKnowledge = Assert.Single(CommercialExposureRules.Observe([basis, cancelled], [Limit()], Book, Own, ["S9"], cancelled.EffectiveAt, Known).Intervals);
        Assert.Equal(100m, beforeKnowledge.ProposedPropertySum);
        var known = Assert.Single(CommercialExposureRules.Observe([basis, cancelled], [Limit()], Book, Own, ["S9"], cancelled.EffectiveAt, Known.AddDays(1)).Intervals);
        Assert.Equal(0m, known.ProposedPropertySum);
        var beforeEvent = Assert.Single(CommercialExposureRules.Observe([basis, cancelled], [Limit()], Book, Own, ["S9"], cancelled.EffectiveAt.AddHours(-1), Known.AddDays(1)).Intervals);
        Assert.Equal(cancelled.EffectiveAt, beforeEvent.To);
    }

    [Fact]
    public void ObservationPinsLimitsAndDoesNotHideMissingOrAmbiguousConfiguration()
    {
        var basis = Row(Own, 1100m); var limit = Limit();
        var exceeded = Assert.Single(CommercialExposureRules.Observe([basis], [limit], Book, Own, ["S9"], Start, Known).Intervals);
        Assert.Equal(limit.Id, exceeded.LimitVersionId); Assert.Equal(-100m, exceeded.Headroom); Assert.Equal("commercial-district-capacity-exceeded", exceeded.Blocker);
        Assert.Equal("commercial-exposure-limit-missing", Assert.Single(CommercialExposureRules.Observe([basis], [], Book, Own, ["S9"], Start, Known).Intervals).Blocker);
        var competing = limit with { Id = Guid.NewGuid(), Version = 2 };
        Assert.Equal("commercial-exposure-limit-ambiguous", Assert.Single(CommercialExposureRules.Observe([basis], [limit, competing], Book, Own, ["S9"], Start, Known).Intervals).Blocker);
        Assert.Throws<ArgumentException>(() => CommercialExposureRules.Observe([basis], [limit], Book, Own, ["S9 2QT"], Start, Known));
    }
}
