using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteBusinessReconciliationTests
{
    private static JsonObject Example(string product = "motor-trade-road-risks")
    {
        using var stream = typeof(QuoteBusinessReconciliationTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!;
        return JsonNode.Parse(stream)!["proposal"]!.AsObject();
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode proposal)
    {
        using var document = JsonDocument.Parse(proposal.ToJsonString());
        return QuoteBusinessRules.Assess(document.RootElement);
    }
    private static JsonObject Reference(string collection, int value)
    {
        using var stream = typeof(QuoteBusinessRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var data = JsonNode.Parse(stream)!;
        return new() { ["collection"] = collection, ["value"] = value, ["version"] = QuoteCatalogueIdentity.Version,
            ["label"] = data["collections"]![collection]!.AsArray().First(x => x!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void EveryLegalEntityRetainsItsSourceMeaning(string product)
    {
        foreach (var (entity, allowed) in new (string, int[])[] { ("sole-trader", [1]), ("partnership", [4]), ("llp", [4]), ("limited-company", [2, 3]) })
            foreach (var company in new[] { 1, 2, 3, 4 })
            {
                var proposal = Example(product); var insured = proposal["insured"]!;
                insured["entityType"] = entity; insured["declaredCompanyType"] = Reference("companyTypes", company);
                insured["legalName"] = "Fictional business"; insured["companyNumber"] = "DEMO1234";
                var before = proposal.ToJsonString(); var issues = Check(proposal);
                if (allowed.Contains(company)) Assert.Empty(issues);
                else Assert.Equal(2, issues.Count(x => x.Code == "conflicting-legal-entity"));
                Assert.Equal(before, proposal.ToJsonString());
            }
    }

    [Theory]
    [InlineData("limited-company", 3)]
    [InlineData("llp", 4)]
    public void IncorporatedEntitiesRequireTheirOwnNumberAndTrustedCompanyCategory(string entity, int company)
    {
        var proposal = Example(); var insured = proposal["insured"]!;
        insured["entityType"] = entity; insured["declaredCompanyType"] = Reference("companyTypes", company);
        insured["legalName"] = "Example"; insured.AsObject().Remove("companyNumber");
        Assert.Contains(Check(proposal), x => x.Code == "incorporated-company-number-required" && x.Path == "/insured/companyNumber");
        insured["companyNumber"] = "   "; Assert.Contains(Check(proposal), x => x.Code == "incorporated-company-number-required");
        insured["companyNumber"] = "DEMO1234"; Assert.Empty(Check(proposal));
        insured["declaredCompanyType"]!["label"] = "Forged";
        Assert.Contains(Check(proposal), x => x.Code == "legal-entity-company-context-required");
        Assert.DoesNotContain(Check(proposal), x => x.Code == "conflicting-legal-entity");
    }

    [Fact]
    public void ProposerNamesRemainFullOrderedNamesWithExplicitMissingSlotIssues()
    {
        var proposal = Example(); var insured = proposal["insured"]!;
        insured["proposerNames"] = new JsonArray("Alex Example", "Sam van der Example", "Sam van der Example");
        var before = proposal.ToJsonString(); Assert.Empty(Check(proposal)); Assert.Equal(before, proposal.ToJsonString());
        insured["proposerNames"]![1] = " ";
        Assert.Contains(Check(proposal), x => x.Code == "proposer-name-required" && x.Path == "/insured/proposerNames/1");
        insured["proposerNames"] = new JsonArray(); Assert.Contains(Check(proposal), x => x.Path == "/insured/proposerNames");
        insured.AsObject().Remove("proposerNames"); Assert.Contains(Check(proposal), x => x.Code == "proposer-name-required");
        insured.AsObject().Remove("entityType"); Assert.Contains(Check(proposal), x => x.Code == "legal-entity-required");
    }

    [Fact]
    public void OccupationRowsRequireCodesAndExactCompleteSharesSeparatelyFromPrototypeSplit()
    {
        var proposal = Example(); var business = proposal["risk"]!["business"]!;
        business.AsObject().Remove("activities"); Assert.Contains(Check(proposal), x => x.Code == "business-activity-required");
        business["activities"] = new JsonArray(); Assert.Contains(Check(proposal), x => x.Code == "activity-share-required");
        business["activities"]!.AsArray().Add(new JsonObject { ["id"] = Guid.NewGuid() });
        Assert.Contains(Check(proposal), x => x.Code == "business-activity-code-required" && x.Path == "/risk/business/activities/0/code");
        Assert.Contains(Check(proposal), x => x.Code == "business-activity-minimum-one-percent");
        var row = business["activities"]![0]!; row["code"] = Reference("mtOccupations", 8); row["turnoverBasisPoints"] = 99;
        Assert.Contains(Check(proposal), x => x.Code == "business-activity-minimum-one-percent");
        Assert.Contains(Check(proposal), x => x.Code == "activity-total-must-equal-100-percent");
        Assert.DoesNotContain(Check(proposal), x => x.Code == "activity-split-total-invalid");
        row["turnoverBasisPoints"] = 100; Assert.DoesNotContain(Check(proposal), x => x.Code == "business-activity-minimum-one-percent");
        row["turnoverBasisPoints"] = 10000; Assert.Empty(Check(proposal));
        var duplicate = row.DeepClone(); duplicate["id"] = Guid.NewGuid(); business["activities"]!.AsArray().Add(duplicate);
        Assert.Contains(Check(proposal), x => x.Code == "duplicate-business-activity" && x.Path == "/risk/business/activities/1/code");
    }

    [Fact]
    public void CarJockeyExclusivityUsesPinnedMetadataWithoutErasingOtherActivities()
    {
        var proposal = Example(); var rows = proposal["risk"]!["business"]!["activities"]!.AsArray();
        rows[0]!["code"] = Reference("mtOccupations", 9);
        Assert.DoesNotContain(Check(proposal), x => x.Code == "car-jockey-must-be-only-activity");
        rows.Add(new JsonObject { ["id"] = Guid.NewGuid(), ["code"] = Reference("mtOccupations", 8), ["turnoverBasisPoints"] = 100 });
        var before = proposal.ToJsonString();
        Assert.Contains(Check(proposal), x => x.Code == "car-jockey-must-be-only-activity" && x.Path == "/risk/business/activities/0/code");
        Assert.Equal(before, proposal.ToJsonString()); rows[0]!["code"]!["version"] = "old";
        Assert.DoesNotContain(Check(proposal), x => x.Code == "car-jockey-must-be-only-activity");
    }

    [Fact]
    public void BusinessChronologyUsesCapturedPolicyDateAndDoesNotInventMissingDates()
    {
        var proposal = Example(); proposal["risk"]!["business"]!["startedOn"] = "2026-09-16";
        Assert.Contains(Check(proposal), x => x.Code == "business-start-after-policy");
        proposal["risk"]!["business"]!["startedOn"] = "2026-09-15"; Assert.DoesNotContain(Check(proposal), x => x.Code == "business-start-after-policy");
        proposal["termIntent"]!.AsObject().Remove("localStartDate"); Assert.DoesNotContain(Check(proposal), x => x.Code == "business-start-after-policy");
    }

    [Theory]
    [InlineData("postcode", 10)]
    [InlineData("houseNumber", 50)]
    [InlineData("street", 50)]
    [InlineData("town", 50)]
    [InlineData("city", 50)]
    [InlineData("county", 50)]
    public void SourceAddressBoundsApplyToProposerAndEachPremise(string field, int maximum)
    {
        var proposal = Example(); proposal["insured"]!["address"]![field] = new string('a', maximum);
        Assert.DoesNotContain(Check(proposal), x => x.Code == "source-text-too-long");
        proposal["insured"]!["address"]![field] = new string('a', maximum + 1);
        proposal["risk"]!["premises"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid(), ["address"] = new JsonObject { [field] = new string('a', maximum + 1) } });
        var issues = Check(proposal);
        Assert.Contains(issues, x => x.Code == "source-text-too-long" && x.Path == "/insured/address/" + field);
        Assert.Contains(issues, x => x.Code == "source-text-too-long" && x.Path == "/risk/premises/0/address/" + field);
    }

    [Fact]
    public void MaterialFactsAndAssociationTextUseTheirDistinctSourceBounds()
    {
        var proposal = Example(); var answers = proposal["risk"]!["business"]!["responses"]!["answers"]!.AsArray();
        answers.Add(new JsonObject { ["questionId"] = "MTS-03-Q07", ["kind"] = "text", ["value"] = new string('a', 20) });
        proposal["risk"]!["materialFacts"] = new string('a', 1000);
        Assert.DoesNotContain(Check(proposal), x => x.Code is "source-text-too-long" or "material-facts-too-long");
        answers.Last()!["value"] = new string('a', 21); proposal["risk"]!["materialFacts"] = new string('a', 1001);
        Assert.Contains(Check(proposal), x => x.Code == "source-text-too-long" && x.Path == $"/risk/business/responses/answers/{answers.Count - 1}/value");
        Assert.Contains(Check(proposal), x => x.Code == "material-facts-too-long" && x.Path == "/risk/materialFacts");
    }
}
