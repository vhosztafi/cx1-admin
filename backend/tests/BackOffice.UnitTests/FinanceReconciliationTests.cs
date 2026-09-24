using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceReconciliationTests
{
    [Theory]
    [InlineData("25.00", 2500L)]
    [InlineData("-25.00", -2500L)]
    [InlineData("0.01", 1L)]
    public void SignedBankMoneyIsCanonical(string text, long pence)
        => Assert.Equal(pence, FinanceReconciliationMath.SignedPence(text));

    [Theory]
    [InlineData("0.00")]
    [InlineData("1.0")]
    [InlineData("1e2")]
    [InlineData("+1.00")]
    [InlineData("10000000000000.00")]
    public void ZeroAndNoncanonicalBankMoneyAreRejected(string text)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReconciliationMath.SignedPence(text));

    [Fact]
    public void SignedResidualSupportsSplitPartialAndReasonedReversalWithoutOvermatch()
    {
        Assert.Equal(1000, FinanceReconciliationMath.Residual(2500, [1000, 500], []));
        Assert.Equal(2000, FinanceReconciliationMath.Residual(2500, [1000, 500], [1000]));
        Assert.Equal(-1000, FinanceReconciliationMath.Residual(-2500, [-1000, -500], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReconciliationMath.Residual(2500, [2000, 1000], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReconciliationMath.Residual(-2500, [1000], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceReconciliationMath.Residual(2500, [1000], [1000, 1000]));
    }
}
