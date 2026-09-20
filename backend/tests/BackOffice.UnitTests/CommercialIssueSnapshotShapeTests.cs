using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialIssueSnapshotShapeTests
{
    private static JsonObject Snapshot()
    {
        using var ready = typeof(CommercialIssueSnapshotShapeTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        using var issued = typeof(CommercialIssueSnapshotShapeTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-motor-trade-road-risks.json")!;
        var snapshot = JsonNode.Parse(ready)!.AsObject(); var source = JsonNode.Parse(issued)!;
        snapshot.Remove("format"); snapshot.Remove("termIntent"); snapshot["snapshotFormat"] = "issued-commercial-1";
        foreach (var key in new[] { "productVersionId", "term", "premium", "provenance" }) snapshot[key] = source[key]!.DeepClone();
        foreach (var key in new[] { "clientId", "clientAgencyRelationshipId" }) snapshot["insured"]![key] = source["insured"]![key]!.DeepClone();
        var location = snapshot["risk"]!["locations"]![0]!["id"]!.GetValue<string>();
        snapshot["cover"]!["sections"] = JsonSerializer.SerializeToNode(new object[] {
            new { id = Guid.NewGuid(), code = "property", limit = "1000000.00", targetIds = new[] { location } },
            new { id = Guid.NewGuid(), code = "glass", basis = "Included for the declared premises", targetIds = new[] { location } },
            new { id = Guid.NewGuid(), code = "named-suppliers", limit = "100000.00", targetIds = Array.Empty<string>() }
        });
        snapshot["cover"]!["endorsements"] = new JsonArray(); snapshot["cover"]!["warranties"] = new JsonArray(); return snapshot;
    }

    [Fact]
    public void CommercialIssuedShapeRetainsItsOwnRiskAndSelectedCover()
    {
        var snapshot = Snapshot(); Assert.Empty(PolicySnapshotShape.Errors(JsonSerializer.SerializeToElement(snapshot)));
        Assert.Null(snapshot["risk"]!["driverBasis"]); Assert.Null(snapshot["cover"]!["sections"]![1]!["limit"]);
    }

    [Theory]
    [InlineData("motor-risk")]
    [InlineData("motor-format")]
    [InlineData("unowned-property-section")]
    [InlineData("invented-glass-limit")]
    [InlineData("missing-premium")]
    public void CommercialIssuedShapeRejectsMixedOrIncompleteContracts(string change)
    {
        var snapshot = Snapshot();
        if (change == "motor-risk") snapshot["risk"]!["drivers"] = new JsonArray();
        if (change == "motor-format") snapshot["snapshotFormat"] = "issued-quote-1";
        if (change == "unowned-property-section") snapshot["cover"]!["sections"]![0]!["targetIds"] = new JsonArray();
        if (change == "invented-glass-limit") snapshot["cover"]!["sections"]![1]!["limit"] = "0.00";
        if (change == "missing-premium") snapshot.Remove("premium");
        Assert.False(PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(snapshot)));
    }
}
