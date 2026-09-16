using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteDriverChronologyTests
{
    private static JsonNode Proposal() => JsonNode.Parse("""
        {"productCode":"motor-trade-road-risks","termIntent":{"localStartDate":"2030-01-01"},
        "risk":{"drivers":[{"dateOfBirth":"2000-02-29","licence":{"issuedOn":"2017-02-28"},"responses":{"answers":[]}}]}}
        """)!;
    private static JsonNode Driver(JsonNode p) => p["risk"]!["drivers"]![0]!;
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode p)
    {
        using var document = JsonDocument.Parse(p.ToJsonString());
        return QuoteDriverRules.Assess(document.RootElement, new DateOnly(2026,9,15));
    }
    [Theory]
    [InlineData("2017-02-27",true)]
    [InlineData("2017-02-28",false)]
    [InlineData("1999-01-01",true)]
    public void LicenceIssueUsesTheActualSeventeenthBirthdayIncludingLeapDay(string issued,bool invalid)
    {
        var p = Proposal(); Driver(p)["licence"]!["issuedOn"] = issued;
        Assert.Equal(invalid, Check(p).Any(i => i.Code == "licence-before-seventeenth-birthday" && i.QuestionId == "MTS-06-Q24"));
    }
    [Fact]
    public void ResidencyDatesUseBirthAndTrustedAssessmentRatherThanFutureInception()
    {
        var p = Proposal(); var answer = new JsonObject { ["questionId"] = "MTS-06-Q22", ["value"] = "1999-01-01" };
        Driver(p)["responses"]!["answers"]!.AsArray().Add(answer);
        Assert.Contains(Check(p), i => i.Code == "residency-before-birth" && i.Path == "/risk/drivers/0/responses/answers/0/value" && i.QuestionId == "MTS-06-Q22");
        answer["value"] = "2026-09-16"; Assert.Contains(Check(p), i => i.Code == "residency-in-future");
        answer["value"] = "2026-09-15"; Assert.DoesNotContain(Check(p), i => i.Code is "residency-in-future" or "residency-before-birth");
    }
    [Fact]
    public void SourceTextLimitsApplyToAllDriverAddressesAndDisabilityExplanation()
    {
        var p = Proposal(); var d = Driver(p); var address = new JsonObject { ["postcode"] = new string('P',10) }; d["address"] = address;
        foreach (var field in new[] { "houseNumber", "street", "town", "city", "county" }) address[field] = new string('A',50);
        var answer = new JsonObject { ["questionId"] = "MTS-06-Q47", ["value"] = new string('D',50) }; d["responses"]!["answers"]!.AsArray().Add(answer);
        Assert.DoesNotContain(Check(p), i => i.Code == "source-text-too-long");
        foreach (var field in address.Select(item => item.Key).ToArray()) address[field] = address[field]!.GetValue<string>() + "X";
        answer["value"] = new string('D',51); var before = p.ToJsonString();
        var issues = Check(p); Assert.Equal(7,issues.Count(i => i.Code == "source-text-too-long")); Assert.Equal(before,p.ToJsonString());
        Assert.Contains(issues,i => i.QuestionId == "MTS-06-Q47" && i.Path.EndsWith("/0/value",StringComparison.Ordinal));
    }
    [Theory]
    [InlineData("under-3-months",1,2)]
    [InlineData("3-to-6-months",3,6)]
    [InlineData("6-to-12-months",6,12)]
    [InlineData("over-12-months",13,1200)]
    public void DeclaredBanRangesKeepInclusiveBoundariesAndRequireExactDuration(string band,int minimum,int maximum)
    {
        var p = Proposal(); var conviction = new JsonObject { ["declaredBanPeriod"] = band, ["disqualified"] = true };
        Driver(p)["convictions"] = new JsonArray(conviction);
        Assert.Contains(Check(p), i => i.Code == "exact-ban-duration-required");
        foreach (var months in new[] { minimum,maximum }) { conviction["banMonths"] = months; Assert.DoesNotContain(Check(p), i => i.Code is "ban-duration-outside-declared-band" or "conflicting-disqualification-declaration"); }
        foreach (var months in new[] { minimum-1,maximum+1 }) { conviction["banMonths"] = months; Assert.Contains(Check(p), i => i.Code == "ban-duration-outside-declared-band"); }
        conviction["disqualified"] = false; Assert.Contains(Check(p), i => i.Code == "conflicting-disqualification-declaration");
    }
    [Fact]
    public void NoBanAndMissingBandRemainDistinctEvenWhenParentHistoryIsInactive()
    {
        var p = Proposal(); var conviction = new JsonObject { ["declaredBanPeriod"] = "none", ["disqualified"] = false, ["banMonths"] = 0 };
        Driver(p)["convictions"] = new JsonArray(conviction);
        Assert.DoesNotContain(Check(p), i => i.Code is "exact-ban-duration-required" or "conflicting-disqualification-declaration" or "ban-duration-outside-declared-band");
        conviction.Remove("declaredBanPeriod"); Assert.Contains(Check(p), i => i.Code == "declared-ban-period-required");
    }
}
