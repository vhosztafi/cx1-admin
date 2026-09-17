using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteTermsRulesTests
{
    private static readonly DateTimeOffset Delivered = DateTimeOffset.Parse("2026-09-17T10:00:00Z");
    [Theory]
    [InlineData("queued", 1, false)]
    [InlineData("failed", 1, false)]
    [InlineData("superseded", 1, false)]
    [InlineData("delivered", -1, false)]
    [InlineData("delivered", 11, false)]
    [InlineData("delivered", 0, true)]
    [InlineData("delivered", 10, true)]
    public void AcceptanceRequiresAppliedDeliveryAndActualReceivedWindow(string state, int minutes, bool expected)
        => Assert.Equal(expected, QuoteTermsRules.AcceptanceWindow(state, Delivered, Delivered.AddMinutes(minutes), Delivered.AddMinutes(10), Delivered.AddDays(1)));

    [Fact]
    public void ExpiryIsExclusiveEvenForAnEarlierAcceptance()
        => Assert.False(QuoteTermsRules.AcceptanceWindow("delivered", Delivered, Delivered.AddMinutes(1), Delivered.AddDays(1), Delivered.AddDays(1)));

    [Theory]
    [InlineData("Ada Customer", "email", true)]
    [InlineData("Ada Customer", "written", true)]
    [InlineData("Ada Customer", "telephone", true)]
    [InlineData("", "email", false)]
    [InlineData("Ada\nCustomer", "email", false)]
    [InlineData("Ada Customer", "sent", false)]
    public void AcceptanceNamesAnActualAccepterAndSupportedChannel(string name, string channel, bool expected)
        => Assert.Equal(expected, QuoteTermsRules.ValidAcceptanceIdentity(name, channel));
}
