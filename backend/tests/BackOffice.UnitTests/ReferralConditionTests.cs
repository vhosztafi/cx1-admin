using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Underwriting;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ReferralConditionTests
{
    private static readonly Guid Driver = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Premises = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement Proposal => Json(new { risk = new { drivers = new[] { new { id = Driver, firstName = "Alex", surname = "Demo" } },
        premises = new[] { new { id = Premises } }, vehicles = Array.Empty<object>() } });
    private static UnderwritingRisk Risk => new(600m, 0m, 30000m, 10, false, false, [1],
        [new(Driver, 35, 10, false, false)], new Dictionary<string, decimal> { ["road-risks"] = 30000m });

    [Fact]
    public void SecurityWarrantyRetainsSourceCodeAndTargetsWhileRiskChangesCannotResolveWithProof()
    {
        var warranty = ReferralRules.Condition(Json(new { code = "overnight-security", premisesId = Premises, wordingVersion = "1" }), Proposal);
        Assert.Equal("warranty", warranty.Kind); Assert.Equal("W-07", warranty.EndorsementCode);
        Assert.Equal("Vehicles kept at the declared secured premises overnight.", warranty.Wording);
        Assert.Equal(new[] { Premises }, warranty.TargetIds);
        var change = ReferralRules.Condition(Json(new { code = "revise-stock-limit", maximumAmount = "100000.00" }), Proposal);
        Assert.Equal("risk-change", change.Kind); Assert.False(ReferralRules.CanResolveWithEvidence(change));
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Json(new { code = "overnight-security", premisesId = Guid.NewGuid(), wordingVersion = "1" }), Proposal));
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Json(new { code = "overnight-security", premisesId = Premises, wordingVersion = "1", overrideAll = true }), Proposal));
    }

    [Fact]
    public void DocumentaryConditionsRejectForeignTargetsUnknownPurposesAndArbitraryWording()
    {
        var proof = ReferralRules.Condition(Json(new { code = "provide-driver-proof", driverId = Driver, requirementCode = "driving-record" }), Proposal);
        Assert.Equal("documentary", proof.Kind); Assert.Equal("driving-record", proof.RequirementCode);
        Assert.True(ReferralRules.CanResolveWithEvidence(proof));
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Json(new { code = "provide-driver-proof", driverId = Driver, requirementCode = "stock-approved" }), Proposal));
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Json(new { code = "named-drivers-only", driverIds = new[] { Driver, Driver }, wordingVersion = "1" }), Proposal));
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Json(new { code = "arbitrary", wording = "Everything approved" }), Proposal));
        Assert.Equal("trading-history", ReferralRules.Condition(Json(new { code = "provide-trading-history" }), Proposal).RequirementCode);
        Assert.Equal("Driving is restricted to the following named drivers: Alex Demo.", ReferralRules.Condition(Json(new { code = "named-drivers-only", driverIds = new[] { Driver }, wordingVersion = "1" }), Proposal).Wording);
    }

    [Fact]
    public void AnyDriverWarrantyRequiresBothMinimumsAndNeverWaivesOtherDimensions()
    {
        var actor = QuoteRatingRulesTests.Definition("authority"); var binder = QuoteRatingRulesTests.Definition("binder");
        var risk = Risk with { AnyDriverCount = 2, AnyDriverMinimumAge = 30, AnyDriverMaximumAge = 65 };
        var warranty = ReferralRules.Condition(Json(new { code = "any-driver-minimum-licence", minimumYears = 2, wordingVersion = "1" }), Proposal);
        Assert.Contains("at least 2 complete years", warranty.Wording);
        Assert.Contains(ReferralRules.AuthorityBlockers(actor, binder, risk, []), x => x.RuleCode == "any-driver-licence-years");
        Assert.DoesNotContain(ReferralRules.AuthorityBlockers(actor, binder, risk, [warranty]), x => x.RuleCode == "any-driver-licence-years");
        Assert.Contains(ReferralRules.AuthorityBlockers(actor, binder, risk with { StockLimit = 150000m }, [warranty]), x => x.Dimension == "stock-limit");
        Assert.Contains(ReferralRules.AuthorityBlockers(actor, binder, risk with { AnyDriverMinimumAge = 16 }, [warranty]), x => x.Dimension == "driver-age");
        var high = JsonNode.Parse(actor.GetRawText())!; high["limits"]!["minimumLicenceYears"] = 3;
        Assert.Contains(ReferralRules.AuthorityBlockers(Json(high), binder, risk, [warranty]), x => x.RuleCode == "any-driver-licence-years");
    }

    [Fact]
    public void EvidenceSatisfactionRequiresIndependentCurrentReviewAndExactFingerprint()
    {
        var fingerprint = new string('a', 64);
        Assert.False(ReferralRules.EvidenceSatisfied("accepted", "unreviewed", false, fingerprint, fingerprint));
        Assert.False(ReferralRules.EvidenceSatisfied("accepted", "accepted", true, fingerprint, fingerprint));
        Assert.False(ReferralRules.EvidenceSatisfied("accepted", "accepted", false, fingerprint, new string('b', 64)));
        Assert.False(ReferralRules.EvidenceSatisfied("pending", "accepted", false, fingerprint, fingerprint));
        Assert.True(ReferralRules.EvidenceSatisfied("accepted", "accepted", false, fingerprint, fingerprint));
    }

    [Fact]
    public void AppliedWarrantyChangesContractualHashWithoutInventingRiskFacts()
    {
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", "questions-1", "references-1");
        var cycle = Guid.NewGuid(); var rating = Guid.NewGuid(); var decision = Guid.NewGuid();
        var warranty = ReferralRules.Condition(Json(new { code = "any-driver-minimum-licence", minimumYears = 2, wordingVersion = "1" }), Proposal);
        var before = UnderwritingHashes.Terms(cycle, rating, pins, Json(new { premium = "600.00", endorsements = Array.Empty<object>() }));
        var contractual = Json(new { premium = "600.00", endorsements = new[] { new { code = warranty.EndorsementCode, version = "1", wording = warranty.Wording, decisionId = decision, targetIds = warranty.TargetIds } } });
        Assert.NotEqual(before, UnderwritingHashes.Terms(cycle, rating, pins, contractual));
        Assert.Empty(warranty.TargetIds); // An unnamed driver's identity/date is never fabricated.
        Assert.Single(Proposal.GetProperty("risk").GetProperty("drivers").EnumerateArray());
    }
}
