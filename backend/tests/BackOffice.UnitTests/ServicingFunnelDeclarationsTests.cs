using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;
namespace BackOffice.UnitTests;
public sealed partial class ServicingProposalTests
{
    [Fact]
    public void RiskDeclarationsArePolicyOwnedAndCannotReplaceIndependentRiskRecords()
    {
        var snapshot=Snapshot();var original=snapshot.ToJsonString();
        var change=Change("risk-details",PolicyId,"update",new JsonObject{["materialFacts"]="Fictional updated risk declaration"});
        var assessed=ServicingProposalRules.Assess(original,Draft(change),Context());
        Assert.Equal("Fictional updated risk declaration",assessed.Proposed.GetProperty("risk").GetProperty("materialFacts").GetString());
        Assert.Equal(snapshot["risk"]!["drivers"]!.ToJsonString(),JsonNode.Parse(assessed.Proposed.GetProperty("risk").GetProperty("drivers").GetRawText())!.ToJsonString());
        Assert.Equal(original,snapshot.ToJsonString());
        change["riskItemId"]=Guid.NewGuid().ToString();Assert.Throws<QuoteValidationException>(()=>ServicingProposalRules.Assess(original,Draft(change),Context()));
        change["riskItemId"]=PolicyId.ToString();change["payload"]!["drivers"]=new JsonArray();Assert.Throws<QuoteInputException>(()=>ServicingProposalInput.Parse(Draft(change),Base));
    }
}
