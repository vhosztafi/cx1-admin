using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteReadinessTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void ValidAnnualTermAndTypedAnswerAreNotFlaggedByUnselectedSchemaBranches(string product)
    {
        var proposal = new JsonObject {
            ["schemaVersion"] = "1.0", ["productCode"] = product,
            ["termIntent"] = new JsonObject { ["kind"] = "annual", ["localStartDate"] = "2024-02-29", ["localStartTime"] = "12:00", ["timeZone"] = "Europe/London" },
            ["insured"] = new JsonObject { ["responses"] = new JsonObject { ["questionSetVersion"] = QuoteCatalogueIdentity.Version,
                ["answers"] = new JsonArray(new JsonObject { ["questionId"] = "MTS-01-Q01", ["kind"] = "boolean", ["value"] = false }) } }
        };
        using var document = JsonDocument.Parse(proposal.ToJsonString());
        var issues = QuoteCaptureShape.ValidateCompleteness(document.RootElement);
        Assert.NotEmpty(issues); // Other source sections are intentionally missing.
        Assert.DoesNotContain(issues, issue => issue.Path.StartsWith("/termIntent", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, issue => issue.Path.StartsWith("/insured/responses", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Code == "schema-required");
    }

    [Fact]
    public void SelectedShortPeriodBranchStillRequiresEndFields()
    {
        using var proposal = JsonDocument.Parse("""
            {"schemaVersion":"1.0","productCode":"motor-trade-road-risks",
             "termIntent":{"kind":"short-period","localStartDate":"2026-01-01","localStartTime":"12:00","timeZone":"Europe/London"}}
            """);
        var issues = QuoteCaptureShape.ValidateCompleteness(proposal.RootElement);
        Assert.Contains(issues, issue => issue.Path == "/termIntent" && issue.Code == "schema-required");
    }

    [Fact]
    public void MissingAnswersKeepDistinctQuestionIdentitiesAtTheSameContainerPath()
    {
        using var proposal = JsonDocument.Parse("{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}");
        var result = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), proposal.RootElement, QuoteTerm.Assess(), null, new DateOnly(2026,9,15));
        foreach (var id in new[] { "MTS-03-Q04", "MTS-03-Q06", "MTS-03-Q08", "MTS-03-Q09" })
            Assert.Contains(result.Issues, x => x.Code == "required-capture-field" && x.Path == "/risk/business/responses/answers" && x.QuestionId == id);
        foreach (var id in new[] { "prototype.quote.c6181a11c34c", "prototype.quote.0552d5a68ba2", "prototype.quote.34613c23e95d" })
            Assert.Contains(result.Issues, x => x.Code == "required-prototype-business-answer" && x.QuestionId == id);
        Assert.False(result.Ready); Assert.True(result.Issues.Count <= 100);
        Assert.Contains(result.Issues, x => x.Code == "quote-assessment-unavailable" && x.QuestionId is null);
    }

    [Fact]
    public void QuestionMetadataIsOmittedForOrdinaryFieldsAndSerializedForAnswerIssues()
    {
        var issues = new[] {
            new QuoteReadinessIssue("/insured/firstName", "required-capture-field", "Complete this field.", "capture", "error"),
            new QuoteReadinessIssue("/risk/business/responses/answers", "required-capture-field", "Complete this field.", "capture", "error", "MTS-03-Q04") };
        var json = JsonSerializer.SerializeToElement(issues, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json[0].TryGetProperty("questionId", out _));
        Assert.Equal("MTS-03-Q04", json[1].GetProperty("questionId").GetString());
        Assert.NotEqual(issues[1], issues[1] with { QuestionId = "MTS-03-Q06" });
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void ConditionalAnswerIdentitySurvivesMissingValuesAndAnswerReordering(string product)
    {
        using var stream = typeof(QuoteReadinessTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!;
        var proposal = JsonNode.Parse(stream)!["proposal"]!;
        var answers = proposal["risk"]!["declarations"]!["answers"]!.AsArray();
        answers.First(x => x!["questionId"]!.GetValue<string>() == "MTS-12-Q01")!["value"] = true;
        QuoteFieldIssue[] Assess()
        {
            using var document = JsonDocument.Parse(proposal.ToJsonString());
            return QuoteBusinessRules.Assess(document.RootElement).Where(x => x.Code == "conditional-answer-required").ToArray();
        }
        var missing = Assert.Single(Assess()); Assert.Equal("MTS-12-Q02", missing.QuestionId);
        Assert.Equal("/risk/declarations/answers", missing.Path);
        answers.Add(new JsonObject { ["questionId"] = "MTS-12-Q02", ["kind"] = "text", ["value"] = " " });
        var last = Assert.Single(Assess()); Assert.Equal("MTS-12-Q02", last.QuestionId);
        Assert.Equal($"/risk/declarations/answers/{answers.Count - 1}/value", last.Path);
        var child = answers.Last()!; answers.Remove(child); answers.Insert(0, child);
        var first = Assert.Single(Assess()); Assert.Equal("MTS-12-Q02", first.QuestionId);
        Assert.Equal("/risk/declarations/answers/0/value", first.Path);
    }

    [Fact]
    public void IncompleteCaptureReportsTermAndStructureWithoutClaimingReadiness()
    {
        using var proposal = JsonDocument.Parse("{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}");
        var quote = Guid.NewGuid(); var revision = Guid.NewGuid();
        var result = QuoteReadiness.Assess(quote, revision, proposal.RootElement, QuoteTerm.Assess(), "agency-unavailable", new DateOnly(2026,9,15));
        Assert.Equal(quote, result.QuoteId); Assert.Equal(revision, result.RevisionId); Assert.False(result.Ready);
        Assert.Contains(result.Issues, x => x.Code == "required-term-field" && x.Path == "/termIntent/localStartDate");
        Assert.Contains(result.Issues, x => x.Code == "schema-required");
        Assert.Contains(result.Issues, x => x.Code == "agency-unavailable" && x.Category == "eligibility");
        Assert.Contains(result.Issues, x => x.Code == "quote-assessment-unavailable" && x.Severity == "error");
    }

    [Fact]
    public void AllSixCompleteSourceFixturesStillRequireUnimplementedSemanticAndEvidenceGates()
    {
        var names = typeof(QuoteReadinessTests).Assembly.GetManifestResourceNames().Where(x => x.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, names.Length);
        foreach (var name in names)
        {
            using var stream = typeof(QuoteReadinessTests).Assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            var proposal = document.RootElement.GetProperty("proposal");
            Assert.Empty(QuoteCaptureShape.ValidateCompleteness(proposal));
            var term = QuoteTerm.Assess(proposal.GetProperty("termIntent")); Assert.Empty(term.Issues);
            var result = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), proposal, term, null, new DateOnly(2026,9,15));
            Assert.False(result.Ready);
            Assert.Equal("quote-assessment-unavailable", Assert.Single(result.Issues, issue => issue.Code != "vehicle-capture-context-required").Code);
            Assert.Equal(proposal.GetProperty("risk").GetProperty("vehicles").GetArrayLength(), result.Issues.Count(issue => issue.Code == "vehicle-capture-context-required"));
        }
    }

    [Fact]
    public void BoundedResultsRetainTheProgressionBlocker()
    {
        using var proposal = JsonDocument.Parse("{}");
        var term = new QuoteTermAssessment(null, Enumerable.Range(0, 150).Select(x => new QuoteFieldIssue("required-term-field", "/termIntent/" + x)).ToArray());
        var result = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), proposal.RootElement, term, null, new DateOnly(2026,9,15));
        Assert.False(result.Ready); Assert.Equal(100, result.Issues.Count);
        Assert.Equal("quote-assessment-unavailable", result.Issues[0].Code);
    }
}
