using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CancellationIssueTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void SnapshotBuilderPreservesHistoricalDeclarationsAndRejectsRepeatedCancellation(string product)
    {
        using var stream=typeof(CancellationIssueTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-"+product+".json")!;
        using var original=JsonDocument.Parse(stream);
        var input=new CancellationSnapshotInput(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"),DateTimeOffset.Parse("2026-09-19T12:00:00Z"),new string('a',64),"insured-request","demo-servicing-1");
        var snapshot=CancellationIssueSnapshot.Create(original.RootElement,input);
        using var parsed=JsonDocument.Parse(snapshot);
        foreach(var field in new[]{"insured","risk","cover","premium","term"})
            Assert.True(JsonElement.DeepEquals(original.RootElement.GetProperty(field),parsed.RootElement.GetProperty(field)));
        Assert.True(PolicySnapshotShape.Valid(parsed.RootElement));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(parsed.RootElement,input));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(original.RootElement,input with {ApprovalId=Guid.Empty}));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(original.RootElement,input with {EffectiveAt=DateTimeOffset.Parse("2027-09-15T08:00:00Z")}));
        Assert.Throws<ArgumentException>(()=>CancellationIssueSnapshot.Create(original.RootElement,input with {PreviewHash="not-a-hash"}));
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void CancellationRetainsRiskButRequiresItsOwnApprovalAndExplicitOutcome(string product)
    {
        using var stream=typeof(CancellationIssueTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-"+product+".json")!;
        var original=JsonNode.Parse(stream)!.AsObject();var bytes=original.ToJsonString();
        var snapshot=original.DeepClone().AsObject();snapshot["snapshotFormat"]="issued-cancellation-1";
        Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["provenance"]=JsonSerializer.SerializeToNode(new {source="backoffice",sourceQuoteId=Guid.NewGuid(),
            cancellationIssueDecisionId=Guid.NewGuid(),cancellationApprovalId=Guid.NewGuid(),cancellationPreviewId=Guid.NewGuid(),
            baseVersionId=Guid.NewGuid(),revisionId=Guid.NewGuid(),transactionId=Guid.NewGuid(),
            effectiveAt="2026-10-01T00:00:00Z",processedAt="2026-09-19T12:00:00Z",sliceOrdinal=1,inputHash=new string('a',64)});
        snapshot["cancellation"]=JsonSerializer.SerializeToNode(new {outcome="cancelled",reasonCode="insured-request",
            effectiveAt="2026-10-01T00:00:00Z",ruleVersion="demo-servicing-1"});
        Assert.True(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        Assert.Equal(original["risk"]!.ToJsonString(),snapshot["risk"]!.ToJsonString());
        snapshot["cancellation"]!["outcome"]="active";Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["cancellation"]!["outcome"]="cancelled";
        snapshot["provenance"]!.AsObject().Remove("cancellationApprovalId");Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        Assert.Equal(bytes,original.ToJsonString());Assert.True(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(original)));
    }
}
