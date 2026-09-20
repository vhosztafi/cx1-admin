using System.Text.Json;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialCapacityTests
{
    private static readonly Guid Quote = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111"), Cycle = Guid.Parse("aaaaaaaa-2222-4222-8222-222222222222"),
        Submission = Guid.Parse("aaaaaaaa-3333-4333-8333-333333333333"), Location = Guid.Parse("aaaaaaaa-4444-4444-8444-444444444444");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-15T12:00:00Z");
    private static CommercialCapacityContext Context() => new(Quote, Cycle, Submission, new string('a',64), Now.AddDays(1), Now.AddYears(1), "single-location", 2500000.01m, Location);
    private static CommercialCapacityDecision Decision() => new(Quote, Cycle, Submission, new string('a',64), "approve-with-conditions", Now.AddDays(-1), Now.AddYears(2), [new("single-location", 3000000m, Location)]);

    [Fact]
    public void ExactLocationExtentDoesNotImplyAnotherSubjectOrDimension()
    {
        Assert.True(CommercialCapacityRules.Applies(Decision(), Context(), Now));
        Assert.False(CommercialCapacityRules.Applies(Decision(), Context() with { RiskItemId = Guid.NewGuid() }, Now));
        Assert.False(CommercialCapacityRules.Applies(Decision(), Context() with { RiskItemId = null }, Now));
        Assert.False(CommercialCapacityRules.Applies(Decision(), Context() with { Dimension = "maximum-estimated-loss" }, Now));
        Assert.False(CommercialCapacityRules.Applies(Decision(), Context() with { RequestedAmount = 3000000.01m }, Now));
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("submission")]
    [InlineData("hash")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("term")]
    [InlineData("query")]
    public void StaleOrUnrelatedCarrierPermissionDoesNotApply(string change)
    {
        var decision = change switch {
            "cycle" => Decision() with { CycleId = Guid.NewGuid() },
            "submission" => Decision() with { SubmissionId = Guid.NewGuid() },
            "hash" => Decision() with { SubmissionHash = new string('b',64) },
            "expired" => Decision() with { ValidTo = Now },
            "future" => Decision() with { ValidFrom = Now.AddSeconds(1) },
            "term" => Decision() with { ValidTo = Context().EndsAt.AddSeconds(-1) },
            _ => Decision() with { Outcome = "query" }
        };
        Assert.False(CommercialCapacityRules.Applies(decision, Context(), Now));
    }

    [Theory]
    [InlineData("{\"dimension\":\"district-property\",\"maximumAmount\":\"50000000.00\"}")]
    [InlineData("{\"dimension\":\"single-location\",\"maximumAmount\":\"3000000.00\"}")]
    [InlineData("{\"dimension\":\"driver-age\",\"minimumAge\":18,\"maximumAge\":80}")]
    [InlineData("{\"dimension\":\"premium-limit\",\"maximumAmount\":\"0.00\"}")]
    [InlineData("{\"dimension\":\"premium-limit\",\"maximumAmount\":\"10.001\"}")]
    public void ClosedExtentRejectsDistrictOverridesMotorFactsAndInvalidMoney(string json)
    {
        using var value = JsonDocument.Parse(json);
        Assert.Throws<ArgumentException>(() => CommercialCapacityRules.Extension(value.RootElement));
    }

    [Fact]
    public void CommercialSignatureConditionBindsExactTermsAndRejectsForeignMotorConditions()
    {
        var proposal = JsonSerializer.SerializeToElement(new { productCode = "commercial-combined" });
        var value = JsonSerializer.SerializeToElement(new { code = "provide-signed-statement", termsVersionId = Submission, termsHash = new string('a', 64) });
        var parsed = ReferralRules.Condition(value, proposal);
        Assert.Equal(Submission, parsed.TermsVersionId); Assert.Equal(new string('a', 64), parsed.TermsHash);
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(JsonSerializer.SerializeToElement(new { code = "provide-signed-statement", termsVersionId = Submission, termsHash = "bad" }), proposal));
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(JsonSerializer.SerializeToElement(new { code = "provide-driver-proof", driverId = Location, requirementCode = "driving-record" }), proposal));
    }

    [Fact]
    public void LocationExtentHasClosedSubjectIdentityAndCannotEnterMotorTradeParser()
    {
        var value = JsonSerializer.SerializeToElement(new { dimension = "single-location", maximumAmount = "3000000.00", riskItemId = Location });
        Assert.Equal(new CommercialCapacityExtension("single-location", 3000000m, Location), CommercialCapacityRules.Extension(value));
        Assert.Throws<ArgumentException>(() => CapacityRules.Extension(value));
    }
}
