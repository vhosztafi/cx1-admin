using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingTermsTests
{
    private static readonly DateTimeOffset Now=new(2026,9,18,10,0,0,TimeSpan.Zero);
    private static readonly Guid DeliveryId=Guid.NewGuid();
    private static readonly ServicingTermsSubject Terms=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        Guid.NewGuid(),Guid.NewGuid(),new string('a',64));
    private static readonly string Assurance=new('b',64);

    [Fact]
    public void LateDeliveryCannotApplyToAnyDifferentServicingOwnerOrTermsVersion()
    {
        Assert.True(ServicingTermsRules.CanApplyDelivery(Terms,Terms,DeliveryId,DeliveryId,"queued",Now,Now.AddDays(1)));
        foreach(var changed in ChangedOwners())
            Assert.False(ServicingTermsRules.CanApplyDelivery(Terms,changed,DeliveryId,DeliveryId,"queued",Now,Now.AddDays(1)));
        Assert.False(ServicingTermsRules.CanApplyDelivery(Terms,Terms,DeliveryId,Guid.NewGuid(),"queued",Now,Now.AddDays(1)));
        Assert.False(ServicingTermsRules.CanApplyDelivery(Terms,Terms,Guid.Empty,Guid.Empty,"queued",Now,Now.AddDays(1)));
        Assert.False(ServicingTermsRules.CanApplyDelivery(Terms,Terms,DeliveryId,DeliveryId,"queued",Now,Now));
    }

    [Theory]
    [InlineData("delivered")]
    [InlineData("failed")]
    [InlineData("superseded")]
    [InlineData("cancelled")]
    public void OnlyTheCurrentQueuedDeliveryMayAcquireAnAppliedOutcome(string state)=>
        Assert.False(ServicingTermsRules.CanApplyDelivery(Terms,Terms,DeliveryId,DeliveryId,state,Now,Now.AddDays(1)));

    [Fact]
    public void AcceptanceNeedsExactDeliveredTermsAndTheCurrentAssuranceFingerprint()
    {
        bool Accept(ServicingTermsSubject current,string assurance)=>ServicingTermsRules.CanAccept(Terms,current,DeliveryId,DeliveryId,
            "delivered",Now.AddMinutes(-1),Now,Now,Now.AddDays(1),Assurance,assurance,"Fictional broker","written");
        Assert.True(Accept(Terms,Assurance));
        foreach(var changed in ChangedOwners())Assert.False(Accept(changed,Assurance));
        Assert.False(Accept(Terms,new string('c',64)));Assert.False(Accept(Terms,"invalid"));
        Assert.False(Accept(Terms with{TermsHash="invalid"},Assurance));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("failed")]
    [InlineData("superseded")]
    public void ARequestToDeliverIsNotAcceptanceEvidence(string state)=>
        Assert.False(ServicingTermsRules.CanAccept(Terms,Terms,DeliveryId,DeliveryId,state,Now.AddMinutes(-1),Now,Now,Now.AddDays(1),
            Assurance,Assurance,"Fictional broker","email"));

    [Fact]
    public void AcceptanceRequiresAnActualOrderedUtcWindowAndRecognisedAccepterChannel()
    {
        bool Accept(DateTimeOffset? delivered,DateTimeOffset accepted,DateTimeOffset expires,string name="Fictional insured",string channel="telephone")=>
            ServicingTermsRules.CanAccept(Terms,Terms,DeliveryId,DeliveryId,"delivered",delivered,accepted,Now,expires,Assurance,Assurance,name,channel);
        Assert.True(Accept(Now,Now,Now.AddDays(1)));
        Assert.False(Accept(null,Now,Now.AddDays(1)));Assert.False(Accept(Now,Now.AddTicks(-1),Now.AddDays(1)));
        Assert.False(Accept(Now,Now.AddTicks(1),Now.AddDays(1)));Assert.False(Accept(Now,Now,Now));
        Assert.False(Accept(Now,Now.ToOffset(TimeSpan.FromHours(1)),Now.AddDays(1)));
        Assert.False(Accept(Now,Now,Now.AddDays(1)," "));Assert.False(Accept(Now,Now,Now.AddDays(1),"Fictional\ninsured"));
        Assert.False(Accept(Now,Now,Now.AddDays(1),channel:"auto-accepted"));
    }

    private static ServicingTermsSubject[] ChangedOwners()=>[Terms with{DraftId=Guid.NewGuid()},Terms with{CycleId=Guid.NewGuid()},
        Terms with{RevisionId=Guid.NewGuid()},Terms with{BaseVersionId=Guid.NewGuid()},Terms with{RatingId=Guid.NewGuid()},
        Terms with{TermsId=Guid.NewGuid()},Terms with{TermsHash=new string('c',64)},Terms with{DraftId=Guid.Empty}];
}
