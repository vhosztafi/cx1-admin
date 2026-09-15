using BackOffice.Application.Agencies;
using Xunit;
namespace BackOffice.UnitTests;
public sealed class AgencyActionCountsTests
{
    [Fact]
    public void OpenActionsCountPendingDecisionsAndKeepUntrackedObligationsSeparate()
    {
        var result=AgencyActionCounts.Total([new(1,2,3,20),new(2,1,0,4)]);
        Assert.Equal(9,result.OpenActions); Assert.Equal(24,result.DueFollowUps);
        Assert.Equal(AgencyActionCounts.Empty,AgencyActionCounts.Total([]));
    }
    [Theory]
    [InlineData("2026-06-01T22:59:59Z","2026-06-01")]
    [InlineData("2026-06-01T23:00:00Z","2026-06-02")]
    [InlineData("2026-12-01T23:59:59Z","2026-12-01")]
    [InlineData("2026-12-02T00:00:00Z","2026-12-02")]
    public void DueDateUsesLondonCalendarAtSummerAndWinterMidnight(string instant,string expected)
        => Assert.Equal(DateOnly.Parse(expected),AgencyActionCounts.LondonDate(DateTimeOffset.Parse(instant)));
}
