using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class PolicySnapshotShapeTests
{
    private static JsonObject Example(string product = "motor-trade-road-risks")
    {
        using var stream = typeof(PolicySnapshotShapeTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-" + product + ".json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    private static bool Valid(JsonNode value) => PolicySnapshotShape.Valid(JsonSerializer.SerializeToElement(value));

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void IssuedShapeRetainsActualDeclarationsWithoutInventedLicenceDates(string product)
    {
        var snapshot = Example(product); Assert.True(Valid(snapshot));
        Assert.Null(snapshot["risk"]!["drivers"]![0]!["licence"]!["testDate"]);
        Assert.NotNull(snapshot["risk"]!["drivers"]![0]!["licence"]!["issuedOn"]);
        Assert.NotEmpty(snapshot["risk"]!["responses"]!["answers"]!.AsArray());
        Assert.NotNull(snapshot["premium"]!["settlement"]!["termsVersionId"]);
    }

    [Theory]
    [InlineData("missing-premium")]
    [InlineData("unknown-authority")]
    [InlineData("fractional-money")]
    [InlineData("missing-client")]
    public void IssuedShapeRejectsIncompleteOrOpenFinancialAuthority(string change)
    {
        var snapshot = Example();
        if (change == "missing-premium") snapshot.Remove("premium");
        if (change == "unknown-authority") snapshot["ignoreAuthority"] = true;
        if (change == "fractional-money") snapshot["premium"]!["termPremium"] = "100.001";
        if (change == "missing-client") snapshot["insured"]!.AsObject().Remove("clientId");
        Assert.False(Valid(snapshot)); Assert.NotEmpty(PolicySnapshotShape.Errors(JsonSerializer.SerializeToElement(snapshot)));
    }

    [Fact]
    public void ThirdPartyOnlySectionDoesNotInventMonetaryCover()
    {
        var snapshot = Example(); snapshot["cover"]!["sections"] = JsonSerializer.SerializeToNode(new[] { new { id = Guid.NewGuid(), code = "road-risks", coverLevel = "third-party-only" } });
        Assert.True(Valid(snapshot)); snapshot["cover"]!["sections"]![0]!["limit"] = "0.00"; Assert.False(Valid(snapshot));
    }
}
