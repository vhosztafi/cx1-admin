using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinancePeriodTests
{
    [Theory]
    [InlineData("-15.20", "10.00", "25.20", "0.00", true)]
    [InlineData("1.00", "0.00", "0.00", "0.00", false)]
    [InlineData("0.00", "0.00", "0.00", "0.00", false)]
    public void FinancePeriod_CorrectionBalancesExactSignedAccounts(string debtor, string provider,
        string cash, string internalDelta, bool valid)
        => Assert.Equal(valid, FinancePeriodMath.TryBalanced(debtor, provider, cash, internalDelta));

    [Theory]
    [InlineData("1e2")]
    [InlineData("0.001")]
    [InlineData("10000000000000.00")]
    public void FinancePeriod_RejectsNonCanonicalOrUnrepresentableMoney(string amount)
        => Assert.False(FinancePeriodMath.TryBalanced(amount, "0.00", "0.00", "-1.00"));
}
