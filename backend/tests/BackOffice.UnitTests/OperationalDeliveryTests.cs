using BackOffice.Application;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalDeliveryTests
{
    [Theory]
    [InlineData("message-send")]
    [InlineData("document-send")]
    public void DeliveryCapabilitiesRemainInternalAndDoNotBroadenParentAuthority(string capability)
    {
        var id=Guid.NewGuid();
        foreach(var role in new[]{"servicing","underwriter","senior-underwriter","agency-admin","system-admin"})
        {
            var actor=new ActorContext(id,null,null,new HashSet<string>{role});
            Assert.True(actor.HasCapability(capability));
            Assert.False((actor with{AgencyId=Guid.NewGuid()}).HasCapability(capability));
        }
        Assert.False(new ActorContext(id,null,null,new HashSet<string>{"finance"}).HasCapability(capability));
        Assert.False(new ActorContext(id,null,null,new HashSet<string>{"agency-admin"}).HasCapability("policy-read"));
    }

    [Fact]
    public void SendRejectsInternalAudienceBlankContentAndMissingRecipients()
    {
        var recipient=Guid.NewGuid();
        Assert.Throws<CommunicationRuleException>(()=>OperationalDeliveryRules.Message("internal","Private",new("Text",[],[])));
        Assert.Throws<CommunicationRuleException>(()=>OperationalDeliveryRules.Message("agency","Subject",new(" ",[recipient],[])));
        Assert.Throws<CommunicationRuleException>(()=>OperationalDeliveryRules.Message("agency","Subject",new("Text",[],[])));
        Assert.Throws<CommunicationRuleException>(()=>OperationalDeliveryRules.Message("agency"," ",new("Text",[recipient],[])));
    }
    [Fact]
    public void PackRequiresExactDistinctFilesButMessageMayHaveNoAttachment()
    {
        var recipient=Guid.NewGuid();var file=Guid.NewGuid();
        OperationalDeliveryRules.Message("agency","Subject",new("Message",[recipient],[]));
        OperationalDeliveryRules.Pack(new([file],[recipient],"Subject","Message"));
        Assert.Throws<CommunicationRuleException>(()=>OperationalDeliveryRules.Pack(new([],[recipient],"Subject","Message")));
        Assert.Throws<CommunicationRuleException>(()=>OperationalDeliveryRules.Pack(new([file,file],[recipient],"Subject","Message")));
    }
}
