using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceStatementTests
{
    [Fact]
    public void EmptyWindowRetainsEarlierOpeningAndCreditClosing()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = from.AddMonths(1);
        var rows = new[]
        {
            new StatementMovement("prior", from.AddDays(-1), 5000, null),
            new StatementMovement("later", to, -7000, null)
        };
        var result = FinanceStatementMath.Reconcile(rows, from, to);
        Assert.Equal("50.00", result.Opening);
        Assert.Equal("0.00", result.Debits);
        Assert.Equal("0.00", result.Credits);
        Assert.Equal("50.00", result.Closing);
        Assert.Empty(result.Rows);

        var credit = FinanceStatementMath.Reconcile(
            rows.Append(new StatementMovement("credit", from, -7000, null)), from, to);
        Assert.Equal("-20.00", credit.Closing);
        Assert.Equal("70.00", credit.Credits);
        Assert.Equal("-20.00", Assert.Single(credit.Rows).RunningBalance);
    }

    [Fact]
    public void AggregateOverflowIsRejected()
    {
        var from = new DateOnly(2026, 9, 1);
        Assert.Throws<OverflowException>(() => FinanceStatementMath.Reconcile(
            [new("a", from, 999_999_999_999_999, null), new("b", from, 1, null)], from, from.AddDays(1)));
    }

    [Fact]
    public void LondonPostingDateSelectsWindowAtDstBoundary()
    {
        var lateSummer = FinanceLedgerMath.LegacyPostingDate(new DateTimeOffset(2026, 10, 24, 23, 30, 0, TimeSpan.Zero));
        var afterFallback = FinanceLedgerMath.LegacyPostingDate(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 25), lateSummer);
        Assert.Equal(lateSummer, afterFallback);
        var statement = FinanceStatementMath.Reconcile(
            [new StatementMovement("summer", lateSummer, 100, null), new StatementMovement("winter", afterFallback, -50, null)],
            new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 26));
        Assert.Equal("0.50", statement.Closing);
    }
}
