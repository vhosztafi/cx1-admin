using System.Text.Json;
using BackOffice.Application.Parties;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class SupportFlagTests
{
    private static readonly DateOnly Today=new(2026,9,14);
    private static FlagWrite Valid()=>new("vulnerability","health","  Use written communication.\r\nAllow additional time.  ","verbal-consent",Today," Fictional request ",[]);
    [Fact]
    public void InternalOnlyFlagPreservesFunctionalMultilineTextAndRequiresExplicitEmptyGrants()
    {
        var result=SupportFlagRules.Validate(Valid(),Today);
        Assert.Equal("Use written communication.\nAllow additional time.",result.InternalInstruction);
        Assert.Equal("Fictional request",result.Reason);Assert.Empty(result.VisibleRelationshipIds);Assert.Null(result.AgencyInstruction);
        Assert.Throws<PartyValidationException>(()=>SupportFlagRules.Validate(Valid() with {VisibleRelationshipIds=null},Today));
    }
    [Theory]
    [InlineData("declined")]
    [InlineData("not-asked")]
    [InlineData("")]
    public void UnacceptedConsentStopsBeforeSensitiveFieldValidationAndDoesNotEchoContent(string basis)
    {
        const string detail="SENSITIVE-FICTIONAL-DETAIL";
        var error=Assert.Throws<PartyValidationException>(()=>SupportFlagRules.Validate(Valid() with {ConsentBasis=basis,InternalInstruction=detail},Today));
        Assert.Single(error.Issues);Assert.DoesNotContain(detail,JsonSerializer.Serialize(error.Issues));Assert.DoesNotContain(detail,error.Message);
    }
    [Fact]
    public void SharedFlagsRequireWordingAndUniqueBoundedGrants()
    {
        var first=Guid.Parse("10000000-0000-4000-8000-000000000001");var second=Guid.Parse("10000000-0000-4000-8000-000000000002");
        var request=Valid() with {VisibleRelationshipIds=[second,first],AgencyInstruction=" Allow more time "};
        var result=SupportFlagRules.Validate(request,Today);Assert.Equal(new[] {first,second},result.VisibleRelationshipIds);Assert.Equal("Allow more time",result.AgencyInstruction);
        Assert.Equal(new[] {second,first},request.VisibleRelationshipIds);
        foreach(var invalid in new[] {request with {AgencyInstruction=null},request with {VisibleRelationshipIds=[first,first]},request with {VisibleRelationshipIds=[Guid.Empty]},request with {VisibleRelationshipIds=Enumerable.Range(0,101).Select(_=>Guid.NewGuid()).ToArray()}})
            Assert.Throws<PartyValidationException>(()=>SupportFlagRules.Validate(invalid,Today));
    }
    [Fact]
    public void ReviewDateMayBeTodayOrFutureButNotMissingOrPast()
    {
        Assert.Equal(Today,SupportFlagRules.Validate(Valid(),Today).ReviewOn);
        Assert.Equal(Today.AddDays(30),SupportFlagRules.Validate(Valid() with {ReviewOn=Today.AddDays(30)},Today).ReviewOn);
        foreach(var date in new[] {default(DateOnly),Today.AddDays(-1)})Assert.Throws<PartyValidationException>(()=>SupportFlagRules.Validate(Valid() with {ReviewOn=date},Today));
        // Replayed intent remains normalizable after its review date; new mutations still check today's date.
        var prior=SupportFlagRules.Validate(Valid() with {ReviewOn=Today.AddDays(-1)});
        Assert.Throws<PartyValidationException>(()=>SupportFlagRules.ValidateReviewDate(prior.ReviewOn,Today));
    }
    [Fact]
    public void RejectsUnknownChoicesBlankInstructionsOversizeReasonsAndHiddenControlCharacters()
    {
        foreach(var invalid in new[] {Valid() with {TypeCode="unknown"},Valid() with {InternalCategory="unknown"},Valid() with {InternalInstruction=" "},
            Valid() with {Reason=new string('x',1001)},Valid() with {AgencyInstruction=" "},Valid() with {InternalInstruction="hidden\0text"},Valid() with {InternalInstruction=new string('x',2001)}})
            Assert.Throws<PartyValidationException>(()=>SupportFlagRules.Validate(invalid,Today));
    }
}
