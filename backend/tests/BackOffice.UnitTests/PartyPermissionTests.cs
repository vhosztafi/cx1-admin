using BackOffice.Application;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class PartyPermissionTests
{
    [Theory]
    [InlineData("servicing",true,true,false)]
    [InlineData("underwriter",true,true,true)]
    [InlineData("senior-underwriter",true,true,true)]
    [InlineData("agency-admin",true,false,false)]
    [InlineData("system-admin",false,false,false)]
    [InlineData("finance",false,false,false)]
    public void PartyCapabilitiesDoNotFollowPlatformAdministration(string role,bool read,bool write,bool match)
    {
        var actor=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string>{role});
        foreach(var capability in new[]{"client-read","relationship-read","contact-write"})Assert.Equal(read,actor.HasCapability(capability));
        foreach(var capability in new[]{"client-write","support-internal-read","support-write"})Assert.Equal(write,actor.HasCapability(capability));
        Assert.Equal(match,actor.HasCapability("match-review"));
        Assert.False((actor with {AgencyId=Guid.NewGuid()}).HasCapability("client-read"));
        Assert.False(actor.HasCapability("invented-capability"));
    }
}
