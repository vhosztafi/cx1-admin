using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalIncidentTests
{
    private static JsonObject Draft()=>new(){["policyId"]=Guid.NewGuid(),["productCode"]="motor-trade-road-risks"};
    [Fact]
    public void MinimalDraftSavesButCannotLogOrInventContactFacts()
    {
        var draft=JsonSerializer.SerializeToElement(Draft());IncidentRules.Draft(draft);
        var missing=IncidentRules.Missing(draft,"incomplete",false);
        Assert.Contains("occurrence",missing);Assert.Contains("description",missing);Assert.Contains("reportedBy",missing);Assert.Contains("bestContactDescription",missing);
        Assert.Contains("thirdPartyInvolvement",missing);
    }
    [Theory]
    [InlineData("foreign-product-branch")]
    [InlineData("inactive-third-party")]
    [InlineData("unknown-field")]
    [InlineData("bad-date")]
    public void InactiveBranchesAndMalformedFactsAreNotPersisted(string change)
    {
        var draft=Draft();
        if(change=="foreign-product-branch")draft["commercialSubject"]=JsonSerializer.SerializeToNode(new{kind="property",locationId=Guid.NewGuid(),coverCode="buildings"});
        if(change=="inactive-third-party"){draft["thirdPartyInvolvement"]="no";draft["thirdPartyName"]="Inactive details";}
        if(change=="unknown-field")draft["settlementAmount"]="1.00";
        if(change=="bad-date")draft["occurrence"]=JsonSerializer.SerializeToNode(new{occurredOn="2026-02-30",timeZone="Europe/London",precision="date"});
        Assert.Throws<IncidentRuleException>(()=>IncidentRules.Draft(JsonSerializer.SerializeToElement(draft)));
    }
    [Fact]
    public void CompleteFactsStillRequireUnambiguousHistoricalSubject()
    {
        var draft=Draft();draft["occurrence"]=JsonSerializer.SerializeToNode(new{occurredOn="2026-09-16",timeZone="Europe/London",precision="date"});
        draft["kind"]="other";draft["motorSubject"]=JsonSerializer.SerializeToNode(new{kind="third-party-only",itemDescription="Fictional reported property"});
        draft["description"]="A fictional incident description with sufficient factual detail.";draft["reportedBy"]="Fictional reporter";draft["reportingRoute"]="agency";draft["bestContactDescription"]="Fictional contact on 01632 960001";
        draft["thirdPartyInvolvement"]="unknown";
        var value=JsonSerializer.SerializeToElement(draft);IncidentRules.Draft(value);
        Assert.Empty(IncidentRules.Missing(value,"resolved",true));
        Assert.Contains("historical-cover",IncidentRules.Missing(value,"ambiguous",true));
        Assert.Contains("subject",IncidentRules.Missing(value,"resolved",false));
    }
}
