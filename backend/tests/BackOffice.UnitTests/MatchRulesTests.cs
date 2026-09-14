using BackOffice.Application.Parties;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class MatchRulesTests
{
    public static IEnumerable<object[]> Transitions()
    {
        foreach(var state in new[] {"pending","queried","linked","separate","declined"})
        foreach(var outcome in new[] {"link","separate","decline","query","reopen"})
            yield return [state,outcome];
    }
    [Theory,MemberData(nameof(Transitions))]
    public void EveryStateDecisionPairEitherAdvancesOrRejects(string state,string outcome)
    {
        var open=state is "pending" or "queried";
        if(open && outcome!="reopen")Assert.Equal(new Dictionary<string,string>{{"link","linked"},{"separate","separate"},{"decline","declined"},{"query","queried"}}[outcome],MatchRules.NextState(state,outcome));
        else if(!open && outcome=="reopen")Assert.Equal("pending",MatchRules.NextState(state,outcome));
        else Assert.Throws<MatchTransitionException>(()=>MatchRules.NextState(state,outcome));
    }
    [Fact]
    public void DecisionRequiresBoundedReasonAndRestrictsCandidateToLink()
    {
        var candidate=Guid.NewGuid();var input=MatchRules.Validate(new("link","  Fictional reason\r\nSecond line  ",candidate));
        Assert.Equal("Fictional reason\nSecond line",input.Reason);Assert.Equal(candidate,input.CandidateClientId);
        foreach(var reason in new[] {"", "  ",new string('a',1001),"Fictional\0detail"})Assert.Throws<PartyValidationException>(()=>MatchRules.Validate(new("query",reason)));
        Assert.Throws<PartyValidationException>(()=>MatchRules.Validate(new("separate","Reason",candidate)));
        Assert.Throws<PartyValidationException>(()=>MatchRules.Validate(new("link","Reason",Guid.Empty)));
    }
    [Fact]
    public void UnknownInputsFailWithoutEchoingSubmittedEvidence()
    {
        var error=Assert.Throws<PartyValidationException>(()=>MatchRules.Validate(new("Fictional private value","Reason")));
        Assert.DoesNotContain("Fictional private value",error.Message+string.Join("",error.Issues.Select(x=>x.Message)));
        Assert.Throws<MatchTransitionException>(()=>MatchRules.NextState("unknown","link"));
        Assert.Throws<MatchTransitionException>(()=>MatchRules.NextState("pending","unknown"));
    }
    [Fact]
    public void EvidenceNormalizesDeclaredIdentityWithoutInventingSignalsOrNames()
    {
        var evidence=MatchRules.ValidateEvidence(Identity,[Signal],Rule,"medium");
        Assert.Equal("Fictional A & B",evidence.Identity.LegalName);Assert.Equal("Fictional A & B",evidence.Signals[0].SubmittedValue);
        Assert.Equal(Rule.Id,evidence.Rule.Id);Assert.Equal("cannot-compare",evidence.Signals[0].Result);
        Assert.Single(evidence.Signals);
    }
    [Fact]
    public void EvidenceRejectsMissingOversizedDuplicateAndUnpinnedValues()
    {
        foreach(var signals in new MatchSignal[][] {[],Enumerable.Repeat(Signal,101).ToArray(),[Signal,Signal],[Signal with {Weight="unknown"}],[Signal with {Result="invented"}],[Signal with {CandidateValue=new string('x',501)}]})
            Assert.Throws<PartyValidationException>(()=>MatchRules.ValidateEvidence(Identity,signals,Rule,"high"));
        Assert.Throws<PartyValidationException>(()=>MatchRules.ValidateEvidence(Identity,[Signal],Rule with {Id=Guid.Empty},"high"));
        Assert.Throws<PartyValidationException>(()=>MatchRules.ValidateEvidence(Identity,[Signal],Rule with {Version=0},"high"));
        Assert.Throws<PartyValidationException>(()=>MatchRules.ValidateEvidence(Identity,[Signal],Rule,"certain"));
        Assert.Throws<PartyValidationException>(()=>MatchRules.ValidateEvidence(Identity with {Address=null},[Signal],Rule,"high"));
    }
    private static ClientWrite Identity=>new("  Fictional A & B  ","sole-trader",new("1 Fictional Road","Sheffield","S1 1AA","GB"));
    private static MatchSignal Signal=>new("legal-name","Fictional comparison"," Fictional A & B ","Not supplied","weak","cannot-compare");
    private static MatchRuleSnapshot Rule=>new(Guid.Parse("38000000-0000-4000-8000-000000000001"),1,"refer",true,"Fictional review rule.");
}
