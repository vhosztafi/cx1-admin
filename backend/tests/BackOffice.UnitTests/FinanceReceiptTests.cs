using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceReceiptTests
{
    [Theory]
    [InlineData("1.00", 100L)]
    [InlineData("0.01", 1L)]
    [InlineData("9999999999999.99", 999999999999999L)]
    public void PositiveCashUsesExactCanonicalPence(string text, long expected)
        => Assert.Equal(expected, FinanceReceiptMath.PositivePence(text));

    [Theory]
    [InlineData("0.00")]
    [InlineData("-0.01")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1e2")]
    [InlineData("1,000.00")]
    [InlineData("10000000000000.00")]
    public void ZeroNegativeNoncanonicalAndOverflowCashAreRejected(string text)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReceiptMath.PositivePence(text));

    [Fact]
    public void ResidualAccountsForPositiveAllocationsAndOneReversalOnly()
    {
        Assert.Equal(40, FinanceReceiptMath.Residual(100, [60], []));
        Assert.Equal(100, FinanceReceiptMath.Residual(100, [60], [60]));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReceiptMath.Residual(100, [60, 60], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReceiptMath.Residual(100, [60], [60, 60]));
    }
}
