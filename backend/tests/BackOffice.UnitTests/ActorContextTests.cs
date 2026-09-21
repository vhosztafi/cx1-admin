using BackOffice.Application;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ActorContextTests
{
    [Theory]
    [InlineData("servicing", true)]
    [InlineData("underwriter", true)]
    [InlineData("senior-underwriter", true)]
    [InlineData("agency-admin", true)]
    [InlineData("system-admin", true)]
    [InlineData("finance", false)]
    [InlineData("broker-admin", false)]
    public void FileCapabilitiesRequireInternalOperationalRole(string role, bool allowed)
    {
        var actor = new ActorContext(Guid.NewGuid(), null, null, new HashSet<string> { role });
        foreach (var capability in new[] { "document-read", "document-download", "document-upload", "document-generate" })
        {
            Assert.Equal(allowed, actor.HasCapability(capability));
            Assert.False((actor with { AgencyId = Guid.NewGuid() }).HasCapability(capability));
        }
    }

    [Theory]
    [InlineData("servicing", true)]
    [InlineData("underwriter", true)]
    [InlineData("senior-underwriter", true)]
    [InlineData("agency-admin", false)]
    [InlineData("system-admin", false)]
    [InlineData("finance", false)]
    [InlineData("broker-admin", false)]
    public void QuoteCapabilitiesAreExplicitAndInternal(string role, bool allowed)
    {
        var actor = new ActorContext(Guid.NewGuid(), null, null, new HashSet<string> { role });
        Assert.Equal(allowed, actor.HasCapability("quote-read"));
        Assert.Equal(allowed, actor.HasCapability("quote-capture"));
        Assert.False((actor with { AgencyId = Guid.NewGuid() }).HasCapability("quote-read"));
        Assert.False((actor with { AgencyId = Guid.NewGuid() }).HasCapability("quote-capture"));
    }

    [Fact]
    public void RatingPermissionDoesNotGrantReferralOrIssueAuthority()
    {
        foreach (var role in new[] { "servicing", "underwriter", "senior-underwriter", "agency-admin", "system-admin", "finance" })
        {
            var actor = new ActorContext(Guid.NewGuid(), null, null, new HashSet<string> { role });
            foreach (var capability in new[] { "quote-rate", "quote-submit", "quote-revise", "underwriting-read" })
            {
                Assert.Equal(role is "servicing" or "underwriter" or "senior-underwriter", actor.HasCapability(capability));
                Assert.False((actor with { AgencyId = Guid.NewGuid() }).HasCapability(capability));
            }
            foreach (var capability in new[] { "underwriting-decide-within-authority", "policy-issue-within-authority", "underwriting-record-capacity" })
                Assert.Equal(role is "underwriter" or "senior-underwriter", actor.HasCapability(capability));
        }
    }

    [Fact]
    public void AdministrationDoesNotGrantBusinessAuthority()
    {
        var administrator=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string> {"system-admin"});
        Assert.True(administrator.HasCapability("platform-admin"));
        Assert.True(administrator.HasCapability("integration-admin"));
        Assert.False(administrator.HasCapability("finance"));
        Assert.False(administrator.HasCapability("client-servicing"));
        Assert.False(administrator.HasCapability("policy-issue-within-authority"));
    }

    [Fact]
    public void ExternalScopeAndUnknownCapabilitiesFailClosed()
    {
        var external=new ActorContext(Guid.NewGuid(),null,Guid.NewGuid(),new HashSet<string> {"system-admin","finance"});
        Assert.False(external.HasCapability("platform-admin"));
        Assert.False(external.HasCapability("integration-admin"));
        Assert.False(external.HasCapability("finance"));
        var internalActor=external with {AgencyId=null};
        Assert.False(internalActor.HasCapability("unknown"));
        Assert.True(internalActor.HasCapability("authenticated"));
    }
}
