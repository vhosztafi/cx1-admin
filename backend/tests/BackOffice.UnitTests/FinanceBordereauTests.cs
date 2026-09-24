using System.Text;
using BackOffice.Infrastructure.Finance;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class FinanceBordereauTests
{
    [Theory]
    [InlineData("=SUM(1,2)", "'=SUM(1,2)")]
    [InlineData("+2", "'+2")]
    [InlineData("-2", "'-2")]
    [InlineData("@cell", "'@cell")]
    [InlineData("\t=1", "'\t=1")]
    [InlineData("ordinary", "ordinary")]
    public void UntrustedCsvTextCannotStartSpreadsheetFormula(string input, string expected)
        => Assert.Equal(expected, BordereauCsv.SafeText(input));

    [Fact]
    public void CsvQuotesUntrustedMappingButKeepsSignedNumbers()
    {
        var row = new BordereauExportRow(Guid.NewGuid(), "PL,\"1\"\nnext", "=provider", "Agency",
            "-1.20", "0.00", "0.00", "-0.10", "-1.10");
        var csv = Encoding.UTF8.GetString(BordereauCsv.Write([row]));
        Assert.Contains("\"PL,\"\"1\"\"\nnext\"", csv);
        Assert.Contains("'=provider", csv);
        Assert.Contains(",-1.20,0.00,0.00,-0.10,-1.10", csv);
    }

    [Fact]
    public void ValidationReportsEveryMappingFailureAndDuplicateSource()
    {
        var source = Guid.NewGuid();
        var rows = new[]
        {
            new BordereauExportRow(source, "", "", "", "1.00", "0.00", "0.00", "0.00", "1.00"),
            new BordereauExportRow(source, "PL-2", "P", "A", "1.00", "0.00", "0.00", "0.00", "1.00")
        };
        var failures = FinanceBordereauValidation.Check(rows);
        Assert.Contains(failures, x => x.SourceJournalId == source && x.Field == "sourceJournalId");
        Assert.Contains(failures, x => x.Field == "policyReference");
        Assert.Contains(failures, x => x.Field == "providerProductCode");
        Assert.Contains(failures, x => x.Field == "agencyReference");
    }
}
