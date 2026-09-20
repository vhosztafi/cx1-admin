using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommercialCancellationRetainsHistoricalRiskAndExactDecision(bool servicing)
    {
        var source=Source(servicing);var before=source.ToJsonString();
        Assert.True(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(source)));
        var input=Input();var output=CancellationIssueSnapshot.Create(JsonSerializer.SerializeToElement(source),input);
        using var parsed=JsonDocument.Parse(output);var result=parsed.RootElement;
        Assert.Equal("issued-commercial-cancellation-1",result.GetProperty("snapshotFormat").GetString());
        Assert.True(PolicySnapshotShape.Valid(result));
        foreach(var field in new[]{"insured","risk","cover","premium","term"})
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(source[field]),result.GetProperty(field)));
        Assert.Equal("cancelled",result.GetProperty("cancellation").GetProperty("outcome").GetString());
        Assert.Equal(input.EffectiveAt,result.GetProperty("cancellation").GetProperty("effectiveAt").GetDateTimeOffset());
        var provenance=result.GetProperty("provenance");
        Assert.Equal(input.DecisionId,provenance.GetProperty("cancellationIssueDecisionId").GetGuid());
        Assert.Equal(input.ApprovalId,provenance.GetProperty("cancellationApprovalId").GetGuid());
        Assert.Equal(input.PreviewId,provenance.GetProperty("cancellationPreviewId").GetGuid());
        Assert.Equal(input.BaseVersionId,provenance.GetProperty("baseVersionId").GetGuid());
        Assert.Equal(input.PreviewHash,provenance.GetProperty("inputHash").GetString());
        Assert.Equal(before,source.ToJsonString());
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(result,input));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(JsonSerializer.SerializeToElement(source),input with{ApprovalId=Guid.Empty}));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(JsonSerializer.SerializeToElement(source),input with{PreviewHash="invalid"}));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(JsonSerializer.SerializeToElement(source),input with{EffectiveAt=DateTimeOffset.Parse("2027-09-15T08:00:00Z")}));
    }

    [Theory]
    [InlineData("motor-format")]
    [InlineData("motor-risk")]
    [InlineData("missing-approval")]
    [InlineData("wrong-outcome")]
    [InlineData("unknown-cancellation-field")]
    [InlineData("missing-term")]
    public void CommercialCancellationShapeRejectsMixedOrIncompleteHistory(string mutation)
    {
        var json=CancellationIssueSnapshot.Create(JsonSerializer.SerializeToElement(Source(false)),Input());
        var value=JsonNode.Parse(json)!.AsObject();
        if(mutation=="motor-format")value["snapshotFormat"]="issued-cancellation-1";
        if(mutation=="motor-risk")value["risk"]!["drivers"]=new JsonArray();
        if(mutation=="missing-approval")value["provenance"]!.AsObject().Remove("cancellationApprovalId");
        if(mutation=="wrong-outcome")value["cancellation"]!["outcome"]="active";
        if(mutation=="unknown-cancellation-field")value["cancellation"]!["refundPaid"]="100.00";
        if(mutation=="missing-term")value.Remove("term");
        Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(value)));
    }

    private static CancellationSnapshotInput Input()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        DateTimeOffset.Parse("2026-10-01T00:00:00Z"),DateTimeOffset.Parse("2026-09-20T00:00:00Z"),new string('a',64),"insured-request","demo-servicing-1");

    private static JsonObject Source(bool servicing)
    {
        using var ready=typeof(CommercialCancellationTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var issued=typeof(CommercialCancellationTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-motor-trade-road-risks.json")!;
        var value=JsonNode.Parse(ready)!.AsObject();var envelope=JsonNode.Parse(issued)!;
        value.Remove("format");value.Remove("termIntent");value["snapshotFormat"]="issued-commercial-1";
        foreach(var key in new[]{"productVersionId","term","premium","provenance"})value[key]=envelope[key]!.DeepClone();
        foreach(var key in new[]{"clientId","clientAgencyRelationshipId"})value["insured"]![key]=envelope["insured"]![key]!.DeepClone();
        var location=value["risk"]!["locations"]![0]!["id"]!.GetValue<string>();
        value["cover"]!["sections"]=JsonSerializer.SerializeToNode(new object[]{
            new{id=Guid.NewGuid(),code="property",limit="1000000.00",targetIds=new[]{location}},
            new{id=Guid.NewGuid(),code="glass",basis="Included for the declared premises",targetIds=new[]{location}},
            new{id=Guid.NewGuid(),code="named-suppliers",limit="100000.00",targetIds=Array.Empty<string>()}});
        value["cover"]!["endorsements"]=new JsonArray();value["cover"]!["warranties"]=new JsonArray();
        if(servicing)
        {
            value["snapshotFormat"]="issued-commercial-servicing-1";
            value["provenance"]=JsonSerializer.SerializeToNode(new{source="backoffice",sourceQuoteId=Guid.NewGuid(),servicingIssueDecisionId=Guid.NewGuid(),
                baseVersionId=Guid.NewGuid(),revisionId=Guid.NewGuid(),transactionId=Guid.NewGuid(),effectiveAt="2026-09-20T00:00:00Z",processedAt="2026-09-20T00:00:00Z",sliceOrdinal=1,inputHash=new string('a',64)});
        }
        return value;
    }
}
