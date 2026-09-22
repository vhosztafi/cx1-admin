using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class AgencyResponseRulesTests
{
    [Theory]
    [InlineData("queued")]
    [InlineData("failed")]
    [InlineData("superseded")]
    public void UnreceivedMessageCannotBecomeAnAgencyResponseObligation(string deliveryState)
        => Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Track(deliveryState, "Please supply the licence number.", "Follow up this delivered request"));

    [Fact]
    public void DeliveredContentAndExplicitReasonAreRequired()
    {
        AgencyResponseRules.Track("delivered", "Please supply the licence number.", "Follow up this delivered request");
        Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Track("delivered", "  ", "Follow up this delivered request"));
        Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Track("delivered", new string('x',8001), "Follow up this delivered request"));
        Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Track("delivered", "Please supply the licence number.", ""));
    }

    [Theory]
    [InlineData("response-received")]
    [InlineData("withdrawn")]
    public void RecordedClosureIsTerminalAndRequiresReason(string outcome)
    {
        AgencyResponseRules.Resolve("awaiting-response", outcome, "Recorded the agency response after review");
        Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Resolve(outcome, outcome, "Recorded the agency response after review"));
        Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Resolve("awaiting-response", outcome, "  "));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("reopened")]
    [InlineData("awaiting-response")]
    public void ResponseTrackingCannotMakeAnInsuranceDecisionOrReopenHistory(string outcome)
        => Assert.Throws<CommunicationRuleException>(() => AgencyResponseRules.Resolve("awaiting-response", outcome, "Reason does not authorize this transition"));
}
