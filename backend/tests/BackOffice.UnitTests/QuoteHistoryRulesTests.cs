using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteHistoryRulesTests
{
    private static JsonNode Proposal(string key, string date) => new JsonObject {
        ["schemaVersion"] = "1.0", ["productCode"] = "motor-trade-road-risks",
        ["termIntent"] = new JsonObject { ["localStartDate"] = "2030-01-01" },
        ["risk"] = new JsonObject { ["business"] = new JsonObject { ["responses"] = new JsonObject { ["answers"] = new JsonArray() } },
            ["drivers"] = new JsonArray(new JsonObject { [key] = new JsonArray(new JsonObject { ["occurredOn"] = date }) }) }
    };
    private static IReadOnlyList<QuoteHistoryIssue> Check(JsonNode proposal, DateOnly? asOf = null)
    {
        using var document = JsonDocument.Parse(proposal.ToJsonString());
        return QuoteDriverRules.AssessHistory(document.RootElement, asOf ?? new DateOnly(2026,9,15));
    }
    [Theory]
    [InlineData("convictions","ef70e80708bb","2021-09-15","2021-09-14")]
    [InlineData("losses","36da21d3c935","2023-09-15","2023-09-14")]
    [InlineData("countyCourtJudgments","922ca15dc9ed","2021-09-15","2021-09-14")]
    public void LookbackIncludesAnniversaryAndIgnoresFuturePolicyInception(string key,string suffix,string boundary,string older)
    {
        foreach (var product in new[] { "motor-trade-road-risks", "motor-trade-combined" })
        {
            var p = Proposal(key,older); p["productCode"] = product; Assert.Empty(Check(p));
            p["risk"]!["drivers"]![0]![key]![0]!["occurredOn"] = boundary;
            var issue = Assert.Single(Check(p)); Assert.Equal("history-declaration-required",issue.Code);
            Assert.Equal("prototype.quote." + suffix,issue.QuestionId); Assert.Equal($"/risk/drivers/0/{key}/0",issue.RelatedPath);
            var answers = p["risk"]!["business"]!["responses"]!["answers"]!.AsArray();
            answers.Add(new JsonObject { ["questionId"] = issue.QuestionId, ["value"] = false });
            Assert.Equal("/risk/business/responses/answers/0/value",Assert.Single(Check(p)).Path);
            answers[0]!["value"] = true; var before = p.ToJsonString(); Assert.Empty(Check(p)); Assert.Equal(before,p.ToJsonString());
        }
    }
    [Fact]
    public void LeapDayLookbackClampsToFebruary28()
    {
        var p = Proposal("losses","2021-02-28"); Assert.Single(Check(p,new DateOnly(2024,2,29)));
        p["risk"]!["drivers"]![0]!["losses"]![0]!["occurredOn"] = "2021-02-27"; Assert.Empty(Check(p,new DateOnly(2024,2,29)));
    }
    [Fact]
    public void PendingProsecutionRequiresExactPinnedReferenceAndCriminalHistoryHasNoLookbackCutoff()
    {
        var p = Proposal("convictions","2010-01-01"); const string id = "prototype.addconv.prosecution-status";
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var label = JsonNode.Parse(stream)!["collections"]![id]!.AsArray().First(x => x!["value"]!.GetValue<int>() == 2)!["text"]!.DeepClone();
        var reference = new JsonObject { ["collection"] = id, ["value"] = 2, ["version"] = QuoteCatalogueIdentity.Version, ["label"] = label };
        p["risk"]!["drivers"]![0]!["convictions"]![0]!["responses"] = new JsonObject { ["answers"] = new JsonArray(new JsonObject { ["questionId"] = id, ["value"] = reference }) };
        Assert.Single(Check(p)); reference["label"] = "Forged"; Assert.Empty(Check(p));
        Assert.Equal("prototype.quote.46414cc10100",Assert.Single(Check(Proposal("criminalConvictions","1910-01-01"))).QuestionId);
    }
    [Fact]
    public void GlobalYesDoesNotInventDriverHistoryAndFutureDatesRemainInvalid()
    {
        var p = Proposal("losses","2026-09-16");
        p["risk"]!["business"]!["responses"]!["answers"]!.AsArray().Add(new JsonObject { ["questionId"] = "prototype.quote.36da21d3c935", ["value"] = true });
        Assert.Equal("history-date-after-assessment",Assert.Single(Check(p)).Code);
        p["risk"]!["drivers"] = new JsonArray(); Assert.Empty(Check(p));
        Assert.Throws<ArgumentOutOfRangeException>(() => Check(p,new DateOnly(1899,12,31)));
    }
    [Fact]
    public void ComposedReadinessCarriesRelatedHistoryWithoutClaimingProgression()
    {
        using var stream = typeof(QuoteHistoryRulesTests).Assembly.GetManifestResourceStream("QuoteExamples.quote-capture-motor-trade-combined.json")!;
        var p = JsonNode.Parse(stream)!["proposal"]!;
        p["risk"]!["drivers"]![0]!["losses"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid(), ["occurredOn"] = "2026-08-01" });
        using var document = JsonDocument.Parse(p.ToJsonString());
        var result = QuoteReadiness.Assess(Guid.NewGuid(),Guid.NewGuid(),document.RootElement,QuoteTerm.Assess(),null,new DateOnly(2026,9,15));
        var issue = Assert.Single(result.Issues,i => i.Code == "history-declaration-required");
        Assert.Equal("/risk/drivers/0/losses/0",issue.RelatedPath); Assert.False(result.Ready);
        var json = JsonSerializer.SerializeToElement(result,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.GetProperty("issues")[0].TryGetProperty("relatedPath",out _));
        Assert.Contains(json.GetProperty("issues").EnumerateArray(),item => item.TryGetProperty("relatedPath",out var related) && related.GetString() == issue.RelatedPath);
    }
}
