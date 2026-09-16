using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteInsuranceRulesTests
{
    private static JsonNode Example(string product = "motor-trade-road-risks")
    { using var stream = typeof(QuoteInsuranceRulesTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!; return JsonNode.Parse(stream)!["proposal"]!; }
    private static JsonNode Reference(string collection,int value)
    {
        using var stream = typeof(QuoteInsuranceRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!; var catalogue = JsonNode.Parse(stream)!;
        return new JsonObject { ["collection"] = collection,["version"] = QuoteCatalogueIdentity.Version,["value"] = value,["label"] = catalogue["collections"]![collection]!.AsArray().First(row => row!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }
    private static JsonNode Insurance(JsonNode p) => p["risk"]!["previousInsurance"]!;
    private static void Set(JsonNode p,string id,JsonNode value,string kind = "reference")
    {
        var rows = Insurance(p)["responses"]!["answers"]!.AsArray(); var row = rows.FirstOrDefault(item => item!["questionId"]!.GetValue<string>() == id);
        if (row is null) rows.Add(new JsonObject { ["questionId"] = id,["kind"] = kind,["value"] = value }); else row["value"] = value;
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode p) { using var document = JsonDocument.Parse(p.ToJsonString()); return QuoteInsuranceRules.Assess(document.RootElement); }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void CompleteProductFixturesHaveConsistentInsuranceDeclarations(string product) => Assert.Empty(Check(Example(product)));
    [Fact]
    public void MissingAndFalseNoDiscountDeclarationsRemainDifferent()
    {
        var p = Example(); var rows = Insurance(p)["responses"]!["answers"]!.AsArray(); rows.Remove(rows.First(row => row!["questionId"]!.GetValue<string>() == "prototype.quote.1bdc05ff8b3d"));
        Assert.Contains(Check(p),i => i.Code == "prototype-insurance-answer-required" && i.QuestionId == "prototype.quote.1bdc05ff8b3d");
        Set(p,"prototype.quote.1bdc05ff8b3d",JsonValue.Create(false)!,"boolean"); Assert.Empty(Check(p));
        Set(p,"prototype.no-claims.reason",JsonValue.Create(" ")!,"text"); Assert.Contains(Check(p),i => i.Code == "conditional-answer-required" && i.QuestionId == "prototype.no-claims.reason");
    }
    [Fact]
    public void CombinedNeedsSharedSourceNcbButNotRoadRisksOnlyPrototypeQuestions()
    {
        var p = Example("motor-trade-combined"); Insurance(p)["responses"]!["answers"] = new JsonArray();
        var issue = Assert.Single(Check(p)); Assert.Equal("MTS-05-Q10",issue.QuestionId); Assert.Equal("insurance-answer-required",issue.Code);
    }
    [Theory]
    [InlineData(16,14,"exact",true)]
    [InlineData(16,15,"exact",false)]
    [InlineData(16,18,"exact",false)]
    [InlineData(16,14,"at-least",false)]
    [InlineData(6,4,"at-least",false)]
    [InlineData(6,6,"at-least",true)]
    [InlineData(6,4,"exact",true)]
    public void ExactAndCappedNoClaimsYearsUseSourceMeaning(int selection,int years,string basis,bool conflict)
    {
        var p = Example(); Set(p,"MTS-05-Q10",Reference("noClaimBonuses",selection)); Insurance(p)["noClaimsYears"] = years; Insurance(p)["noClaimsYearsBasis"] = basis;
        Assert.Equal(conflict,Check(p).Any(i => i.Code == "conflicting-ncb-years"));
    }
    [Fact]
    public void OriginOrdinalIdsAreReconciledByMeaningAndProtectionIsIndependent()
    {
        var p = Example(); Set(p,"prototype.quote.c0650c760167",Reference("prototype.quote.c0650c760167",1)); Set(p,"MTS-05-Q11",Reference("noClaimBonusesEarned",3));
        Assert.DoesNotContain(Check(p),i => i.Code == "conflicting-ncb-origin"); Set(p,"MTS-05-Q11",Reference("noClaimBonusesEarned",1));
        Assert.Equal(2,Check(p).Count(i => i.Code == "conflicting-ncb-origin"));
        Set(p,"MTS-05-Q17",JsonValue.Create(true)!,"boolean"); Assert.Equal(2,Check(p).Count(i => i.Code == "conflicting-ncb-protection"));
    }
    [Fact]
    public void ExpiryAndOtherInsurerLimitsDoNotRewriteRetainedAnswers()
    {
        var p = Example(); Insurance(p)["noClaimsBonusExpiresOn"] = "1899-12-31"; Set(p,"MTS-05-Q16",JsonValue.Create(new string('x',51))!,"text"); var before = p.ToJsonString();
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "ncb-expiry-too-early" && i.Path == "/risk/previousInsurance/noClaimsBonusExpiresOn"); Assert.Contains(issues,i => i.Code == "insurer-details-too-long"); Assert.Contains(issues,i => i.Code == "inactive-insurance-answer-retained"); Assert.Equal(before,p.ToJsonString());
    }
    [Fact]
    public void ClaimedDiscountNeedsOriginPositiveYearsAndSeparatePolicyExpiry()
    {
        var p = Example(); Set(p,"prototype.quote.1bdc05ff8b3d",JsonValue.Create(true)!,"boolean"); Insurance(p)["noClaimsYears"] = 0; Insurance(p).AsObject().Remove("expiresOn"); Insurance(p)["noClaimsBonusExpiresOn"] = "2026-09-15";
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "claimed-discount-years-must-be-positive"); Assert.Contains(issues,i => i.Code == "prototype-insurance-answer-required" && i.Path == "/risk/previousInsurance/expiresOn"); Assert.Contains(issues,i => i.Code == "inactive-answer-retained" && i.QuestionId == "prototype.no-claims.reason");
    }
}
