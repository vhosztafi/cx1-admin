using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed partial class ServicingProposalTests
{
    private static readonly Guid PolicyId = Guid.NewGuid();
    private static JsonObject Snapshot(string product = "motor-trade-combined")
    {
        using var stream = typeof(ServicingProposalTests).Assembly.GetManifestResourceStream($"PolicyExamples.issued-{product}.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    private static ServicingProposalContext Context(DateTimeOffset? now = null, bool senior = false) => new(PolicyId, Base,
        DateTimeOffset.Parse("2026-09-15T08:00:00Z"), DateTimeOffset.Parse("2027-09-15T08:00:00Z"),
        DateTimeOffset.Parse("2026-09-15T08:00:00Z"), now ?? DateTimeOffset.Parse("2026-09-17T10:00:00Z"), senior);
    private static JsonObject Change(string kind, Guid target, string operation, JsonNode? payload = null) => new()
    {
        ["changeId"] = Guid.NewGuid().ToString(), ["riskItemId"] = target.ToString(), ["kind"] = kind, ["operation"] = operation,
        ["payload"] = payload?.DeepClone()
    };
    private static string Draft(params JsonObject[] changes)
    {
        var node = JsonNode.Parse(Proposal())!;
        node["changes"] = new JsonArray(changes.Select(x => { var copy = x.DeepClone().AsObject(); if (copy["payload"] is null) copy.Remove("payload"); return (JsonNode)copy; }).ToArray());
        return node.ToJsonString();
    }

    [Fact]
    public void TypedPatchRetainsIssuedValuesAndStableIdentityWithoutMutatingTheInput()
    {
        var snapshot = Snapshot(); var original = snapshot.ToJsonString(); var driver = snapshot["risk"]!["drivers"]![0]!;
        var id = Guid.Parse(driver["id"]!.GetValue<string>());
        var result = ServicingProposalRules.Assess(original, Draft(Change("driver", id, "update", new JsonObject { ["fullName"] = "Corrected driver name" })), Context());
        Assert.Equal(original, snapshot.ToJsonString());
        Assert.Equal("Corrected driver name", result.Proposed.GetProperty("risk").GetProperty("drivers")[0].GetProperty("fullName").GetString());
        Assert.Equal(driver["dateOfBirth"]!.GetValue<string>(), result.Proposed.GetProperty("risk").GetProperty("drivers")[0].GetProperty("dateOfBirth").GetString());
        Assert.Contains(result.Changes, x => x.ItemId == id && x.Path.EndsWith("/fullName", StringComparison.Ordinal));
    }

    [Fact]
    public void ForeignTargetsDuplicateEffectiveChangesAndNestedIdentityReuseAreRejected()
    {
        var snapshot = Snapshot(); var id = Guid.Parse(snapshot["risk"]!["drivers"]![0]!["id"]!.GetValue<string>());
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", Guid.NewGuid(), "update", new JsonObject())), Context()));
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", id, "update", new JsonObject()), Change("driver", id, "remove")), Context()));
        var payload = new JsonObject { ["occupations"] = new JsonArray(new JsonObject { ["id"] = id.ToString() }) };
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", Guid.NewGuid(), "add", payload)), Context()));
    }

    [Fact]
    public void RemovingReferencedDriverRequiresAnExplicitDependentChange()
    {
        var snapshot = Snapshot(); var id = Guid.Parse(snapshot["risk"]!["drivers"]![0]!["id"]!.GetValue<string>());
        snapshot["risk"]!["vehicles"]![0]!["ownerDriverId"] = id.ToString();
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", id, "remove")), Context()));
        var vehicle = Guid.Parse(snapshot["risk"]!["vehicles"]![0]!["id"]!.GetValue<string>());
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", id, "remove"), Change("vehicle", vehicle, "remove")), Context());
        Assert.Empty(result.Proposed.GetProperty("risk").GetProperty("drivers").EnumerateArray());
    }

    [Fact]
    public void IncompleteAdditionIsSaveableButReportsItsMissingFields()
    {
        var result = ServicingProposalRules.Assess(Snapshot().ToJsonString(), Draft(Change("driver", Guid.NewGuid(), "add", new JsonObject())), Context());
        Assert.Equal(2, result.Proposed.GetProperty("risk").GetProperty("drivers").GetArrayLength());
        Assert.Contains(result.ReadinessIssues, x => x.Path.StartsWith("/risk/drivers/1", StringComparison.Ordinal));
    }

    [Fact]
    public void ReorderingStableRowsIsNotAMaterialChange()
    {
        var snapshot = Snapshot(); var activities = snapshot["risk"]!["business"]!["activities"]!.AsArray();
        var second = activities[0]!.DeepClone(); second["id"] = Guid.NewGuid().ToString(); activities.Add(second);
        var reordered = new JsonArray(activities.Reverse().Select(x => x!.DeepClone()).ToArray());
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("business", PolicyId, "update", new JsonObject { ["activities"] = reordered })), Context());
        Assert.Empty(result.Changes);
    }

    [Theory]
    [InlineData("2027-03-28", "01:30", null, "nonexistent-local-time")]
    [InlineData("2026-10-25", "01:30", null, "ambiguous-local-time")]
    [InlineData("2026-10-25", "01:30", 0, null)]
    [InlineData("2026-10-25", "01:30", 60, null)]
    public void LondonIntentRejectsGapAndRequiresExplicitFoldChoice(string date, string clock, int? offset, string? error)
    {
        var node = JsonNode.Parse(Draft(Change("business", PolicyId, "update", new JsonObject { ["turnover"] = "700000.00" })))!;
        node["commonEffectiveIntent"]!["localDate"] = date; node["commonEffectiveIntent"]!["localTime"] = clock;
        if (offset is not null) node["commonEffectiveIntent"]!["utcOffsetMinutes"] = offset.Value;
        var result = ServicingProposalRules.Assess(Snapshot().ToJsonString(), node.ToJsonString(), Context());
        if (error is null) Assert.DoesNotContain(result.ReadinessIssues, x => x.Path == "/commonEffectiveIntent");
        else Assert.Contains(result.ReadinessIssues, x => x.Code == error);
    }

    [Fact]
    public void BackdatedDriversRemainBlockedEvenForSeniorWhileOtherChangesNeedAuthority()
    {
        var snapshot = Snapshot(); var id = Guid.Parse(snapshot["risk"]!["drivers"]![0]!["id"]!.GetValue<string>());
        var futureClock = DateTimeOffset.Parse("2026-10-02T12:00:00Z");
        var driver = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", id, "update", new JsonObject())), Context(futureClock, true));
        Assert.Contains(driver.ReadinessIssues, x => x.Code == "driver-backdate-forbidden");
        var business = Draft(Change("business", PolicyId, "update", new JsonObject()));
        Assert.Contains(ServicingProposalRules.Assess(snapshot.ToJsonString(), business, Context(futureClock)).ReadinessIssues, x => x.Code == "senior-backdate-authority-required");
        Assert.DoesNotContain(ServicingProposalRules.Assess(snapshot.ToJsonString(), business, Context(futureClock, true)).ReadinessIssues, x => x.Code == "senior-backdate-authority-required");
    }

    [Fact]
    public void LaterCoverSliceRetainsBusinessChangeAndEarlierInvalidCoverRemainsVisible()
    {
        var snapshot = Snapshot(); var section = snapshot["cover"]!["requestedSections"]![1]!.DeepClone();
        var id = Guid.Parse(section["id"]!.GetValue<string>());
        var early = Change("cover", id, "remove");
        section["limit"] = "125000.00";
        var later = Change("cover", id, "add", new JsonObject { ["requestedSections"] = new JsonArray(section.DeepClone()) });
        later["effectiveIntent"] = new JsonObject { ["localDate"] = "2026-10-03", ["localTime"] = "00:00", ["timeZone"] = "Europe/London" };
        var proposal = JsonNode.Parse(Draft(Change("business", PolicyId, "update", new JsonObject { ["turnover"] = "700000.00" }), early, later))!;
        proposal["dateBasis"] = "per-cover-change";
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), proposal.ToJsonString(), Context());
        Assert.Equal(2, result.Slices.Count);
        Assert.All(result.Slices, slice => Assert.Equal("700000.00", slice.Proposed.GetProperty("risk").GetProperty("business").GetProperty("turnover").GetString()));
        Assert.Contains(result.ReadinessIssues, x => x.Code == "section-selection-required");
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void TypedIncompleteVehicleAndActivityTotalsProducePreciseReadiness(string product)
    {
        var snapshot = Snapshot(product); var activities = snapshot["risk"]!["business"]!["activities"]!.DeepClone();
        activities[0]!["turnoverBasisPoints"] = 9000;
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(
            Change("vehicle", Guid.NewGuid(), "add", new JsonObject()),
            Change("business", PolicyId, "update", new JsonObject { ["activities"] = activities })), Context());
        Assert.Contains(result.ReadinessIssues, x => x.Code == "activity-total-must-equal-100-percent");
        Assert.Contains(result.ReadinessIssues, x => x.Path == "/risk/vehicles/1/registration");
    }

    [Fact]
    public void ExclusiveEndAndLatestFutureIssuedSliceBlockProgression()
    {
        var proposal = JsonNode.Parse(Draft(Change("business", PolicyId, "update", new JsonObject())))!;
        var snapshot = Snapshot().ToJsonString();
        var result = ServicingProposalRules.Assess(snapshot, proposal.ToJsonString(), Context() with { LatestIssuedEffectiveAt = DateTimeOffset.Parse("2026-11-01T00:00:00Z") });
        Assert.Contains(result.ReadinessIssues, x => x.Code == "effective-before-latest-issued-slice"); Assert.Empty(result.Slices);
        proposal["commonEffectiveIntent"]!["localDate"] = "2027-09-15";
        proposal["commonEffectiveIntent"]!["localTime"] = "09:00";
        result = ServicingProposalRules.Assess(snapshot, proposal.ToJsonString(), Context());
        Assert.Contains(result.ReadinessIssues, x => x.Code == "effective-outside-term"); Assert.Empty(result.Slices);
    }

    [Fact]
    public void CoverOverridesNeedPerCoverModeAndCannotPrecedeCommonDate()
    {
        var change = Change("cover", PolicyId, "update", new JsonObject());
        change["effectiveIntent"] = new JsonObject { ["localDate"] = "2026-09-30", ["localTime"] = "00:00", ["timeZone"] = "Europe/London" };
        var proposal = JsonNode.Parse(Draft(change))!;
        var result = ServicingProposalRules.Assess(Snapshot().ToJsonString(), proposal.ToJsonString(), Context());
        Assert.Contains(result.ReadinessIssues, x => x.Code == "shared-date-override-forbidden");
        Assert.Contains(result.ReadinessIssues, x => x.Code == "cover-date-before-common"); Assert.Empty(result.Slices);
        proposal["dateBasis"] = "per-cover-change";
        proposal["changes"]![0]!["effectiveIntent"]!["localDate"] = "2026-10-02";
        result = ServicingProposalRules.Assess(Snapshot().ToJsonString(), proposal.ToJsonString(), Context());
        Assert.Single(result.Slices);
    }

    [Fact]
    public void PolicyholderCorrectionPreservesOwnerAndSystemFieldsCannotEnterCapture()
    {
        var snapshot = Snapshot(); var client = Guid.Parse(snapshot["insured"]!["clientId"]!.GetValue<string>());
        var correction = Change("policyholder", client, "update", new JsonObject { ["legalName"] = "Fictional corrected company" });
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(correction), Context());
        Assert.Equal("Fictional corrected company", result.Proposed.GetProperty("insured").GetProperty("legalName").GetString());
        correction["riskItemId"] = Guid.NewGuid().ToString();
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(correction), Context()));
        correction["riskItemId"] = client.ToString(); correction["payload"]!["clientId"] = Guid.NewGuid().ToString();
        Assert.Throws<QuoteInputException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(correction), Context()));
        Assert.False(result.Proposed.TryGetProperty("premium", out _));
        Assert.False(result.Proposed.GetProperty("cover").TryGetProperty("sections", out _));
    }

    [Fact]
    public void UnsupportedRoadRisksSectionAndZeroLimitAreRejected()
    {
        var road = Snapshot("motor-trade-road-risks"); var section = Snapshot()["cover"]!["requestedSections"]![1]!.DeepClone();
        var id = Guid.Parse(section["id"]!.GetValue<string>());
        var change = Change("cover", id, "add", new JsonObject { ["requestedSections"] = new JsonArray(section.DeepClone()) });
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(road.ToJsonString(), Draft(change), Context()));
        change["payload"]!["requestedSections"]![0]!["limit"] = "0.00";
        Assert.Throws<QuoteInputException>(() => ServicingProposalRules.Assess(road.ToJsonString(), Draft(change), Context()));
    }

    [Fact]
    public void ExplicitReplacementClearsOptionalFieldsWithoutChangingTargetIdentity()
    {
        var snapshot = Snapshot(); var driver = snapshot["risk"]!["drivers"]![0]!;
        var id = Guid.Parse(driver["id"]!.GetValue<string>());
        var change = Change("driver", id, "update", new JsonObject { ["fullName"] = "Replacement driver details" });
        change["payloadMode"] = "replace";
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(change), Context());
        var projected = result.Proposed.GetProperty("risk").GetProperty("drivers")[0];
        Assert.Equal(id, projected.GetProperty("id").GetGuid());
        Assert.False(projected.TryGetProperty("dateOfBirth", out _));
        Assert.Contains(result.ReadinessIssues, x => x.Path == "/risk/drivers/0/dateOfBirth");
        Assert.Contains(result.Changes, x => x.Kind == "removed" && x.Path.EndsWith("/dateOfBirth", StringComparison.Ordinal));
        Assert.NotNull(driver["dateOfBirth"]);
    }

    [Fact]
    public void DriverRemovalCanExplicitlyClearDependentVehicleOwnerByReplacement()
    {
        var snapshot = Snapshot(); var driver = snapshot["risk"]!["drivers"]![0]!;
        var driverId = Guid.Parse(driver["id"]!.GetValue<string>());
        var vehicle = snapshot["risk"]!["vehicles"]![0]!;
        vehicle["ownerDriverId"] = driverId.ToString();
        var vehicleId = Guid.Parse(vehicle["id"]!.GetValue<string>());
        var payload = vehicle.DeepClone().AsObject(); payload.Remove("id"); payload.Remove("ownerDriverId");
        var update = Change("vehicle", vehicleId, "update", payload); update["payloadMode"] = "replace";
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(Change("driver", driverId, "remove"), update), Context());
        Assert.Empty(result.Proposed.GetProperty("risk").GetProperty("drivers").EnumerateArray());
        Assert.False(result.Proposed.GetProperty("risk").GetProperty("vehicles")[0].TryGetProperty("ownerDriverId", out _));
        Assert.Equal(vehicleId, result.Proposed.GetProperty("risk").GetProperty("vehicles")[0].GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public void ReplacementModeCannotBeAppliedToAdditionOrRemoval(string operation)
    {
        var change = Change("driver", Guid.NewGuid(), operation, operation == "add" ? new JsonObject() : null);
        change["payloadMode"] = "replace";
        Assert.Throws<QuoteInputException>(() => ServicingProposalInput.Parse(Draft(change), Base));
    }

    [Fact]
    public void ExplicitCoverReplacementCanDeselectWithoutRetainingInactiveAmounts()
    {
        var snapshot = Snapshot(); var section = snapshot["cover"]!["requestedSections"]![1]!;
        var id = Guid.Parse(section["id"]!.GetValue<string>());
        var payload = new JsonObject { ["requestedSections"] = new JsonArray(new JsonObject {
            ["id"] = id.ToString(), ["code"] = "stock-custody", ["selected"] = false }) };
        var change = Change("cover", id, "update", payload);
        Assert.Throws<QuoteValidationException>(() => ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(change), Context()));
        change["payloadMode"] = "replace";
        var result = ServicingProposalRules.Assess(snapshot.ToJsonString(), Draft(change), Context());
        var projected = result.Proposed.GetProperty("cover").GetProperty("requestedSections").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.False(projected.GetProperty("selected").GetBoolean()); Assert.False(projected.TryGetProperty("limit", out _));
        Assert.NotNull(section["limit"]);
    }
}
