using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingIssueTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void IssuedServicingRequiresItsOwnDecisionAndOneBasedSliceWithoutChangingOriginal(string product)
    {
        using var stream = typeof(ServicingIssueTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-" + product + ".json")!;
        var original = JsonNode.Parse(stream)!.AsObject(); var before = original.ToJsonString();
        var snapshot = original.DeepClone().AsObject(); snapshot["snapshotFormat"] = "issued-servicing-1";
        Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["provenance"] = JsonSerializer.SerializeToNode(new { source="backoffice",sourceQuoteId=Guid.NewGuid(),
            servicingIssueDecisionId=Guid.NewGuid(),baseVersionId=Guid.NewGuid(),revisionId=Guid.NewGuid(),transactionId=Guid.NewGuid(),
            effectiveAt="2026-10-01T00:00:00Z",processedAt="2026-09-18T12:00:00Z",sliceOrdinal=1,inputHash=new string('a',64) });
        Assert.True(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["provenance"]!["sliceOrdinal"] = 0; Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["provenance"]!["sliceOrdinal"] = 100; Assert.True(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["provenance"]!["sliceOrdinal"] = 101; Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        snapshot["provenance"]!["sliceOrdinal"] = 1; snapshot["snapshotFormat"] = "unknown-format";
        Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
        Assert.Equal(before,original.ToJsonString()); Assert.True(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(original)));
    }
}
