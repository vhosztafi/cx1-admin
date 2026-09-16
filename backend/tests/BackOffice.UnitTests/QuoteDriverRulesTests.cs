using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteDriverRulesTests
{
    private static JsonNode Example(string product = "motor-trade-road-risks")
    {
        using var stream = typeof(QuoteDriverRulesTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!;
        return JsonNode.Parse(stream)!["proposal"]!;
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode proposal)
    { using var document = JsonDocument.Parse(proposal.ToJsonString()); return QuoteDriverRules.Assess(document.RootElement); }
    private static JsonNode Driver(JsonNode proposal) => proposal["risk"]!["drivers"]![0]!;
    private static JsonNode Answer(JsonNode holder,int number) => holder["responses"]!["answers"]!.AsArray().First(item => item!["questionId"]!.GetValue<string>() == $"MTS-06-Q{number:00}")!;
    private static JsonNode Reference(string collection,int value)
    {
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var reference = JsonNode.Parse(stream)!;
        return new JsonObject { ["collection"] = collection, ["version"] = QuoteCatalogueIdentity.Version, ["value"] = value,
            ["label"] = reference["collections"]![collection]!.AsArray().First(item => item!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void SourceBaselineHasNoDriverSectionIssues(string product) => Assert.Empty(Check(Example(product)));

    [Fact]
    public void ComposedReadinessIncludesDriverQuestionIdentityAndRetainsGlobalBlock()
    {
        var proposal = Example(); Answer(proposal["risk"]!,1)["value"] = Reference("driverPlans",3);
        using var document = JsonDocument.Parse(proposal.ToJsonString());
        var result = QuoteReadiness.Assess(Guid.NewGuid(),Guid.NewGuid(),document.RootElement,QuoteTerm.Assess(),null);
        Assert.Contains(result.Issues,issue => issue.Code == "any-driver-answer-required" && issue.QuestionId == "MTS-06-Q02");
        Assert.Contains(result.Issues,issue => issue.Code == "quote-assessment-unavailable");
        Assert.False(result.Ready);
    }

    [Theory]
    [InlineData("2009-01-02",true)]
    [InlineData("2009-01-01",false)]
    [InlineData("1941-01-01",false)]
    [InlineData("1940-01-01",true)]
    public void AgeUsesCompletedYearsAtThePolicyStartAndAccepts85(string birth,bool invalid)
    {
        var proposal = Example(); proposal["termIntent"]!["localStartDate"] = "2026-01-01"; Driver(proposal)["dateOfBirth"] = birth;
        Assert.Equal(invalid,Check(proposal).Any(issue => issue.Code == "driver-age-out-of-range"));
    }
    [Fact]
    public void LeapDayAgeUsesTheSourceFebruaryAnniversary()
    {
        var proposal = Example(); Driver(proposal)["dateOfBirth"] = "2008-02-29"; proposal["termIntent"]!["localStartDate"] = "2025-02-28";
        Assert.DoesNotContain(Check(proposal),issue => issue.Code == "driver-age-out-of-range");
        proposal["termIntent"]!["localStartDate"] = "2025-02-27";
        Assert.Contains(Check(proposal),issue => issue.Code == "driver-age-out-of-range");
    }
    [Fact]
    public void AnyDriverSelectionDoesNotSilentlyMergeOrDiscardNamedDrivers()
    {
        var proposal = Example(); var risk = proposal["risk"]!; Answer(risk,1)["value"] = Reference("driverPlans",3);
        Assert.Contains(Check(proposal),issue => issue.Code == "inactive-named-drivers-retained");
        foreach (var number in Enumerable.Range(2,6)) Assert.Contains(Check(proposal),issue => issue.Code == "any-driver-answer-required" && issue.QuestionId == $"MTS-06-Q{number:00}");
        risk["responses"]!["answers"]!.AsArray().Add(new JsonObject { ["questionId"] = "MTS-06-Q02", ["kind"] = "count", ["value"] = 0 });
        Assert.Contains(Check(proposal),issue => issue.Code == "positive-any-driver-count-required");
        Answer(risk,1)["value"]!["version"] = "untrusted";
        Assert.Contains(Check(proposal),issue => issue.Code == "driver-plan-context-required");
    }
    [Theory]
    [InlineData("convictions",33)]
    [InlineData("losses",40)]
    [InlineData("criminalConvictions",48)]
    [InlineData("countyCourtJudgments",53)]
    public void HistoryParentsKeepMissingAndInactiveRowsDistinct(string group,int parent)
    {
        var proposal = Example(); var driver = Driver(proposal); Answer(driver,parent)["value"] = true;
        Assert.Contains(Check(proposal),issue => issue.Code == "driver-history-required" && issue.Path == "/risk/drivers/0/" + group);
        driver[group] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid() });
        Assert.Contains(Check(proposal),issue => issue.Code == "required-driver-history-field" && issue.Path.StartsWith("/risk/drivers/0/" + group + "/0/",StringComparison.Ordinal));
        Answer(driver,parent)["value"] = false;
        var before = proposal.ToJsonString(); Assert.Contains(Check(proposal),issue => issue.Code == "inactive-driver-history-retained"); Assert.Equal(before,proposal.ToJsonString());
        driver["responses"]!["answers"]!.AsArray().Remove(Answer(driver,parent));
        Assert.Contains(Check(proposal),issue => issue.Code == "driver-history-context-required");
    }
    [Fact]
    public void LicenceAndConditionalRequirementsUsePinnedReferencesAndExactQuestionPaths()
    {
        var proposal = Example(); var driver = Driver(proposal);
        driver["licence"]!["type"] = Reference("driverLicenceTypes",3);
        Assert.Contains(Check(proposal),issue => issue.Code == "provisional-licence-not-covered" && issue.QuestionId == "MTS-06-Q23");
        driver["licence"]!["type"]!["label"] = "Forged";
        Assert.DoesNotContain(Check(proposal),issue => issue.Code == "provisional-licence-not-covered");
        Answer(driver,46)["value"] = true; Answer(driver,21)["value"] = false;
        foreach (var number in new[] {22,47}) Assert.Contains(Check(proposal),issue => issue.Code == "required-driver-field" && issue.QuestionId == $"MTS-06-Q{number:00}");
        driver["dateOfBirth"] = "2005-01-01"; proposal["termIntent"]!["localStartDate"] = "2026-01-01"; driver["licence"]!["issuedOn"] = "2025-12-31";
        Assert.Contains(Check(proposal),issue => issue.Code == "young-driver-licence-experience");
    }
    [Fact]
    public void OccupationDuplicatesAndBanLengthAreAssessedWithoutTreatingZeroAsMissing()
    {
        var proposal = Example(); var driver = Driver(proposal); Answer(driver,28)["value"] = Reference("driverTradeEmploymentBasises",2);
        var occupation = new JsonObject { ["id"] = Guid.NewGuid(), ["occupation"] = Reference("occupations",1) };
        driver["occupations"] = new JsonArray(occupation,occupation.DeepClone());
        Assert.Contains(Check(proposal),issue => issue.Code == "duplicate-driver-occupation");
        Answer(driver,33)["value"] = true;
        var conviction = new JsonObject { ["id"] = Guid.NewGuid(), ["disqualified"] = true }; driver["convictions"] = new JsonArray(conviction);
        Assert.Contains(Check(proposal),issue => issue.Code == "ban-length-required"); conviction["banMonths"] = 0;
        Assert.DoesNotContain(Check(proposal),issue => issue.Code == "ban-length-required");
    }
}
