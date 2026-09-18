using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed partial class ServicingProposalTests
{
    [Fact]
    public void RenewalProposalProjectsConfiguredShortTermWithoutChangingCarriedRiskIdentities()
    {
        var snapshot=Snapshot();var before=snapshot.ToJsonString();
        var prepared=RenewalPreparationRules.Term(Context().EndsAt,6,[6,12]);
        var envelope=JsonNode.Parse(Draft())!;var intent=prepared.Intent;
        envelope["commonEffectiveIntent"]=JsonSerializer.SerializeToNode(new{localDate=intent.GetProperty("localStartDate").GetString(),
            localTime=intent.GetProperty("localStartTime").GetString(),timeZone="Europe/London",utcOffsetMinutes=intent.GetProperty("utcOffsetMinutes").GetInt32()});
        var context=Context() with{StartsAt=prepared.Term.StartsAt,EndsAt=prepared.Term.EndsAt,LatestIssuedEffectiveAt=prepared.Term.StartsAt,CoverageTerm=prepared.Term};
        var result=ServicingProposalRules.Assess(before,envelope.ToJsonString(),context);
        Assert.Equal(prepared.Term,QuoteTerm.Assess(result.Proposed.GetProperty("termIntent")).Term);
        Assert.True(JsonElement.DeepEquals(result.Base.GetProperty("risk"),result.Proposed.GetProperty("risk")));
        Assert.DoesNotContain(result.ReadinessIssues,x=>x.Code=="effective-outside-term");Assert.Equal(before,snapshot.ToJsonString());
    }
}
