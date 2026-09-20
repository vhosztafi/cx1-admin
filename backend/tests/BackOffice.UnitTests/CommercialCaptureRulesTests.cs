using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialCaptureRulesTests
{
    private static QuoteVersionPins Pins => new(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"), "1.0", "commercial-questions-1", "commercial-reference-1");
    private static JsonObject Example()
    {
        using var stream = typeof(CommercialCaptureRulesTests).Assembly.GetManifestResourceStream("CommercialExamples.Capture")!;
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }
    private static PreparedQuoteCapture Prepare(JsonNode value) => QuoteRules.Prepare(value.ToJsonString(), "commercial-combined", Pins);

    [Fact]
    public void EmptyCommercialCreateAndPartialSourceFixturePassWithoutMotorRegistrations()
    {
        var empty = QuoteRules.Prepare(null, "commercial-combined", Pins);
        Assert.Empty(empty.Registrations);
        using var root = JsonDocument.Parse(empty.Input.Json);
        Assert.Equal("commercial-combined-capture-1", root.RootElement.GetProperty("format").GetString());
        var input = Example(); var before = input.ToJsonString(); var result = Prepare(input);
        Assert.Empty(result.Registrations); Assert.Equal(before, input.ToJsonString());
        Assert.NotNull(QuoteCaptureBoundary.Validate(result.Input.Json, Pins).Input);
    }

    [Theory]
    [InlineData("premium")]
    [InlineData("actorId")]
    [InlineData("districtTotal")]
    public void SystemOwnedRootMembersAreRejected(string member)
    {
        var p = Example(); p[member] = "forged";
        Assert.Throws<QuoteValidationException>(() => Prepare(p));
    }

    [Fact]
    public void CommercialAndMotorShapesCannotCrossProductPins()
    {
        var p = Example(); p["risk"]!["drivers"] = new JsonArray();
        Assert.Throws<QuoteValidationException>(() => Prepare(p));
        p = Example(); p["productCode"] = "motor-trade-combined";
        Assert.Throws<QuoteValidationException>(() => Prepare(p));
        Assert.Throws<InvalidOperationException>(() => QuoteRules.Prepare(Example().ToJsonString(), "commercial-combined",
            Pins with { QuestionSetVersion = QuoteCatalogueIdentity.Version, ReferenceVersion = QuoteCatalogueIdentity.Version }));
    }

    [Fact]
    public void DuplicateRiskIdentityAndForeignLossLocationAreRejected()
    {
        var p = Example(); p["risk"]!["locations"]![1]!["id"] = p["risk"]!["locations"]![0]!["id"]!.DeepClone();
        Assert.Contains(Assert.Throws<QuoteValidationException>(() => Prepare(p)).Issues, x => x.Code == "duplicate-risk-id");
        p = Example(); p["risk"]!["losses"] = JsonNode.Parse("""[{"id":"00000000-0000-4000-8000-000000000099","riskItemId":"00000000-0000-4000-8000-000000000098"}]""");
        Assert.Contains(Assert.Throws<QuoteValidationException>(() => Prepare(p)).Issues, x => x.Code == "loss-location-not-owned");
    }

    [Fact]
    public void DuplicateQuestionAndIncorrectSubjectScopeAreRejected()
    {
        var p = Example(); var answers = p["risk"]!["declarations"]!["answers"]!.AsArray();
        var duplicate = answers[0]!.DeepClone(); duplicate["value"] = false; answers.Add(duplicate);
        Assert.Contains(Assert.Throws<QuoteValidationException>(() => Prepare(p)).Issues, x => x.Code == "duplicate-question-id");
        p = Example(); p["risk"]!["locations"]![0]!["responses"]!["answers"] = new JsonArray(p["risk"]!["declarations"]!["answers"]![0]!.DeepClone());
        Assert.Throws<QuoteValidationException>(() => Prepare(p));
    }

    [Fact]
    public void ExactMoneyAndPostcodeSyntaxAreEnforcedWithoutRounding()
    {
        foreach (var amount in new[] { "1.001", "01.00", "-1.00", "10000000000000.00" })
        {
            var p = Example(); p["risk"]!["locations"]![0]!["buildings"] = amount;
            Assert.Throws<QuoteValidationException>(() => Prepare(p));
        }
        var invalid = Example(); invalid["risk"]!["locations"]![0]!["address"]!["postcode"] = "ZZ99ZZ";
        Assert.Contains(Assert.Throws<QuoteValidationException>(() => Prepare(invalid)).Issues, x => x.Code == "location-postcode-invalid");
        var valid = Example(); valid["risk"]!["locations"]![0]!["address"]!["postcode"] = "GIR0AA";
        Assert.NotNull(Prepare(valid));
    }

    [Fact]
    public void EmptyItemIdentityCannotBeSavedAsAnOwnedRisk()
    {
        var p = Example(); p["risk"]!["locations"]![0]!["id"] = Guid.Empty.ToString("D");
        Assert.Contains(Assert.Throws<QuoteValidationException>(() => Prepare(p)).Issues, x => x.Code == "risk-id-empty");
    }

    [Fact]
    public void CommercialLossReadinessUsesThePersistedOccurredOnField()
    {
        var p = Example();
        p["risk"]!["losses"] = new JsonArray(new JsonObject { ["id"] = "00000000-0000-4000-8000-000000000099", ["occurredOn"] = "2027-01-01" });
        using var doc = JsonDocument.Parse(Prepare(p).Input.Json);
        var issues = CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20));
        Assert.Contains(issues, x => x.Code == "loss-date-after-assessment" && x.Path == "/risk/losses/0/occurredOn");
        Assert.DoesNotContain(issues, x => x.Path == "/risk/losses/0/date");
    }

    [Fact]
    public void SourceCommercialCharityOrTrustOptionHasADistinctCaptureValue()
    {
        var p = Example(); p["insured"]!["entityType"] = "charity-or-trust";
        Assert.NotNull(Prepare(p));
    }

    [Fact]
    public void HealthAndSafetyDetailsAreNotRequiredForNoInspectorRecommendations()
    {
        var p = Example(); var answers = p["risk"]!["declarations"]!["answers"]!.AsArray();
        using var stream = typeof(CommercialCaptureReadiness).Assembly.GetManifestResourceStream("QuoteCapture.Questions")!;
        using var source = JsonDocument.Parse(stream);
        foreach (var question in source.RootElement.GetProperty("deferredQuestions").EnumerateArray().Where(x =>
            x.TryGetProperty("stages", out var stages) && stages.EnumerateArray().Any(s => s.GetString() == "Commercial Combined:step-9") && x.GetProperty("kind").GetString() == "boolean"))
        {
            var id = question.GetProperty("questionId").GetString()!;
            answers.Add(new JsonObject { ["questionId"] = id, ["kind"] = "boolean", ["value"] = id != "prototype.quote.978fde66afb5" });
        }
        using var doc = JsonDocument.Parse(Prepare(p).Input.Json);
        Assert.DoesNotContain(CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20)), x => x.QuestionId == "prototype.quote-value.a0e5d1b910f8");
    }

    [Theory]
    [InlineData("prototype.quote.00fa2758dd8c", 2, "No")]
    [InlineData("prototype.quote.00fa2758dd8c", 3, "Some outstanding")]
    [InlineData("prototype.quote.6d9a54d9e464", 2, "No")]
    [InlineData("prototype.quote.956ebc71fded", 3, "Signed but not enforced")]
    [InlineData("prototype.quote.992fa2724da2", 2, "No")]
    [InlineData("prototype.quote.ca115cf242f7", 2, "No")]
    [InlineData("prototype.quote.978fde66afb5", 3, "Yes — some outstanding")]
    public void AdverseHealthDropdownAnswersRequireExplanatoryDetails(string id, int value, string label)
    {
        var p = Example();
        p["risk"]!["declarations"]!["answers"]!.AsArray().Add(new JsonObject { ["questionId"] = id, ["kind"] = "reference",
            ["value"] = new JsonObject { ["collection"] = id, ["value"] = value, ["label"] = label, ["version"] = CommercialCaptureRules.ReferenceVersion } });
        using var doc = JsonDocument.Parse(Prepare(p).Input.Json);
        Assert.Contains(CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20)), x => x.QuestionId == "prototype.quote-value.a0e5d1b910f8");
    }

    [Fact]
    public void PublishedCapturePinsAcceptOnlyMatchingCatalogueFamilies()
    {
        string Settings(string questions, string references) => JsonSerializer.Serialize(new { demo = true, kind = "quote-capture",
            products = new[] { new { productVersionId = Pins.ProductVersionId, schemaVersion = "1.0", questionSetVersion = questions, referenceVersion = references } } });
        Assert.NotNull(QuoteCaptureConfiguration.Parse(Settings(Pins.QuestionSetVersion, Pins.ReferenceVersion)));
        Assert.Null(QuoteCaptureConfiguration.Parse(Settings(Pins.QuestionSetVersion, QuoteCatalogueIdentity.Version)));
        Assert.Null(QuoteCaptureConfiguration.Parse(Settings("commercial-questions-2", Pins.ReferenceVersion)));
    }

    [Fact]
    public void PartialCommercialReadinessDoesNotFallThroughToMotorQuestionsOrEvidence()
    {
        using var doc = JsonDocument.Parse(Example().ToJsonString());
        var result = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), doc.RootElement,
            new QuoteTermAssessment(null, []), null, new DateOnly(2026, 9, 20));
        Assert.False(result.Ready);
        Assert.Contains(result.Issues, x => x.Code == "commercial-question-required");
        Assert.DoesNotContain(result.Issues, x => x.Code.Contains("driver", StringComparison.Ordinal) ||
            x.Code.Contains("vehicle", StringComparison.Ordinal) || x.Code.Contains("motor-trader", StringComparison.Ordinal));
        Assert.DoesNotContain(QuoteEvidenceRequirements.ForProposal(doc.RootElement), x => x.Code == "motor-trader-proof");
    }

    [Fact]
    public void QuestionCatalogueCovers109QuestionsAndKeepsLocationSubjectsSeparate()
    {
        Assert.Equal(109, CommercialCaptureReadiness.QuestionCount);
        using var doc = JsonDocument.Parse(Example().ToJsonString());
        var issues = CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20));
        var walls = issues.Where(x => x.QuestionId == "prototype.addloc.wall-construction").ToArray();
        Assert.Equal(2, walls.Length);
        Assert.Contains(walls, x => x.Path == "/risk/locations/0/responses/answers");
        Assert.Contains(walls, x => x.Path == "/risk/locations/1/responses/answers");
    }

    [Fact]
    public void DisabledSectionsDoNotRequireTheirQuestionsButRetainedDetailsBlockReadiness()
    {
        var p = Example();
        p["risk"]!["declarations"]!["answers"]![0]!["value"] = false;
        p["cover"]!["responses"]!["answers"]![0]!["value"] = false;
        using var doc = JsonDocument.Parse(p.ToJsonString());
        var issues = CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20));
        Assert.Contains(issues, x => x.Code == "el-disabled-details");
        Assert.Contains(issues, x => x.Code == "bi-disabled-details");
        Assert.DoesNotContain(issues, x => x.QuestionId == "prototype.quote.7bd824326d4c");
        Assert.DoesNotContain(issues, x => x.Code == "el-details-required");
    }

    [Fact]
    public void MoneyTotalsAndPercentageTotalsRemainReadinessBlockersOnSaveableDrafts()
    {
        var p = Example(); p["risk"]!["business"]!["activities"]![0]!["percentageBasisPoints"] = 9000;
        p["risk"]!["locations"]![0]!["buildings"] = "0.00";
        p["risk"]!["locations"]![0]!["contents"] = "0.00";
        p["risk"]!["locations"]![0]!["stock"] = "0.00";
        var saved = Prepare(p);
        using var doc = JsonDocument.Parse(saved.Input.Json);
        var issues = CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20));
        Assert.Contains(issues, x => x.Code == "activity-total-must-be-10000");
        Assert.Contains(issues, x => x.Code == "location-positive-sum-insured-required");
        Assert.Contains(issues, x => x.Code == "location-mel-exceeds-total");
    }

    [Fact]
    public void ExplicitCompleteCommercialFactsCanPassCaptureReadiness()
    {
        var p = Example();
        p["insured"]!["companyNumber"] = "01234567";
        p["risk"]!["business"]!["startedOn"] = "2015-01-01";
        p["risk"]!["business"]!["vatRegistered"] = true;
        p["risk"]!["businessInterruption"]!["declarationLinked"] = false;
        p["cover"]!["contractWorks"] = new JsonObject { ["selected"] = false };
        foreach (var row in p["risk"]!["locations"]!.AsArray()) { row!["sprinklers"] = false; row["floodZone"] = "1"; }
        using var source = typeof(CommercialCaptureReadiness).Assembly.GetManifestResourceStream("QuoteCapture.Questions")!;
        using var catalogue = JsonDocument.Parse(source);
        foreach (var q in catalogue.RootElement.GetProperty("deferredQuestions").EnumerateArray())
        {
            var id = q.GetProperty("questionId").GetString()!; var kind = q.GetProperty("kind").GetString()!;
            var container = q.GetProperty("targetContainer").GetString()!;
            IEnumerable<JsonNode> Resolve(JsonNode owner, string[] parts)
            {
                if (parts.Length == 0) { yield return owner; yield break; }
                var part = parts[0]; var array = part.EndsWith("[]", StringComparison.Ordinal); var name = array ? part[..^2] : part;
                if (array)
                {
                    foreach (var child in owner[name]!.AsArray()) foreach (var resolved in Resolve(child!, parts[1..])) yield return resolved;
                }
                else foreach (var resolved in Resolve(owner[name]!, parts[1..])) yield return resolved;
            }
            foreach (var target in Resolve(p, container.Split('.')))
            {
                var answers = target["answers"]!.AsArray();
                if (answers.Any(x => x!["questionId"]!.GetValue<string>() == id)) continue;
                JsonNode value = kind switch
                {
                    "boolean" => JsonValue.Create(false)!, "text" => JsonValue.Create("None declared")!,
                    "money" => JsonValue.Create("0.00")!, "count" => JsonValue.Create(id == "prototype.addloc.year-built" ? 1990 : 1)!,
                    "percentage" => JsonValue.Create(0)!,
                    "reference" => Reference(q, id),
                    _ => throw new InvalidOperationException("Unexpected source question kind: " + kind)
                };
                var answer = new JsonObject { ["questionId"] = id, ["kind"] = kind, ["value"] = value };
                if (kind == "money") answer["currency"] = "GBP";
                if (kind == "percentage") answer["unit"] = "basis-points";
                answers.Add(answer);
            }
        }
        var prepared = Prepare(p);
        using var doc = JsonDocument.Parse(prepared.Input.Json);
        Assert.Empty(CommercialCaptureReadiness.Assess(doc.RootElement, new DateOnly(2026, 9, 20)));

        static JsonNode Reference(JsonElement q, string id)
        {
            var hasValues = q.TryGetProperty("referenceValues", out var values) && values.GetArrayLength() > 0;
            var value = hasValues ? JsonNode.Parse(values[0].GetProperty("value").GetRawText())! : JsonValue.Create(1)!;
            var label = hasValues ? values[0].GetProperty("label").GetString()! : q.GetProperty("sourceOptions")[0].GetString()!;
            return new JsonObject { ["collection"] = id, ["value"] = value, ["label"] = label, ["version"] = CommercialCaptureRules.ReferenceVersion };
        }
    }
}
