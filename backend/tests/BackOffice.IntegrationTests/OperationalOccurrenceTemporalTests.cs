using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Policies;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalOccurrenceTemporalTests
{
    [Theory]
    [InlineData("whole-day","resolved")]
    [InlineData("midday-adjustment","ambiguous")]
    [InlineData("midday-cancellation","partly-uncovered")]
    [InlineData("backdate-not-known","resolved")]
    [InlineData("backdate-known","ambiguous")]
    [InlineData("expired","uncovered")]
    public void RetainedTemporalBoundariesDetermineWholeClaimedInterval(string scenario,string expected)
    {
        var policy=Guid.NewGuid();var term=Guid.NewGuid();var day=new DateTimeOffset(2026,9,16,0,0,0,TimeSpan.Zero);
        var initial=new PolicyTemporalCandidate(policy,term,Guid.NewGuid(),day.AddDays(-10),day.AddDays(30),day.AddDays(-10),day.AddDays(-11),1,1,"new-business");
        var change=initial with{VersionId=Guid.NewGuid(),EffectiveAt=day.AddHours(12),ProcessedAt=scenario.StartsWith("backdate",StringComparison.Ordinal)?day.AddDays(3):day.AddDays(-1),TransactionSequence=2,Kind=scenario=="midday-cancellation"?"cancellation":"adjustment"};
        var window=new IncidentOccurrenceWindow(day,day.AddDays(1),false);var known=day.AddDays(scenario=="backdate-known"?4:2);
        var history=scenario=="whole-day"?new[]{initial}:scenario=="expired"?new[]{initial with{EndsAt=day}}:new[]{initial,change};
        var result=IncidentOccurrenceResolver.Intervals(history,policy,window,known);
        var state=IncidentOccurrenceRules.State(window,known,result.Select(x=>new IncidentOccurrenceSlice(x.Candidate.VersionId,new string('a',64),x.From,x.To)).ToArray());
        Assert.Equal(expected,state);
        if(scenario=="backdate-not-known")Assert.All(result,x=>Assert.Equal(initial.VersionId,x.Candidate.VersionId));
        if(scenario=="midday-cancellation")Assert.Equal(day.AddHours(12),result.Last().To);
    }
}
