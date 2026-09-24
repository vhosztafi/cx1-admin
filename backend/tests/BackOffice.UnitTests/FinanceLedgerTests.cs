using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceLedgerTests
{
    [Theory]
    [InlineData("100.00", 10000L)]
    [InlineData("-0.01", -1L)]
    [InlineData("9999999999999.99", 999999999999999L)]
    public void FinanceLedger_MoneyIsCanonicalAndExact(string amount, long pence)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(pence, FinanceLedgerMath.Pence(value));
        Assert.Equal(amount, FinanceLedgerMath.Money(value));
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("10000000000000.00")]
    public void FinanceLedger_RejectsUnrepresentableAmount(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<OverflowException>(() => FinanceLedgerMath.Pence(value));
    }

    [Fact]
    public void FinanceLedger_AggregateOverflowIsNotTruncated()
    {
        Assert.Throws<OverflowException>(() => FinanceLedgerMath.SumPence([999999999999999L, 1L]));
    }

    [Theory]
    [InlineData("2026-03-29T00:59:59+00:00", "2026-03-29")]
    [InlineData("2026-03-29T23:30:00+00:00", "2026-03-30")]
    [InlineData("2026-10-25T00:30:00+00:00", "2026-10-25")]
    [InlineData("2026-10-25T23:30:00+00:00", "2026-10-25")]
    public void FinanceLedger_LegacyPostedAtUsesLondonDate(string instant, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), FinanceLedgerMath.LegacyPostingDate(DateTimeOffset.Parse(instant)));
    }

    [Fact]
    public void FinanceLedger_SignedIssueAdjustmentCancellationAndCash()
    {
        // Positive receivable means owed by debtor; positive provider means owed to insurer.
        var movements = new[] { (debtor: 10000L, provider: 8000L), (debtor: 2000L, provider: 1500L),
            (debtor: -3000L, provider: -2400L), (debtor: -4000L, provider: 0L) };
        var balances = FinanceLedgerMath.Balances(movements);
        Assert.Equal(5000L, balances.DebtorPence);
        Assert.Equal(7100L, balances.ProviderPence);
    }
}
