using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteBusinessRulesTests
{
    private static JsonObject Example(string product = "motor-trade-road-risks")
    {
        using var stream = typeof(QuoteBusinessRulesTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!;
        return JsonNode.Parse(stream)!["proposal"]!.AsObject();
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode proposal)
    {
        using var document = JsonDocument.Parse(proposal.ToJsonString());
        return QuoteBusinessRules.Assess(document.RootElement);
    }
    private static JsonArray Answers(JsonNode proposal, string section = "business") =>
        (section == "business" ? proposal["risk"]![section]!["responses"]!["answers"] : proposal["risk"]![section]!["answers"])!.AsArray();
    private static JsonNode Answer(JsonNode proposal, string id, string section = "business") => Answers(proposal, section).First(x => x!["questionId"]!.GetValue<string>() == id)!;
    private static JsonNode Reference(string collection, int value)
    {
        using var stream = typeof(QuoteBusinessRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var data = JsonNode.Parse(stream)!;
        return new JsonObject { ["collection"] = collection, ["value"] = value, ["version"] = QuoteCatalogueIdentity.Version,
            ["label"] = data["collections"]![collection]!.AsArray().First(x => x!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }

    [Fact]
    public void PremisesRequireEverySourceFieldAndRetainInactiveRows()
    {
        var proposal = Example(); Answer(proposal, "MTS-02-Q01")["value"] = Reference("tradingFroms", 3);
        Assert.Contains(Check(proposal), x => x.Code == "premises-required");
        var premise = new JsonObject { ["id"] = Guid.NewGuid(), ["use"] = Reference("tradingPremiseTypes", 1), ["yearsTrading"] = 0, ["sharedWorksite"] = false,
            ["address"] = new JsonObject { ["postcode"] = "AB1 2CD", ["houseNumber"] = "1", ["street"] = "Example Road", ["county"] = "Example County" } };
        proposal["risk"]!["premises"] = new JsonArray(premise); Assert.Empty(Check(proposal));
        foreach (var path in new[] { "use", "yearsTrading", "sharedWorksite", "address.postcode", "address.houseNumber", "address.street", "address.county" })
        {
            var copy = proposal.DeepClone(); var keys = path.Split('.');
            var parent = keys[..^1].Aggregate(copy["risk"]!["premises"]![0]!, (node, key) => node[key]!);
            parent.AsObject().Remove(keys[^1]);
            Assert.Contains(Check(copy), x => x.Code == "required-capture-field" && x.Path == "/risk/premises/0/" + path.Replace('.', '/'));
        }
        Answer(proposal, "MTS-02-Q01")["value"] = Reference("tradingFroms", 1);
        Assert.Contains(Check(proposal), x => x.Code == "inactive-premises-retained");
        Answer(proposal, "MTS-02-Q01")["value"]!["version"] = "old";
        Assert.Contains(Check(proposal), x => x.Code == "premises-context-required");
    }

    [Fact]
    public void ServicingAndMechanicalSharesAreCombinedWithoutInventingAnAllocation()
    {
        var proposal = Example(); var business = proposal["risk"]!["business"]!;
        business["activities"]![0]!["code"] = Reference("mtOccupations", 18);
        business["declaredActivitySplit"]!["sales"] = 0;
        business["declaredActivitySplit"]!["servicing"] = 2500;
        business["declaredActivitySplit"]!["mechanicalRepair"] = 7500;
        Assert.Empty(Check(proposal));
        business["declaredActivitySplit"]!["mechanicalRepair"] = 5000;
        Assert.Contains(Check(proposal), x => x.Code == "activity-split-below-declared-occupations");
    }

    [Fact]
    public void PrototypeHistoryNeedsNarrativeAndPreservesLegitimateMaterialFacts()
    {
        foreach (var suffix in new[] { "ea4580cbac7a", "46414cc10100", "ef70e80708bb", "36da21d3c935", "6c1927f561b8", "f7972c55f517", "382ce4de8253", "922ca15dc9ed" })
        {
            var proposal = Example(); var answer = Answer(proposal, "prototype.quote." + suffix); answer["value"] = true;
            Assert.Contains(Check(proposal), x => x.Code == "declaration-material-facts-required");
            proposal["risk"]!["materialFacts"] = " "; Assert.Contains(Check(proposal), x => x.Code == "declaration-material-facts-required");
            proposal["risk"]!["materialFacts"] = "Fictional declaration explanation"; Assert.Empty(Check(proposal));
            answer["value"] = false; Assert.Empty(Check(proposal));
        }
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void PartTimeTraderRequiresOccupationAndApplicableEmployment(string product)
    {
        const string trader = "prototype.quote.c6181a11c34c", employment = "prototype.quote.34613c23e95d", occupation = "prototype.quote-value.3fd9edd7e66e";
        var proposal = Example(product); Answer(proposal, trader)["value"] = Reference(trader, 2);
        Assert.Contains(Check(proposal), x => x.Code == "part-time-employment-required");
        Answers(proposal).Add(new JsonObject { ["questionId"] = occupation, ["kind"] = "text", ["value"] = "Bookkeeper" });
        Answer(proposal, employment)["value"] = Reference(employment, 2); Assert.Empty(Check(proposal));
        Answer(proposal, trader)["value"] = Reference(trader, 1);
        Assert.Contains(Check(proposal), x => x.Code == "inactive-main-occupation-retained");
        Assert.Contains(Check(proposal), x => x.Code == "inactive-main-employment-retained");
        Answer(proposal, trader)["value"]!["label"] = "Forged";
        Assert.Contains(Check(proposal), x => x.Code == "main-occupation-context-required");
    }

    [Fact]
    public void ActivityAppetiteAndVehicleDetailsFollowEveryControllingAnswer()
    {
        foreach (var (parents, detail) in new (string[], string)[] {
            (["ba9d4158ae2c", "35350a32e79e", "2ad460339240", "f5e77ab8ec93", "7fe8e3151553", "27322dcfabf5", "f856f5891026"], "909e1c6eff8c"),
            (["d7a75768e505", "5f9e8331ac6f", "488ecf4bdc09", "87fad6b4a9fe"], "2d662a3ec81d") })
            foreach (var parent in parents)
            {
                var proposal = Example(); var answer = Answer(proposal, "prototype.quote." + parent); answer["value"] = true;
                Assert.Contains(Check(proposal), x => x.Code == "required-prototype-business-answer");
                Answers(proposal).Add(new JsonObject { ["questionId"] = "prototype.quote-value." + detail, ["kind"] = "text", ["value"] = "Fictional explanation" });
                Assert.Empty(Check(proposal)); answer["value"] = false;
                Assert.Contains(Check(proposal), x => x.Code == "inactive-prototype-details-retained");
                Answers(proposal).Remove(answer);
                Assert.Contains(Check(proposal), x => x.Code == "prototype-details-context-required");
            }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(13)]
    [InlineData(23)]
    public void OccupationsRequireTheirExplicitActivityDeclaration(int occupation)
    {
        var proposal = Example(); proposal["risk"]!["business"]!["activities"]![0]!["code"] = Reference("mtOccupations", occupation);
        Assert.Contains(Check(proposal), x => x.Code == "activity-declaration-required");
        proposal["risk"]!["business"]!["activities"]![0]!["turnoverBasisPoints"] = 0;
        Assert.DoesNotContain(Check(proposal), x => x.Code == "activity-declaration-required");
    }

    [Fact]
    public void SixSourceExamplesPassWithoutMutation()
    {
        var names = typeof(QuoteBusinessRulesTests).Assembly.GetManifestResourceNames().Where(x => x.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, names.Length);
        foreach (var name in names)
        {
            using var stream = typeof(QuoteBusinessRulesTests).Assembly.GetManifestResourceStream(name)!;
            var proposal = JsonNode.Parse(stream)!["proposal"]!; var before = proposal.ToJsonString();
            Assert.Empty(Check(proposal)); Assert.Equal(before, proposal.ToJsonString());
        }
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void AbsentContainersReportFieldsWhileExplicitFalseAndZeroRemainAnswers(string product)
    {
        var complete = Example(product); Assert.Empty(Check(complete));
        var partial = new JsonObject { ["schemaVersion"] = "1.0", ["productCode"] = product };
        var issues = Check(partial);
        Assert.True(issues.Count >= 35);
        Assert.Contains(issues, x => x.Path == "/insured/firstName" && x.Code == "required-capture-field");
        Assert.Contains(issues, x => x.Path == "/risk/business/declaredActivitySplit/other");
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version);
        Assert.Empty(QuoteCaptureBoundary.Validate(partial.ToJsonString(), pins).Issues);
    }

    [Fact]
    public void CompanyContactAndSourceMinimumsHaveSpecificPaths()
    {
        var proposal = Example(); var insured = proposal["insured"]!;
        insured["declaredCompanyType"]!["value"] = 3;
        // Copy the label from the trusted bundled reference, never infer it.
        using var stream = typeof(QuoteBusinessRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var references = JsonNode.Parse(stream)!;
        insured["declaredCompanyType"]!["label"] = references["collections"]!["companyTypes"]!.AsArray().First(x => x!["value"]!.GetValue<int>() == 3)!["text"]!.DeepClone();
        insured.AsObject().Remove("legalName");
        Assert.Contains(Check(proposal), x => x.Path == "/insured/legalName" && x.Code == "required-capture-field");
        insured["legalName"] = new string('x', 51);
        insured["contact"]!["mobile"] = "061234567890";
        proposal["risk"]!["business"]!["startedOn"] = "1899-12-31";
        Answer(proposal, "MTS-03-Q08")["value"] = 0;
        var codes = Check(proposal).Select(x => x.Code).ToArray();
        foreach (var code in new[] { "company-name-too-long", "contact-number-too-long", "mobile-prefix-invalid", "business-start-too-early", "vehicles-handled-minimum" }) Assert.Contains(code, codes);
        insured["declaredCompanyType"]!["label"] = "Forged";
        Assert.DoesNotContain(Check(proposal), x => x.Code == "company-name-too-long");
    }

    [Theory]
    [InlineData("MTS-12-Q01", "MTS-12-Q02")]
    [InlineData("MTS-12-Q03", "MTS-12-Q04")]
    [InlineData("MTS-12-Q19", "MTS-12-Q20")]
    public void ConditionalDetailsRequireParentAndRetainContradictions(string parent, string child)
    {
        var proposal = Example(); var answers = Answers(proposal, "declarations");
        Answer(proposal, parent, "declarations")["value"] = true;
        Assert.Contains(Check(proposal), x => x.Code == "conditional-answer-required");
        answers.Add(new JsonObject { ["questionId"] = child, ["kind"] = "text", ["value"] = "Fictional explanation" });
        Assert.Empty(Check(proposal));
        Answer(proposal, parent, "declarations")["value"] = false;
        Assert.Contains(Check(proposal), x => x.Code == "inactive-answer-retained" && x.Path.EndsWith("/value", StringComparison.Ordinal));
        answers.Remove(Answer(proposal, parent, "declarations"));
        Assert.Contains(Check(proposal), x => x.Code == "controlling-answer-required");
    }

    [Theory]
    [InlineData("sales")]
    [InlineData("servicing")]
    [InlineData("mechanicalRepair")]
    [InlineData("breakdownRecovery")]
    [InlineData("bodyRepairs")]
    [InlineData("valeting")]
    [InlineData("other")]
    public void EveryShareRequiresAnExplicitValue(string key)
    {
        var proposal = Example(); proposal["risk"]!["business"]!["declaredActivitySplit"]!.AsObject().Remove(key);
        Assert.Contains(Check(proposal), x => x.Code == "activity-split-share-required" && x.Path.EndsWith("/" + key, StringComparison.Ordinal));
    }

    [Fact]
    public void SplitTotalsAndOtherExplanationFollowTheDeclaredShares()
    {
        var proposal = Example(); var split = proposal["risk"]!["business"]!["declaredActivitySplit"]!;
        split["sales"] = 9999;
        Assert.Contains(Check(proposal), x => x.Code == "activity-split-total-invalid");
        Assert.Contains(Check(proposal), x => x.Code == "activity-split-below-declared-occupations");
        split["other"] = 1;
        Assert.DoesNotContain(Check(proposal), x => x.Code == "activity-split-total-invalid");
        Assert.Contains(Check(proposal), x => x.Code == "other-activity-description-required");
        Answers(proposal).Add(new JsonObject { ["questionId"] = "prototype.quote-value.3e4fdf2b682e", ["kind"] = "text", ["value"] = "Fictional other work" });
        Assert.DoesNotContain(Check(proposal), x => x.Code == "other-activity-description-required");
        split["other"] = 0;
        Assert.Contains(Check(proposal), x => x.Code == "inactive-other-activity-description");
        split.AsObject().Remove("other");
        Assert.Contains(Check(proposal), x => x.Code == "other-activity-share-context-required");
    }

    [Fact]
    public void ForgedOccupationDoesNotDriveBusinessDecisions()
    {
        var proposal = Example(); proposal["risk"]!["business"]!["declaredActivitySplit"]!["sales"] = 0;
        Assert.Contains(Check(proposal), x => x.Code == "activity-split-below-declared-occupations");
        proposal["risk"]!["business"]!["activities"]![0]!["code"]!["label"] = "Forged";
        Assert.DoesNotContain(Check(proposal), x => x.Code == "activity-split-below-declared-occupations");
    }
}
