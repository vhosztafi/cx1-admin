using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceRefundTests
{
    [Theory]
    [InlineData(10000L, 10000L, 0L, 0L, 0L, 0L)]
    [InlineData(3000L, 0L, 0L, 10000L, 0L, 3000L)]
    [InlineData(10000L, 9000L, 0L, 1000L, 0L, 1000L)]
    [InlineData(10000L, 9000L, 0L, 1000L, 500L, 500L)]
    [InlineData(5000L, 9000L, 5000L, 4000L, 0L, 1000L)]
    public void CreditOffsetsOutstandingDebtBeforeCollectedCashCanRefund(
        long credit, long outstanding, long earlierCredits, long collected, long reserved, long expected)
        => Assert.Equal(expected, FinanceRefundMath.Entitlement(credit, outstanding,
            earlierCredits, collected, reserved));

    [Theory]
    [InlineData(25000L, 1)]
    [InlineData(25001L, 2)]
    public void VersionedThresholdRequiresIndependentDecisions(long amount, int expected)
        => Assert.Equal(expected, FinanceRefundMath.RequiredApprovals(amount, 25000));

    [Fact]
    public void InvalidMoneyAndNegativeResidualsFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceRefundMath.Entitlement(-1, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceRefundMath.Entitlement(100, 0, 0, 10, 11));
    }
}
