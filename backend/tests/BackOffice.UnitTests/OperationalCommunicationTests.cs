using BackOffice.Application;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalCommunicationTests
{
    [Fact]
    public void DraftCanBeBlankButRejectsNullDuplicateUnboundedOrInternalRecipients()
    {
        CommunicationRules.Draft(new("",[],[]),"agency");
        CommunicationRules.Draft(new("",[],[]),"internal");
        var id=Guid.NewGuid();
        foreach(var input in new MessageDraftWrite[]{new(null!,[],[]),new(new string('x',8001),[],[]),new("",null!,[]),
            new("",[id,id],[]),new("",[],[Guid.Empty]),new("",[],[id,id]),new("",Enumerable.Range(0,51).Select(_=>Guid.NewGuid()).ToArray(),[]),
            new("",[],Enumerable.Range(0,21).Select(_=>Guid.NewGuid()).ToArray())})
            Assert.Throws<CommunicationRuleException>(()=>CommunicationRules.Draft(input,"agency"));
        Assert.Throws<CommunicationRuleException>(()=>CommunicationRules.Draft(new("",[id],[]),"internal"));
    }
    [Fact]
    public void AudienceHasOneClosedRelationshipShapeAndNotesCannotBeBlank()
    {
        CommunicationRules.Thread(new("internal","Review"));
        CommunicationRules.Thread(new("agency","Review",Guid.NewGuid()));
        foreach(var input in new ThreadWrite[]{new("public","Review"),new("agency","Review"),new("agency","Review",Guid.Empty),new("internal","Review",Guid.NewGuid()),new("internal"," ")})
            Assert.Throws<CommunicationRuleException>(()=>CommunicationRules.Thread(input));
        Assert.Throws<CommunicationRuleException>(()=>CommunicationRules.Note("  "));
        Assert.Throws<CommunicationRuleException>(()=>CommunicationRules.Note(null));
    }
    [Theory]
    [InlineData("person@example.test",true)]
    [InlineData("Name <person@example.test>",false)]
    [InlineData("a@example.test,b@example.test",false)]
    [InlineData("a@example.test\r\nBcc: b@example.test",false)]
    [InlineData("",false)]
    [InlineData(null,false)]
    public void RecipientAddressIsOnePlainMailbox(string? address,bool expected)=>Assert.Equal(expected,CommunicationRules.Email(address));
    [Theory]
    [InlineData("internal-note-read")]
    [InlineData("internal-note-write")]
    [InlineData("message-read")]
    [InlineData("message-write")]
    public void CommunicationCapabilitiesRemainInternalAndDoNotGrantParentAuthority(string capability)
    {
        var actor=new ActorContext(Guid.NewGuid(),null,null,new HashSet<string>{"underwriter"});
        Assert.True(actor.HasCapability(capability));
        Assert.False((actor with{AgencyId=Guid.NewGuid()}).HasCapability(capability));
        Assert.False((actor with{Roles=new HashSet<string>{"finance"}}).HasCapability(capability));
        var agencyAdmin=actor with{Roles=new HashSet<string>{"agency-admin"}};
        Assert.True(agencyAdmin.HasCapability(capability));
        Assert.False(agencyAdmin.HasCapability("policy-read"));
        Assert.False(agencyAdmin.HasCapability("support-internal-read"));
    }
}
