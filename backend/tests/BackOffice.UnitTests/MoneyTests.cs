using BackOffice.Domain;
using Xunit;

namespace BackOffice.UnitTests;

public class MoneyTests
{
    [Theory]
    [InlineData("1200.00", 120000L)]
    [InlineData("-355.07", -35507L)]
    [InlineData("0.01", 1L)]
    [InlineData("9999999999999.99", 999999999999999L)]
    public void PreservesExactContractAmounts(string input, long pence)
    {
        var money = Money.Parse(input);
        Assert.Equal(pence, money.Pence);
        Assert.Equal(input, money.ToString());
    }

    [Theory]
    [InlineData("1.001")][InlineData("1,200.00")][InlineData("£1.00")]
    [InlineData(" 1.00")][InlineData("01.00")][InlineData("1.00\n")]
    public void RejectsAmbiguousOrOverPreciseInput(string input) => Assert.Throws<FormatException>(() => Money.Parse(input));

    [Fact]
    public void HalfPenniesRoundAwayFromZero()
    {
        Assert.Equal("1.01", Money.Round(1.005m).ToString());
        Assert.Equal("-1.01", Money.Round(-1.005m).ToString());
        Assert.Equal("0.00", (Money.Parse("355.07") + Money.Parse("-355.07")).ToString());
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
    }
}
