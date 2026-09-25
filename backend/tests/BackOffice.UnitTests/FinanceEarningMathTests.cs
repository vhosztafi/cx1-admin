using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceEarningMathTests
{
    [Theory]
    [InlineData(6000L, 3100L, 2900L)]
    [InlineData(-6000L, -3100L, -2900L)]
    public void LeapFebruaryAllocatesExactSignedPence(long premium, long january, long february)
    {
        var rows = FinanceEarningMath.Allocate(premium,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal([new MonthlyEarning(new DateOnly(2024, 1, 1), january),
            new MonthlyEarning(new DateOnly(2024, 2, 1), february)], rows);
        Assert.Equal(premium, rows.Sum(x => x.EarnedPence));
    }

    [Theory]
    [InlineData(1463L, 743L, 720L)]
    [InlineData(-1463L, -743L, -720L)]
    public void LondonSpringClockChangeUsesExactUtcOverlap(long premium, long march, long april)
    {
        var rows = FinanceEarningMath.Allocate(premium,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 30, 23, 0, 0, TimeSpan.Zero));
        Assert.Equal([new MonthlyEarning(new DateOnly(2026, 3, 1), march),
            new MonthlyEarning(new DateOnly(2026, 4, 1), april)], rows);
    }

    [Fact]
    public void FinalMonthReceivesRemainderWithoutLosingOnePenny()
    {
        var rows = FinanceEarningMath.Allocate(1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 23, 0, 0, TimeSpan.Zero));
        Assert.Equal([0L, 0L, 1L], rows.Select(x => x.EarnedPence));
        Assert.Equal(-1, FinanceEarningMath.Allocate(-1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 23, 0, 0, TimeSpan.Zero)).Sum(x => x.EarnedPence));
    }

    [Fact]
    public void CoverageAndSourceHashRejectChangedSavedFacts()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<InvalidOperationException>(() => FinanceEarningMath.Allocate(100, start, start));
        Assert.Throws<InvalidOperationException>(() => FinanceEarningMath.Allocate(100,
            start.ToOffset(TimeSpan.FromHours(1)), start.AddDays(1)));
        var component = new IssueFinancialComponent { Amount = 100m, CoverageStartsAt = start,
            CoverageEndsAt = start.AddMonths(1) };
        var hash = FinanceEarningMath.SourceHash(component, start);
        component.Amount = 100.01m;
        Assert.NotEqual(hash, FinanceEarningMath.SourceHash(component, start));
    }
}
