using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteCaptureShapeTests
{
    private static readonly QuoteVersionPins Pins = new(Guid.NewGuid(), Guid.NewGuid(), "1.0", "questions", "references");
    private static IReadOnlyList<QuoteFieldIssue> Check(string text)
    {
        var canonical = QuoteCanonicalJson.Create(text, Pins);
        using var document = JsonDocument.Parse(canonical.Json);
        return QuoteCaptureShape.Validate(document.RootElement);
    }
    private static string[] Examples => typeof(QuoteCaptureShapeTests).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).Order().ToArray();
    private static JsonObject Example(string name)
    {
        using var stream = typeof(QuoteCaptureShapeTests).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!["proposal"]!.AsObject();
    }

    [Fact]
    public void AllSixActualGeneratedMotorTradeCapturesPassTheBundledSchema()
    {
        Assert.Equal(6, Examples.Length);
        foreach (var name in Examples) Assert.Empty(Check(Example(name).ToJsonString()));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-combined\",\"risk\":{\"drivers\":[]}}")]
    public void IncompleteDraftsAreNotMistakenForInvalidShapes(string text) => Assert.Empty(Check(text));

    [Theory]
    [InlineData("premium")]
    [InlineData("productVersionId")]
    [InlineData("state")]
    [InlineData("actorId")]
    public void RootAuthorityFieldsCannotEnterQuoteCapture(string field)
    {
        var p = Example(Examples[0]); p[field] = "forged-sensitive-value";
        var issues = Check(p.ToJsonString());
        Assert.Contains(issues, issue => issue.Code == "schema-additionalProperties");
        Assert.DoesNotContain("forged-sensitive-value", JsonSerializer.Serialize(issues));
    }

    [Fact]
    public void NestedPolicyOutcomesAndUnidentifiedChildrenAreRejected()
    {
        var p = Example(Examples.First(name => name.EndsWith("history-and-extras.json", StringComparison.Ordinal)));
        p["cover"]!["endorsements"] = new JsonArray();
        p["insured"]!["clientId"] = Guid.NewGuid().ToString();
        p["risk"]!["drivers"]![0]!.AsObject().Remove("id");
        var issues = Check(p.ToJsonString());
        Assert.Contains(issues, i => i.Code == "schema-required" && i.Path == "/risk/drivers/0");
        Assert.Contains(issues, i => i.Code == "schema-additionalProperties" && i.Path == "/insured");
        Assert.Contains(issues, i => i.Code == "schema-additionalProperties" && i.Path == "/cover");
    }

    [Theory]
    [InlineData("dateOfBirth", "2026-02-30")]
    [InlineData("id", "not-a-uuid")]
    public void FormatsAreAssertionsRatherThanIgnoredAnnotations(string field, string value)
    {
        var p = Example(Examples.First(name => name.EndsWith("history-and-extras.json", StringComparison.Ordinal)));
        p["risk"]!["drivers"]![0]![field] = value;
        Assert.Contains(Check(p.ToJsonString()), i => i.Code == "schema-format");
    }

    [Fact]
    public void BoundedTypedAnswersRejectWrongKindsFractionalCountsAndMissingDiscriminants()
    {
        var p = Example(Examples[0]);
        var answers = p["risk"]!["business"]!["responses"]!["answers"]!.AsArray();
        var count = answers.First(row => row!["kind"]!.GetValue<string>() == "count")!;
        count["value"] = 1.5;
        Assert.NotEmpty(Check(p.ToJsonString()));
        count["value"] = 1; count.AsObject().Remove("kind");
        Assert.NotEmpty(Check(p.ToJsonString()));
    }

    [Fact]
    public void ReferenceAndMoneyTypesRemainDistinctAndNullIsNotOmission()
    {
        var p = Example(Examples[0]);
        p["risk"]!["business"]!["turnover"] = 1000;
        Assert.NotEmpty(Check(p.ToJsonString()));
        p["risk"]!["business"]!["turnover"] = "1000.00";
        p["insured"]!["address"] = null;
        Assert.NotEmpty(Check(p.ToJsonString()));
    }

    [Fact]
    public async Task SharedSchemaSupportsConcurrentEvaluationWithoutLeakingPriorErrors()
    {
        var valid = Example(Examples[0]).ToJsonString();
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => Check(i % 2 == 0 ? valid : "{}"))));
        for (var i = 0; i < results.Length; i++) if (i % 2 == 0) Assert.Empty(results[i]); else Assert.NotEmpty(results[i]);
    }

    [Fact]
    public void ErrorProjectionIsBoundedAndOnlyContainsCodesAndPointers()
    {
        var p = Example(Examples[0]);
        var vehicles = p["risk"]!["vehicles"]!.AsArray(); vehicles.Clear();
        for (var i = 0; i < 200; i++) vehicles.Add(new JsonObject { ["id"] = "sensitive-invalid-value" });
        var issues = Check(p.ToJsonString());
        Assert.InRange(issues.Count, 1, QuoteCaptureShape.MaximumIssues);
        Assert.All(issues, i => Assert.True(i.Path.Length <= 1024));
        Assert.DoesNotContain("sensitive-invalid-value", JsonSerializer.Serialize(issues));
    }

    [Fact]
    public void EveryDiscriminatedAnswerShapeHasPositiveAndNegativeCoverage()
    {
        const string reference = """{"collection":"demo","value":1,"label":"One","version":"v1"}""";
        var values = new Dictionary<string, string> {
            ["boolean"] = "false", ["text"] = "\"details\"", ["date"] = "\"2026-01-01\"",
            ["count"] = "0", ["money"] = "\"12.34\"", ["reference"] = reference,
            ["references"] = "[" + reference + "]", ["percentage"] = "2500" };
        foreach (var (kind, value) in values)
        {
            var answer = new JsonObject { ["questionId"] = "shape-only", ["kind"] = kind, ["value"] = JsonNode.Parse(value) };
            if (kind == "money") answer["currency"] = "GBP";
            if (kind == "percentage") answer["unit"] = "basis-points";
            var p = new JsonObject { ["schemaVersion"] = "1.0", ["productCode"] = "motor-trade-road-risks",
                ["risk"] = new JsonObject { ["responses"] = new JsonObject { ["questionSetVersion"] = "v1", ["answers"] = new JsonArray(answer) } } };
            Assert.Empty(Check(p.ToJsonString()));
            answer["value"] = null;
            Assert.NotEmpty(Check(p.ToJsonString()));
            answer["value"] = JsonNode.Parse(value);
            if (kind == "money") { answer.Remove("currency"); Assert.NotEmpty(Check(p.ToJsonString())); }
            if (kind == "percentage") { answer["unit"] = "percent"; Assert.NotEmpty(Check(p.ToJsonString())); }
        }
    }

    [Fact]
    public void ShapeSuccessDoesNotClaimCatalogueOrIdentityAcceptance()
    {
        var p = Example(Examples[0]);
        var answer = p["risk"]!["responses"]!["answers"]![0]!;
        answer["questionId"] = "unknown-but-shaped";
        Assert.Empty(Check(p.ToJsonString())); // The next catalogue gate must reject this.
        var vehicle = p["risk"]!["vehicles"]![0]!;
        vehicle["ownerDriverId"] = Guid.NewGuid().ToString();
        Assert.Empty(Check(p.ToJsonString()));
        using var doc = JsonDocument.Parse(p.ToJsonString());
        Assert.NotEmpty(QuoteItemIdentity.Validate(doc.RootElement));
    }
}
