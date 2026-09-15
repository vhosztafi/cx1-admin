using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteCaptureBoundaryTests
{
    private static QuoteVersionPins Pins => new(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"), "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version);
    private static string[] Examples => typeof(QuoteCaptureBoundaryTests).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).Order().ToArray();
    private static JsonObject Example(string? name = null)
    {
        name ??= Examples.First(name => name.EndsWith("history-and-extras.json", StringComparison.Ordinal));
        using var stream = typeof(QuoteCaptureBoundaryTests).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd())!["proposal"]!.AsObject();
    }
    private static QuoteDraftValidation Check(JsonNode p) => QuoteCaptureBoundary.Validate(p.ToJsonString(), Pins);
    private static JsonArray Answers(JsonNode p) => p["risk"]!["business"]!["responses"]!["answers"]!.AsArray();
    private static void Reject(JsonNode p, string code)
    {
        var result = Check(p); Assert.Null(result.Input); Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void ActualSixCapturesPassAllSaveShapeAndIdentityGatesWithoutMutation()
    {
        Assert.Equal(6, Examples.Length);
        foreach (var name in Examples)
        {
            var p = Example(name); var before = p.ToJsonString(); var result = Check(p);
            Assert.Empty(result.Issues); Assert.NotNull(result.Input); Assert.Equal(before, p.ToJsonString());
            Assert.Equal(result.Input, QuoteCaptureBoundary.Validate(result.Input.Json, Pins).Input);
        }
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void IncompleteDraftCanPassWithoutClaimingReadiness(string product)
    {
        var result = QuoteCaptureBoundary.Validate(JsonSerializer.Serialize(new { schemaVersion = "1.0", productCode = product }), Pins);
        Assert.Empty(result.Issues); Assert.NotNull(result.Input);
    }

    [Fact]
    public void QuestionsRequirePinnedVersionAndScopedIdentity()
    {
        var p = Example(); p["risk"]!["business"]!["responses"]!["questionSetVersion"] = "old";
        Reject(p, "question-version-mismatch");
        p = Example(); Answers(p)[0]!["questionId"] = "unknown"; Reject(p, "unknown-question-id");
        p = Example(); Answers(p).Add(Answers(p)[0]!.DeepClone()); Reject(p, "duplicate-question-id");
        p = Example(); p["risk"]!["responses"]!["answers"]!.AsArray().Add(Answers(p)[0]!.DeepClone()); Reject(p, "unknown-question-id");
    }

    [Fact]
    public void MissingQuestionVersionProducesAnIssueEvenForAnEmptyAnswerList()
    {
        var p = Example(); var responses = p["risk"]!["business"]!["responses"]!.AsObject();
        responses.Remove("questionSetVersion"); Reject(p, "question-version-mismatch");
        responses["answers"]!.AsArray().Clear(); Reject(p, "question-version-mismatch");
        responses.Remove("answers"); Assert.Empty(Check(p).Issues);
        responses["questionSetVersion"] = "old"; Reject(p, "question-version-mismatch");
        responses["questionSetVersion"] = QuoteCatalogueIdentity.Version; Assert.Empty(Check(p).Issues);
    }

    [Fact]
    public void WrongButStructurallyValidAnswerKindIsRejectedByCatalogue()
    {
        var p = Example(); var answer = Answers(p).First(a => a!["kind"]!.GetValue<string>() == "boolean")!;
        answer["kind"] = "text"; answer["value"] = "false";
        Reject(p, "question-kind-mismatch");
    }

    [Fact]
    public void QuestionIdentityIsLocalToEachRepeatedDriver()
    {
        var p = Example(); var drivers = p["risk"]!["drivers"]!.AsArray(); var second = drivers[0]!.DeepClone();
        second["id"] = Guid.NewGuid().ToString(); second.AsObject().Remove("losses"); second.AsObject().Remove("convictions");
        drivers.Add(second); Assert.Empty(Check(p).Issues);
        second["responses"]!["answers"]!.AsArray().Add(second["responses"]!["answers"]![0]!.DeepClone());
        var result = Check(p); Assert.Contains(result.Issues, i => i.Code == "duplicate-question-id" && i.Path.StartsWith("/risk/drivers/1/", StringComparison.Ordinal));
    }

    [Fact]
    public void TypedReferenceValueLabelVersionAndFamilyMustAllMatch()
    {
        foreach (var (field, value, code) in new[] {
            ("value", "1", "unknown-reference-value"), ("version", "old", "reference-version-mismatch"),
            ("label", "forged", "reference-label-mismatch"), ("collection", "unknown", "reference-collection-mismatch") })
        {
            var p = Example(); p["insured"]!["declaredCompanyType"]![field] = value; Reject(p, code);
        }
        var missing = Example(); missing["insured"]!["declaredCompanyType"]!["value"] = 999999; Reject(missing, "unknown-reference-value");
    }

    [Fact]
    public void NestedIncidentReferencesAreCheckedAtTheirActualPaths()
    {
        var p = Example(); p["risk"]!["drivers"]![0]!["losses"]![0]!["declaredType"]!["label"] = "Sensitive forged label";
        var result = Check(p);
        Assert.Contains(new QuoteFieldIssue("reference-label-mismatch", "/risk/drivers/0/losses/0/declaredType/label"), result.Issues);
        Assert.DoesNotContain("Sensitive forged label", JsonSerializer.Serialize(result.Issues));
    }

    [Fact]
    public void RootParsingShapeAndOwnershipFailuresNeverReturnPersistableInput()
    {
        var duplicate = QuoteCaptureBoundary.Validate("{\"risk\":{},\"risk\":{}}", Pins);
        Assert.Null(duplicate.Input); Assert.Equal("quote-duplicate-property", Assert.Single(duplicate.Issues).Code);
        var p = Example(); p["premium"] = new JsonObject(); Reject(p, "schema-additionalProperties");
        p = Example(); p["risk"]!["vehicles"]![0]!["ownerDriverId"] = Guid.NewGuid().ToString(); Reject(p, "unknown-item-reference");
        p = Example(); p["risk"]!["vehicles"]![0]!["id"] = Guid.Empty.ToString(); Reject(p, "invalid-item-id");
    }

    [Fact]
    public void UnknownPinnedConfigurationFailsClosedInsteadOfUsingLatest()
    {
        Assert.Throws<InvalidOperationException>(() => QuoteCaptureBoundary.Validate("{}", Pins with { QuestionSetVersion = "old" }));
        Assert.Throws<InvalidOperationException>(() => QuoteCaptureBoundary.Validate("{}", Pins with { ReferenceVersion = "old" }));
        Assert.Throws<InvalidOperationException>(() => QuoteCaptureBoundary.Validate("{}", Pins with { SchemaVersion = "2.0" }));
    }

    [Fact]
    public void ConfiguredDynamicFamilyMembershipIsDistinctFromReadinessEligibility()
    {
        var p = Example(); var answers = p["cover"]!["responses"]!["answers"]!.AsArray();
        var excess = answers.First(a => a!["questionId"]!.GetValue<string>() == "MTS-05-Q04")!;
        // Another configured own-limit family: identity is valid even while the
        // selected own-limit context makes it ineligible. Readiness must flag it.
        excess["value"]!["collection"] = "indemnityOwnVehicles/number:6/excesses";
        Assert.Empty(Check(p).Issues);
        excess["value"]!["collection"] = "driverTitles"; Reject(p, "reference-collection-mismatch");
    }

    private static JsonObject Resource(string name)
    {
        using var stream = typeof(QuoteCatalogueIdentity).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream); return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    [Fact]
    public void EveryReferenceBindingAcceptsItsFirstAndLastPinnedOptionsThroughTheBoundary()
    {
        var questions = Resource("QuoteCapture.Questions"); var references = Resource("QuoteCapture.References");
        var bindings = references["bindings"]!.AsArray(); Assert.Equal(73, bindings.Count);
        foreach (var binding in bindings)
        {
            var questionId = binding!["questionId"]?.GetValue<string>();
            var mapping = questionId is null ? null : questions["mappings"]!.AsArray().First(row => row!["questionId"]?.GetValue<string>() == questionId);
            var product = mapping?["products"]![0]!.GetValue<string>() ?? "motor-trade-road-risks";
            foreach (var collectionNode in binding["collections"]!.AsArray())
            {
                var collection = collectionNode!.GetValue<string>(); var options = references["collections"]![collection]!.AsArray();
                foreach (var option in new[] { options[0]!, options[^1]! })
                {
                    var selection = new JsonObject { ["collection"] = collection, ["version"] = QuoteCatalogueIdentity.Version,
                        ["value"] = option["value"]!.DeepClone(), ["label"] = option["text"]!.DeepClone() };
                    JsonNode value = selection;
                    if (questionId is not null)
                    {
                        var kind = mapping!["answerKind"]!.GetValue<string>();
                        value = new JsonObject { ["questionId"] = questionId, ["kind"] = kind,
                            ["value"] = kind == "references" ? new JsonArray(selection) : selection };
                    }
                    var p = new JsonObject { ["schemaVersion"] = "1.0", ["productCode"] = product }; var cursor = p;
                    var parts = binding["canonicalPath"]!.GetValue<string>().Split('.');
                    for (var i = 0; i < parts.Length; i++)
                    {
                        var array = parts[i].EndsWith("[]", StringComparison.Ordinal); var key = array ? parts[i][..^2] : parts[i];
                        if (i == parts.Length - 1)
                        {
                            if (key == "answers") cursor["questionSetVersion"] = QuoteCatalogueIdentity.Version;
                            cursor[key] = array ? new JsonArray(value) : value;
                        }
                        else
                        {
                            var child = new JsonObject(); if (array) child["id"] = Guid.NewGuid().ToString();
                            cursor[key] = array ? new JsonArray(child) : child; cursor = child;
                        }
                    }
                    var result = Check(p);
                    Assert.True(result.Issues.Count == 0, collection + ": " + JsonSerializer.Serialize(result.Issues));
                }
            }
        }
    }

    [Fact]
    public void DuplicateMultiSelectsAndOtherProductQuestionsAreRejected()
    {
        var p = Example(); var r = Resource("QuoteCapture.References"); var option = r["collections"]!["marketingMethods"]![0]!;
        var selection = new JsonObject { ["collection"] = "marketingMethods", ["version"] = QuoteCatalogueIdentity.Version,
            ["value"] = option["value"]!.DeepClone(), ["label"] = option["text"]!.DeepClone() };
        p["insured"]!["responses"] = new JsonObject { ["questionSetVersion"] = QuoteCatalogueIdentity.Version,
            ["answers"] = new JsonArray(new JsonObject { ["questionId"] = "MTS-01-Q03", ["kind"] = "references", ["value"] = new JsonArray(selection, selection.DeepClone()) }) };
        Reject(p, "duplicate-reference-selection");
        p = Example(Examples.First(name => name.Contains("combined-history", StringComparison.Ordinal)));
        p["risk"]!["previousInsurance"]!["responses"]!["answers"]!.AsArray().Add(new JsonObject {
            ["questionId"] = "prototype.no-claims.reason", ["kind"] = "text", ["value"] = "Road Risks-only answer" });
        Reject(p, "unknown-question-id");
    }
}
