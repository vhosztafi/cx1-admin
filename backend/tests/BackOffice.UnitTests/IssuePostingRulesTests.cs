using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class IssuePostingRulesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z"), End = Start.AddYears(1);
    private static IssuePostingInput Example(string collector = "agency", string mode = "net-remittance", int share = 0) => new(1200m, 144m, 35m, 120m, share, collector, mode, Start, End);
    [Theory]
    [InlineData("agency", "net-remittance", 0, "1259.00", "0.00")]
    [InlineData("agency", "net-remittance", 2000, "1252.00", "0.00")]
    [InlineData("agency", "separate-payment", 2000, "1379.00", "127.00")]
    [InlineData("mga", "net-remittance", 2000, "1379.00", "127.00")]
    [InlineData("mga", "separate-payment", 2000, "1379.00", "127.00")]
    public void FirstIssueRetainsExactSettlementAndBalancedComponentLineage(string collector, string mode, int share, string invoice, string payable)
    {
        var result = IssuePostingRules.Calculate(Example(collector, mode, share));
        Assert.Equal(1379m, result.GrossDue); Assert.Equal(decimal.Parse(invoice, System.Globalization.CultureInfo.InvariantCulture), result.InvoiceDue);
        Assert.Equal(decimal.Parse(payable, System.Globalization.CultureInfo.InvariantCulture), result.BrokerPayable);
        Assert.Equal(1224m, result.InsurerDue); Assert.Equal(share == 0 ? 35m : 28m, result.RetainedFee);
        Assert.Equal(result.Lines.Sum(x => x.Debit), result.Lines.Sum(x => x.Credit));
        var debtor = collector == "agency" ? "agency-receivable" : "relationship-receivable";
        Assert.Equal(result.InvoiceDue, result.Lines.Where(x => x.AccountCode == debtor).Sum(x => x.Debit - x.Credit));
        Assert.Equal(result.InsurerDue, result.Lines.Where(x => x.AccountCode == "insurer-payable").Sum(x => x.Credit - x.Debit));
        Assert.Equal(result.RetainedFee, result.Lines.Where(x => x.AccountCode == "fee-income").Sum(x => x.Credit - x.Debit));
        Assert.Equal(result.BrokerPayable, result.Lines.Where(x => x.AccountCode == "broker-remuneration-payable").Sum(x => x.Credit - x.Debit));
        Assert.All(result.Lines, line => { Assert.True(line.Debit > 0 && line.Credit == 0 || line.Credit > 0 && line.Debit == 0); Assert.Contains(result.Components, x => x.Code == line.ComponentCode); });
        Assert.DoesNotContain(result.Lines, x => x.AccountCode.Contains("cash") || x.AccountCode.Contains("paid"));
    }
    [Fact]
    public void FeeShareRoundsHalfAwayFromZeroAndOmitsZeroLines()
    {
        var result = IssuePostingRules.Calculate(Example(share: 5000) with { Fee = 0.01m, Commission = 0m, Tax = 0m });
        Assert.Equal(0.01m, result.FeeShare); Assert.Equal(0m, result.RetainedFee);
        Assert.DoesNotContain(result.Lines, x => x.ComponentCode is "tax" or "commission");
        Assert.Equal(1200m, result.InvoiceDue);
        var empty = IssuePostingRules.Calculate(Example() with { Premium = 0m, Tax = 0m, Fee = 0m, Commission = 0m });
        Assert.Empty(empty.Lines); Assert.Equal(0m, empty.InvoiceDue); Assert.Equal(5, empty.Components.Count);
    }
    [Fact]
    public void RejectsNegativeUnroundedOverflowAndUnsupportedSettlement()
    {
        foreach (var input in new[] { Example() with { Premium = -1 }, Example() with { Fee = 0.001m }, Example() with { Premium = 9999999999999.99m }, Example() with { Commission = 1201 }, Example() with { FeeShareBasisPoints = 10001 }, Example() with { Collector = "unknown" }, Example() with { Settlement = "paid" }, Example() with { EndsAt = Start }, Example() with { StartsAt = Start.ToOffset(TimeSpan.FromHours(1)) } })
            Assert.Throws<ArgumentException>(() => IssuePostingRules.Calculate(input));
    }
}
