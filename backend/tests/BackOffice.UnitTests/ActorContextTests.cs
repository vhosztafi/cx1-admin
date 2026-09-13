using BackOffice.Application;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ActorContextTests
{
    [Fact]
    public void AdministrationDoesNotGrantBusinessAuthority()
    {
        var administrator=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string> {"system-admin"});
        Assert.True(administrator.HasCapability("platform-admin"));
        Assert.False(administrator.HasCapability("finance"));
        Assert.False(administrator.HasCapability("client-servicing"));
        Assert.False(administrator.HasCapability("policy-issue-within-authority"));
    }

    [Fact]
    public void ExternalScopeAndUnknownCapabilitiesFailClosed()
    {
        var external=new ActorContext(Guid.NewGuid(),null,Guid.NewGuid(),new HashSet<string> {"system-admin","finance"});
        Assert.False(external.HasCapability("platform-admin"));
        Assert.False(external.HasCapability("finance"));
        var internalActor=external with {AgencyId=null};
        Assert.False(internalActor.HasCapability("unknown"));
        Assert.True(internalActor.HasCapability("authenticated"));
    }
}
