using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteItemIdentityTests
{
    private static string Id(int n) => $"aaaaaaaa-0000-4000-8000-{n:000000000000}";
    private static JsonObject Fixture() => JsonSerializer.SerializeToNode(new {
        risk = new { drivers = new[] { new { id = Id(1), losses = new[] { new { id = Id(4), riskItemId = Id(2) } } } },
            vehicles = new[] { new { id = Id(2), ownerDriverId = Id(1) } }, premises = new[] { new { id = Id(3) } }, specifiedVehicleIds = new[] { Id(2) } },
        cover = new { temporaryEuropeanCover = new[] { new { id = Id(5), driverIds = new[] { Id(1) } } } }
    })!.AsObject();
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return QuoteItemIdentity.Validate(doc.RootElement);
    }

    [Fact]
    public void TypedLinksResolveWithinCurrentDocumentWithoutMutation()
    {
        var p = Fixture(); var before = p.ToJsonString();
        Assert.Empty(Check(p)); Assert.Equal(before, p.ToJsonString());
        p["risk"]!["vehicles"]![0]!["ownerDriverId"] = Id(1).ToUpperInvariant();
        Assert.Empty(Check(p));
    }

    [Fact]
    public void GlobalIdsIncludeNestedHistoryAndCoverCollections()
    {
        var p = Fixture(); p["cover"]!["temporaryEuropeanCover"]![0]!["id"] = Id(4).ToUpperInvariant();
        Assert.Equal(new QuoteFieldIssue("duplicate-item-id", "/cover/temporaryEuropeanCover/0/id"), Assert.Single(Check(p)));
    }

    [Fact]
    public void WrongItemTypesCannotSatisfyOwnerSpecifiedTripOrIncidentReferences()
    {
        var p = Fixture();
        p["risk"]!["specifiedVehicleIds"]![0] = Id(1);
        p["risk"]!["vehicles"]![0]!["ownerDriverId"] = Id(3);
        p["cover"]!["temporaryEuropeanCover"]![0]!["driverIds"]![0] = Id(2);
        p["risk"]!["drivers"]![0]!["losses"]![0]!["riskItemId"] = Id(4);
        var issues = Check(p);
        Assert.Equal(new[] { "/risk/specifiedVehicleIds/0", "/risk/vehicles/0/ownerDriverId", "/cover/temporaryEuropeanCover/0/driverIds/0", "/risk/drivers/0/losses/0/riskItemId" }, issues.Select(i => i.Path));
        Assert.All(issues, i => Assert.Equal("unknown-item-reference", i.Code));
    }

    [Fact]
    public void RemovingLinkedDriverRequiresExplicitLinkCorrection()
    {
        var p = Fixture(); p["risk"]!["drivers"]!.AsArray().Clear();
        Assert.Equal(2, Check(p).Count);
        p["risk"]!["vehicles"]![0]!.AsObject().Remove("ownerDriverId");
        p["cover"]!["temporaryEuropeanCover"]![0]!.AsObject().Remove("driverIds");
        Assert.Empty(Check(p));
    }

    [Fact]
    public void ReferenceListsRejectCaseInsensitiveDuplicates()
    {
        var p = Fixture();
        p["risk"]!["specifiedVehicleIds"]!.AsArray().Add(Id(2).ToUpperInvariant());
        p["cover"]!["temporaryEuropeanCover"]![0]!["driverIds"]!.AsArray().Add(Id(1).ToUpperInvariant());
        Assert.Equal(2, Check(p).Count);
        Assert.All(Check(p), i => Assert.Equal("duplicate-item-reference", i.Code));
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("aaaaaaaa000040008000000000000002")]
    public void InvalidOrEmptyIdsAreReportedWithoutTreatingThemAsMembers(string id)
    {
        var p = Fixture(); p["risk"]!["vehicles"]![0]!["id"] = id;
        Assert.Contains(new QuoteFieldIssue("invalid-item-id", "/risk/vehicles/0/id"), Check(p));
        Assert.Contains(new QuoteFieldIssue("unknown-item-reference", "/risk/specifiedVehicleIds/0"), Check(p));
    }

    [Fact]
    public void MalformedLinksAndEscapedPointersDoNotExposeValues()
    {
        var p = Fixture(); p["risk"]!["specifiedVehicleIds"] = "sensitive";
        Assert.Equal(new QuoteFieldIssue("invalid-item-reference-list", "/risk/specifiedVehicleIds"), Assert.Single(Check(p)));
        Assert.Equal(new QuoteFieldIssue("invalid-item-id", "/a~1b~0c/id"), Assert.Single(Check(JsonNode.Parse("{\"a/b~c\":{\"id\":null}}")!)));
    }

    [Fact]
    public void PartialDraftWithoutItemsHasNoInventedIdentityRequirements()
    {
        Assert.Empty(Check(new JsonObject()));
    }
}
