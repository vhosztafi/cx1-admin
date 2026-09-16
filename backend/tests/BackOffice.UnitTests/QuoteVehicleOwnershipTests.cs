using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteVehicleOwnershipTests
{
    private static JsonNode Reference(string collection,int value)
    {
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var label = JsonNode.Parse(stream)!["collections"]![collection]!.AsArray().First(row => row!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone();
        return new JsonObject { ["collection"] = collection, ["version"] = QuoteCatalogueIdentity.Version, ["value"] = value, ["label"] = label };
    }
    private static JsonNode Proposal(bool specified)
    {
        var driverId = Guid.NewGuid().ToString(); var vehicleId = Guid.NewGuid().ToString();
        return new JsonObject { ["insured"] = new JsonObject { ["declaredCompanyType"] = Reference("companyTypes",1) },
            ["risk"] = new JsonObject { ["specifiedVehicleIds"] = specified ? new JsonArray(JsonValue.Create(vehicleId)) : new JsonArray(),
                ["drivers"] = new JsonArray(new JsonObject { ["id"] = driverId, ["relationship"] = Reference("driverRelationshipsPolicyHolder",2), ["responses"] = new JsonObject { ["answers"] = new JsonArray() } }),
                ["vehicles"] = new JsonArray(new JsonObject { ["id"] = vehicleId, ["declaredOwnerType"] = Reference("vehicleOwnerTypes",4), ["ownerDriverId"] = driverId }) } };
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode p)
    { using var document = JsonDocument.Parse(p.ToJsonString()); return QuoteVehicleOwnership.Assess(document.RootElement); }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersonalVehicleOwnerNeedsExplicitCoverForBothVehicleBranches(bool specified)
    {
        var p = Proposal(specified); var answers = p["risk"]!["drivers"]![0]!["responses"]!["answers"]!.AsArray();
        Assert.Contains(Check(p),i => i.Code == "vehicle-owner-cover-context-required");
        answers.Add(new JsonObject { ["questionId"] = "MTS-06-Q31", ["value"] = false }); Assert.Contains(Check(p),i => i.Code == "vehicle-owner-personal-cover-required");
        answers[0]!["value"] = true; var before = p.ToJsonString(); Assert.Empty(Check(p)); Assert.Equal(before,p.ToJsonString());
    }
    [Fact]
    public void PolicyholderOwnershipDistinguishesOrdinaryAndSpecifiedVehicles()
    {
        var p = Proposal(false); p["risk"]!["drivers"]![0]!["relationship"] = Reference("driverRelationshipsPolicyHolder",3);
        Assert.Contains(Check(p),i => i.Code == "policyholder-driver-owner-not-selectable");
        p["risk"]!["specifiedVehicleIds"]!.AsArray().Add(p["risk"]!["vehicles"]![0]!["id"]!.DeepClone());
        Assert.DoesNotContain(Check(p),i => i.Code == "policyholder-driver-owner-not-selectable");
    }
    [Fact]
    public void BusinessOwnerDoesNotSilentlyClearARetainedDriverAndForgedContextCannotGrantEligibility()
    {
        var p = Proposal(false); p["risk"]!["vehicles"]![0]!["declaredOwnerType"] = Reference("vehicleOwnerTypes",1);
        Assert.Contains(Check(p),i => i.Code == "inactive-owner-driver-retained");
        p["insured"]!["declaredCompanyType"]!["label"] = "Forged"; Assert.Contains(Check(p),i => i.Code == "vehicle-owner-context-required");
    }
    [Fact]
    public void MissingOrUnresolvableNamedOwnerIsDiagnosedWithoutGuessingAnotherDriver()
    {
        var p = Proposal(false); p["risk"]!["vehicles"]![0]!.AsObject().Remove("ownerDriverId");
        Assert.Contains(Check(p),i => i.Code == "vehicle-owner-driver-required");
        p["risk"]!["vehicles"]![0]!["ownerDriverId"] = Guid.NewGuid().ToString();
        Assert.Contains(Check(p),i => i.Code == "vehicle-owner-driver-not-in-proposal");
    }
}
