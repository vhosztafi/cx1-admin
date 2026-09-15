using BackOffice.Application.Agencies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class AgencyPermissionRulesTests
{
    [Fact]
    public void AllowlistedRequestNormalizesReasonWithoutGrantingAuthority()
    {
        Assert.Equal(new AgencyPermissionInput("bordereau-download", "Fictional reporting need"), AgencyPermissionRules.Request("bordereau-download", " Fictional reporting need "));
        Assert.False(AgencyAccessRules.Allows("broker-admin", "bordereau-download"));
    }

    [Theory]
    [InlineData("platform-admin")]
    [InlineData("agency-admin")]
    [InlineData("BORDEREAU-DOWNLOAD")]
    [InlineData("")]
    public void ArbitraryOrNoncanonicalPermissionIsRejected(string permission)
        => Assert.Equal(422, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Request(permission, "Reason")).Status);

    [Theory]
    [InlineData("approve", "granted")]
    [InlineData("reject", "rejected")]
    public void IndependentPendingDecisionHasExplicitTerminalState(string decision, string state)
        => Assert.Equal(new AgencyPermissionDecision(state, "Reviewed"), AgencyPermissionRules.Decide(Guid.NewGuid(), Guid.NewGuid(), "pending", decision, " Reviewed "));

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public void RequesterCannotDecideOwnRequest(string decision)
    {
        var actor = Guid.NewGuid();
        Assert.Equal(403, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Decide(actor, actor, "pending", decision, "Reason")).Status);
    }

    [Theory]
    [InlineData("granted")]
    [InlineData("rejected")]
    public void TerminalDecisionCannotBeRewritten(string state)
        => Assert.Equal(409, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Decide(Guid.NewGuid(), Guid.NewGuid(), state, "approve", "Reason")).Status);

    [Fact]
    public void MissingIdentityInvalidDecisionAndUnboundedReasonsAreRejected()
    {
        Assert.Equal(403, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Decide(Guid.Empty, Guid.NewGuid(), "pending", "approve", "Reason")).Status);
        Assert.Equal(403, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Decide(Guid.NewGuid(), Guid.Empty, "pending", "approve", "Reason")).Status);
        Assert.Equal(422, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Decide(Guid.NewGuid(), Guid.NewGuid(), "pending", "grant", "Reason")).Status);
        foreach (var reason in new[] { " ", "x\ny", new string('x', 1001) })
            Assert.Equal(422, Assert.Throws<AgencyCommandException>(() => AgencyPermissionRules.Request("bordereau-download", reason)).Status);
        Assert.Equal(1000, AgencyPermissionRules.Reason(new string('x', 1000)).Length);
    }
}
