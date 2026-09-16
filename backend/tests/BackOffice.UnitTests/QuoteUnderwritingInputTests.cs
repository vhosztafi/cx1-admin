using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteUnderwritingInputTests
{
    [Fact]
    public void RatingReadinessCanDeferProofButStillRejectsIncompleteCapturedRisk()
    {
        var proposal = Proposal(); var root = Json(proposal); var term = QuoteTerm.Assess(root.GetProperty("termIntent"));
        var modes = root.GetProperty("risk").GetProperty("vehicles").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid(), _ => "manual");
        var capture = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), root, term, null, new(2026, 9, 16), modes);
        Assert.False(capture.Ready); Assert.All(capture.Issues, x => Assert.Equal("evidence", x.Category));
        Assert.True(QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), root, term, null, new(2026, 9, 16), modes, includeEvidence: false).Ready);
        proposal["risk"]!["drivers"]![0]!.AsObject().Remove("dateOfBirth");
        var invalid = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), Json(proposal), term, null, new(2026, 9, 16), modes, includeEvidence: false);
        Assert.False(invalid.Ready); Assert.Contains(invalid.Issues, x => x.Category == "capture");
    }
    internal static JsonNode Proposal(string product = "motor-trade-road-risks")
    {
        using var stream = typeof(QuoteUnderwritingInputTests).Assembly.GetManifestResourceStream("UnderwritingExamples.Demo")!;
        return JsonNode.Parse(stream)!["proposals"]![product]!.DeepClone();
    }
    private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static JsonElement Reference(string collection, int value)
    {
        using var stream = typeof(QuoteCaptureShape).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        using var doc = JsonDocument.Parse(stream);
        var row = doc.RootElement.GetProperty("collections").GetProperty(collection).EnumerateArray().Single(x => x.GetProperty("value").GetInt32() == value);
        return JsonSerializer.SerializeToElement(new { collection, value, label = row.GetProperty("text").GetString(), version = QuoteCatalogueIdentity.Version });
    }
    [Theory]
    [InlineData("motor-trade-road-risks",600)]
    [InlineData("motor-trade-combined",1200)]
    public void ActualCapturedSourceFieldsProduceWorkedPricesAndSafePricingProjection(string product, decimal premium)
    {
        var proposal = Proposal(product); var config = QuoteRatingRulesTests.Definition("rating", product);
        var projected = QuoteUnderwritingInput.Project(Json(proposal), config);
        var rating = QuoteRatingRules.Calculate(config, projected.Rating, projected.Term, 1000);
        Assert.Equal(premium, rating.AnnualPremium);
        Assert.Equal(1, projected.Rating.DriverCount); Assert.Equal(1, projected.Rating.VehicleCount);
        Assert.DoesNotContain("Alex", projected.Pricing.GetRawText());
        Assert.DoesNotContain("licenceNumber", projected.Pricing.GetRawText());
        Assert.DoesNotContain("email", projected.Pricing.GetRawText());
        Assert.Equal(6, projected.RiskForPremium(premium).TradingYears);
    }
    [Fact]
    public void OptionalTestDateDoesNotReplaceSourceLicenceHeldDateAndNamesAreNotPricingFacts()
    {
        var proposal = Proposal(); var config = QuoteRatingRulesTests.Definition("rating");
        var before = QuoteUnderwritingInput.Project(Json(proposal), config);
        proposal["risk"]!["drivers"]![0]!["licence"]!["testDate"] = "2026-01-01";
        proposal["risk"]!["drivers"]![0]!["fullName"] = "Changed presentation name";
        var after = QuoteUnderwritingInput.Project(Json(proposal), config);
        Assert.Equal(before.Drivers[0].LicenceYears, after.Drivers[0].LicenceYears);
        Assert.True(JsonElement.DeepEquals(before.Pricing, after.Pricing));
        proposal["risk"]!["drivers"]![0]!["licence"]!.AsObject().Remove("issuedOn");
        Assert.Throws<QuoteValidationException>(() => QuoteUnderwritingInput.Project(Json(proposal), config));
    }
    [Fact]
    public void MissingSelectionsOrUntrustedReferenceNeverBecomeZeroRisk()
    {
        var proposal = Proposal("motor-trade-combined"); var config = QuoteRatingRulesTests.Definition("rating", "motor-trade-combined");
        proposal["cover"]!.AsObject().Remove("requestedSections");
        Assert.Contains(Assert.Throws<QuoteValidationException>(() => QuoteUnderwritingInput.Project(Json(proposal), config)).Issues, x => x.Code == "requested-sections-required");
        proposal = Proposal(); proposal["risk"]!["business"]!["activities"]![0]!["code"]!["value"] = 99999;
        Assert.Throws<QuoteValidationException>(() => QuoteUnderwritingInput.Project(Json(proposal), QuoteRatingRulesTests.Definition("rating")));
    }
    [Fact]
    public void MixedDriverBasisAddsDeclaredCountAndUsesPinnedAgesRatherThanOptionIds()
    {
        var proposal = Proposal(); var answers = proposal["risk"]!["responses"]!["answers"]!.AsArray();
        var plan = answers.Single(x => x!["questionId"]!.GetValue<string>() == "MTS-06-Q01")!;
        plan["value"]!["value"] = 2; plan["value"]!["label"] = "Named Driver and Any Authorised Driver";
        // Resolve exact labels from trusted embedded catalogue in production;
        // test fixtures also use that source rather than guessed display wording.
        var reference = Reference("driverPlans", 2);
        plan["value"] = JsonNode.Parse(reference.GetRawText());
        answers.Add(new JsonObject { ["questionId"] = "MTS-06-Q02", ["kind"] = "count", ["value"] = 2 });
        foreach (var (question, collection, value) in new[] { ("MTS-06-Q03","aadDriverMinAge",2), ("MTS-06-Q04","aadDriverMaxAge",2),
            ("MTS-06-Q05","aadMaxVehicleGrouping",1), ("MTS-06-Q06","aadMaxVehicleGvw",1), ("MTS-06-Q07","aadMaxMotorcycleCc",1) })
            answers.Add(new JsonObject { ["questionId"] = question, ["kind"] = "reference", ["value"] = JsonNode.Parse(Reference(collection,value).GetRawText()) });
        var result = QuoteUnderwritingInput.Project(Json(proposal), QuoteRatingRulesTests.Definition("rating"));
        Assert.Equal(3, result.Rating.DriverCount); Assert.Equal(23, result.Rating.YoungestDriverAge);
        Assert.Equal(23, result.AnyDriverMinimumAge); Assert.Equal(70, result.AnyDriverMaximumAge);
        Assert.Single(result.Drivers); // No fabricated driver identities/licence experience.
    }
}
