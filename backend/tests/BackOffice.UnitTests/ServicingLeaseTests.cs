using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingLeaseTests
{
    [Theory]
    [InlineData("servicing", true, false)]
    [InlineData("underwriter", true, true)]
    [InlineData("senior-underwriter", true, true)]
    [InlineData("system-admin", false, false)]
    [InlineData("agency-admin", false, false)]
    public void ServicingCapabilitiesHaveNoAdministrativeOrAgencyBypass(string role, bool write, bool takeover)
    {
        var actor = new BackOffice.Application.ActorContext(Guid.NewGuid(), null, null, new HashSet<string> { role });
        Assert.Equal(write, actor.HasCapability("policy-draft-write"));
        Assert.Equal(write, actor.HasCapability("policy-draft-rate"));
        Assert.Equal(takeover, actor.HasCapability("policy-draft-takeover"));
        var agency = actor with { AgencyId = Guid.NewGuid() };
        Assert.False(agency.HasCapability("policy-draft-write")); Assert.False(agency.HasCapability("policy-draft-takeover"));
        Assert.False(agency.HasCapability("policy-draft-rate"));
    }

    private static readonly DateTimeOffset Now=new(2026,9,17,12,0,0,TimeSpan.Zero);
    private static readonly Guid Holder=Guid.NewGuid(), Other=Guid.NewGuid();

    [Fact]
    public void FreshAcquisitionHasFiveMinuteExpiryAndNewFence()
    {
        var first=ServicingLeaseRules.Acquire(null,Holder,Now,false,false,null);
        Assert.Equal(Now.AddMinutes(5),first.ExpiresAt);Assert.Equal(Holder,first.HolderId);
        Assert.NotEqual(Guid.Empty,first.Token);Assert.Equal(1,first.Generation);
        var second=ServicingLeaseRules.Acquire(first,Holder,Now.AddMinutes(1),false,false,null);
        Assert.Equal(2,second.Generation);Assert.NotEqual(first.Token,second.Token);
    }

    [Fact]
    public void AnotherLiveHolderRequiresExplicitTakeoverCapabilityAndReason()
    {
        var first=ServicingLeaseRules.Acquire(null,Holder,Now,false,false,null);
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Acquire(first,Other,Now,false,true,"Reviewed takeover request"));
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Acquire(first,Other,Now,true,false,"Reviewed takeover request"));
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Acquire(first,Other,Now,true,true," "));
        var takeover=ServicingLeaseRules.Acquire(first,Other,Now,true,true,"Previous editor unavailable");
        Assert.Equal(Other,takeover.HolderId);Assert.NotEqual(first.Token,takeover.Token);
    }

    [Fact]
    public void ExpiryIsExclusiveAndOldFenceCannotRenewOrReleaseNewOwnership()
    {
        var first=ServicingLeaseRules.Acquire(null,Holder,Now,false,false,null);
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Renew(first,Holder,first.Token,first.ExpiresAt));
        var replacement=ServicingLeaseRules.Acquire(first,Other,first.ExpiresAt,false,false,null);
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Renew(replacement,Holder,first.Token,first.ExpiresAt));
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Release(replacement,Holder,first.Token,first.ExpiresAt));
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Renew(replacement,Other,first.Token,first.ExpiresAt));
    }

    [Fact]
    public void RenewalPreservesFenceAndReleaseCannotBeRenewed()
    {
        var first=ServicingLeaseRules.Acquire(null,Holder,Now,false,false,null);
        var renewed=ServicingLeaseRules.Renew(first,Holder,first.Token,Now.AddMinutes(2));
        Assert.Equal(first.Token,renewed.Token);Assert.Equal(first.Generation,renewed.Generation);
        Assert.Equal(Now.AddMinutes(7),renewed.ExpiresAt);
        var released=ServicingLeaseRules.Release(renewed,Holder,renewed.Token,Now.AddMinutes(3));
        Assert.False(released.Active);
        Assert.Throws<ArgumentException>(()=>ServicingLeaseRules.Renew(released,Holder,released.Token,Now.AddMinutes(3)));
    }
}
