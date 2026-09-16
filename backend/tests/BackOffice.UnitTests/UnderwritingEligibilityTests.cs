using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class UnderwritingEligibilityTests
{
    [Fact]
    public void AnyDriverLicenceExperienceIsUnknownAndCannotBeInferredFromTheAgeRange()
    {
        var config = QuoteRatingRulesTests.Definition("authority");
        var risk = Risk() with { AnyDriverCount = 3, AnyDriverMinimumAge = 30, AnyDriverMaximumAge = 65 };
        Assert.Contains(UnderwritingRules.AssessAuthority(config, risk), x => x.RuleCode == "any-driver-licence-years");
        var tooYoung = risk with { AnyDriverMinimumAge = 16 };
        var requirements = UnderwritingRules.AssessAuthority(config, tooYoung);
        Assert.Contains(requirements, x => x.RuleCode == "any-driver-age");
        Assert.Contains(requirements, x => x.RuleCode == "any-driver-licence-years");
        Assert.Throws<ArgumentException>(() => UnderwritingRules.AssessAuthority(config, risk with { AnyDriverMaximumAge = null }));
    }
    private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static UnderwritingRisk Risk() => new(600m, 0m, 30000m, 10, false, false, [1],
        [new(Guid.Parse("11111111-1111-4111-8111-111111111111"), 35, 10, false, false)],
        new Dictionary<string, decimal> { ["road-risks"] = 30000m });

    [Fact]
    public void EveryAuthorityDimensionIsMandatoryAndUnknownFieldsOrDuplicateKeysFailClosed()
    {
        var baseline = QuoteRatingRulesTests.Definition("authority");
        Assert.True(UnderwritingConfiguration.Valid(baseline, "authority"));
        foreach (var property in baseline.GetProperty("limits").EnumerateObject())
        {
            var changed = JsonNode.Parse(baseline.GetRawText())!; changed["limits"]!.AsObject().Remove(property.Name);
            Assert.False(UnderwritingConfiguration.Valid(Json(changed), "authority"));
            Assert.Throws<ArgumentException>(() => UnderwritingRules.AssessAuthority(Json(changed), Risk()));
        }
        var extra = JsonNode.Parse(baseline.GetRawText())!; extra["limits"]!["overrideEverything"] = true;
        Assert.False(UnderwritingConfiguration.Valid(Json(extra), "authority"));
        using var duplicate = JsonDocument.Parse(baseline.GetRawText().Replace("\"schemaVersion\": \"1\"", "\"schemaVersion\": \"1\", \"schemaVersion\": \"1\""));
        Assert.False(UnderwritingConfiguration.Valid(duplicate.RootElement, "authority"));
    }

    [Fact]
    public void EqualityBoundariesPassAndEachExceededDimensionRemainsIndependent()
    {
        var node = JsonNode.Parse(QuoteRatingRulesTests.Definition("authority").GetRawText())!;
        var limits = node["limits"]!;
        limits["annualPremiumLimit"] = "600.00"; limits["vehicleLimit"] = "30000.00";
        limits["minimumDriverAge"] = 35; limits["maximumDriverAge"] = 35; limits["minimumLicenceYears"] = 10;
        limits["allowedTradeValues"] = new JsonArray(1);
        limits["coverLimits"]!["road-risks"] = "30000.00";
        var config = Json(node);
        Assert.Empty(UnderwritingRules.AssessAuthority(config, Risk()));
        var outside = Risk() with { AnnualPremium = 600.01m, VehicleLimit = 30000.01m,
            Drivers = [new(Risk().Drivers[0].Id, 34, 9, false, false)], TradeValues = [2],
            CoverLimits = new Dictionary<string, decimal> { ["road-risks"] = 30000.01m } };
        var issues = UnderwritingRules.AssessAuthority(config, outside);
        foreach (var dimension in new[] { "premium-limit", "vehicle-limit", "driver-age", "licence-years", "trade-restriction", "cover-restriction" })
            Assert.Contains(issues, x => x.Dimension == dimension);
        Assert.Equal(6, issues.Count);
    }

    [Fact]
    public void ClaimsConvictionsTradingAndSourceValetingProduceDistinctReferrals()
    {
        var node = JsonNode.Parse(QuoteRatingRulesTests.Definition("authority").GetRawText())!;
        node["limits"]!["reviewConvictions"] = false; node["limits"]!["reviewClaims"] = false;
        node["limits"]!["reviewTradingHistory"] = false;
        var risk = Risk() with { TradingYears = 4, HasClaims = true, HasValeting = true, VehicleLimit = 50000.01m,
            Drivers = [new(Risk().Drivers[0].Id, 35, 10, true, true)] };
        var issues = UnderwritingRules.AssessAuthority(Json(node), risk);
        Assert.Contains(issues, x => x.Dimension == "conviction-history");
        Assert.Contains(issues, x => x.Dimension == "claims-history" && x.TargetId == risk.Drivers[0].Id);
        Assert.Contains(issues, x => x.RuleCode == "UW-22");
        Assert.Contains(UnderwritingRules.SourceReferrals(risk, 5), x => x.RuleCode == "UW-09");
        Assert.DoesNotContain(UnderwritingRules.SourceReferrals(risk with { TradingYears = 5, VehicleLimit = 50000m }, 5), x => x.RuleCode is "UW-09" or "UW-22");
    }

    [Fact]
    public void GrantCannotExpandAnyBinderDimensionAndSalvageIsNeverAnImplicitGrant()
    {
        var binder = QuoteRatingRulesTests.Definition("binder"); var authority = QuoteRatingRulesTests.Definition("authority");
        Assert.True(UnderwritingConfiguration.WithinBinder(authority, binder));
        foreach (var amount in new[] { "annualPremiumLimit", "stockLimit", "vehicleLimit" })
        {
            var changed = JsonNode.Parse(authority.GetRawText())!; changed["limits"]![amount] = "9999999.00";
            Assert.False(UnderwritingConfiguration.WithinBinder(Json(changed), binder));
        }
        var younger = JsonNode.Parse(authority.GetRawText())!; younger["limits"]!["minimumDriverAge"] = 24;
        Assert.False(UnderwritingConfiguration.WithinBinder(Json(younger), binder));
        Assert.Contains(UnderwritingRules.AssessAuthority(authority, Risk() with { HasSalvageOrBreaking = true }), x => x.RuleCode == "salvage-breaking");
    }

    [Fact]
    public void CurrentnessIsHalfOpenAndBinderMustCoverTheEntireTerm()
    {
        var config = QuoteRatingRulesTests.Definition("binder"); var term = QuoteRatingRulesTests.Term();
        Assert.True(UnderwritingConfiguration.Current(config, "binder", "motor-trade-road-risks", term.StartsAt, term));
        Assert.False(UnderwritingConfiguration.Current(config, "binder", "motor-trade-road-risks", DateTimeOffset.Parse("2030-01-01T00:00:00Z"), term));
        Assert.False(UnderwritingConfiguration.Current(config, "binder", "motor-trade-road-risks", term.StartsAt,
            QuoteRatingRulesTests.Term("2029-06-01T00:00:00Z", "2030-06-01T00:00:00Z")));
        Assert.False(UnderwritingConfiguration.Current(config, "binder", "motor-trade-combined", term.StartsAt, term));
    }

    [Fact]
    public void RefreshRequiresCurrentSameProductDraftAndProofDoesNotDeadlockPricing()
    {
        var product = Guid.NewGuid(); var revision = Guid.NewGuid(); var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var context = new UnderwritingRefreshContext(product, product, revision, revision, Guid.NewGuid(), Guid.NewGuid(),
            "draft", false, "published", now, now.AddYears(2), now, QuoteRatingRulesTests.Term());
        Assert.Null(UnderwritingLifecycleRules.RefreshBlocker(context));
        Assert.Equal("quote-revision-stale", UnderwritingLifecycleRules.RefreshBlocker(context with { ExpectedRevisionId = Guid.NewGuid() }));
        Assert.Equal("refresh-product-mismatch", UnderwritingLifecycleRules.RefreshBlocker(context with { TargetProductId = Guid.NewGuid() }));
        Assert.Equal("quote-capture-closed", UnderwritingLifecycleRules.RefreshBlocker(context with { QuoteState = "bound" }));
        Assert.Equal("refresh-product-unavailable", UnderwritingLifecycleRules.RefreshBlocker(context with { TargetState = "draft" }));
        Assert.Equal("refresh-product-unavailable", UnderwritingLifecycleRules.RefreshBlocker(context with { TargetEffectiveTo = now.AddMonths(6) }));
        Assert.False(UnderwritingLifecycleRules.ProofBlocks("signed-statement", "rate"));
        Assert.False(UnderwritingLifecycleRules.ProofBlocks("signed-statement", "prepare-terms"));
        Assert.True(UnderwritingLifecycleRules.ProofBlocks("signed-statement", "send"));
        Assert.True(UnderwritingLifecycleRules.ProofBlocks("driver-proof", "prepare-terms"));
        Assert.False(UnderwritingLifecycleRules.CanReturnToDraft("bound"));
        Assert.True(UnderwritingLifecycleRules.CanReturnToDraft("accepted"));
    }

    [Fact]
    public void SectionsRequireExplicitChoicesAndCurrentUniqueTargetsWithoutChangingOldCaptureReadiness()
    {
        var id = Guid.NewGuid(); var premises = Guid.NewGuid();
        using var omitted = JsonDocument.Parse("{}");
        Assert.Contains(UnderwritingRules.ValidateSections(omitted.RootElement, "motor-trade-road-risks", []), x => x.Code == "requested-sections-required");
        var selections = JsonNode.Parse($$"""{"requestedSections":[{"id":"{{id}}","code":"premises","selected":true,"limit":"250000.00","excess":"500.00","premisesIds":["{{premises}}"]},{"id":"{{Guid.NewGuid()}}","code":"stock-custody","selected":false},{"id":"{{Guid.NewGuid()}}","code":"tools-equipment","selected":false}]}""")!;
        Assert.Empty(UnderwritingRules.ValidateSections(Json(selections), "motor-trade-combined", [premises]));
        Assert.Contains(UnderwritingRules.ValidateSections(Json(selections), "motor-trade-combined", []), x => x.Code == "foreign-section-premises");
        Assert.Contains(UnderwritingRules.ValidateSections(Json(selections), "motor-trade-road-risks", [premises]), x => x.Code == "section-not-supported");
        selections["requestedSections"]!.AsArray().Add(selections["requestedSections"]![0]!.DeepClone());
        Assert.Contains(UnderwritingRules.ValidateSections(Json(selections), "motor-trade-combined", [premises]), x => x.Code == "duplicate-requested-section");
    }
}
