using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialReferralTests
{
    private static JsonObject Proposal() => JsonNode.Parse(typeof(CommercialReferralTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!)!.AsObject();
    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static void Answer(JsonObject proposal, string id, object value)
    {
        var answers = proposal["risk"]!["declarations"]!["answers"]!.AsArray();
        answers.Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote." + id)!["value"] = JsonSerializer.SerializeToNode(value);
    }

    [Theory]
    [InlineData("a6ff9fdbe769", true, "PR-05", "internal-review")]
    [InlineData("755d136885fb", true, "PR-17", "outside-appetite")]
    [InlineData("cae14bccab65", true, "PR-18", "outside-appetite")]
    [InlineData("ec38529cf3bd", true, "PR-21", "internal-review")]
    [InlineData("8cc83c3a006f", true, "LI-03", "internal-review")]
    [InlineData("185df2844c76", true, "LI-05", "internal-review")]
    [InlineData("ed8870d17328", true, "LI-08", "outside-appetite")]
    [InlineData("c329b4c60f47", false, "LI-11", "internal-review")]
    [InlineData("f538427148cf", false, "LI-15", "internal-review")]
    [InlineData("8581de6c0b2a", true, "LI-18", "internal-review")]
    [InlineData("eb57b4a6cc8b", true, "UW-20", "internal-review")]
    [InlineData("cfb8d4a5494b", true, "UW-18", "internal-review")]
    public void SourceBooleanBranchesHaveExplicitDispositions(string question, bool value, string rule, string disposition)
    {
        var proposal = Proposal(); proposal["risk"]!["liability"]!["maximumHeightMetres"] = 2;
        Answer(proposal, question, value);
        Assert.Contains(CommercialReferralRules.SourceReferrals(Element(proposal)), x => x.RuleCode == rule && x.Disposition == disposition);
        Answer(proposal, question, !value);
        Assert.DoesNotContain(CommercialReferralRules.SourceReferrals(Element(proposal)), x => x.RuleCode == rule);
    }

    [Theory]
    [InlineData("2000000.00", false, false)]
    [InlineData("2000000.01", true, false)]
    [InlineData("2500000.00", true, false)]
    [InlineData("2500000.01", true, true)]
    public void PropertyBoundariesUseLargestLocationSumEvenWhenSuppliedMelIsLower(string sum, bool mel, bool location)
    {
        var proposal = Proposal();
        foreach (var row in proposal["risk"]!["locations"]!.AsArray())
        { row!["buildings"] = sum; row["contents"] = "0.00"; row["stock"] = "0.00"; row["maximumEstimatedLoss"] = "1.00"; }
        var result = CommercialReferralRules.SourceReferrals(Element(proposal));
        Assert.Equal(mel, result.Any(x => x.RuleCode == "AU-06"));
        Assert.Equal(location, result.Any(x => x.RuleCode == "AU-05"));
        Assert.All(result.Where(x => x.RuleCode.StartsWith("AU-")), x => Assert.Equal("carrier-required", x.Disposition));
    }

    [Fact]
    public void MotorTradeCannotEnterCommercialRules()
    {
        var proposal = Proposal(); proposal["productCode"] = "motor-trade-combined";
        Assert.Throws<ArgumentException>(() => CommercialReferralRules.SourceReferrals(Element(proposal)));
    }

    [Fact]
    public void EvidenceHasOnlyCurrentCommercialSubjectsAndRejectsForeignDeletedAndMotorTargets()
    {
        var proposal = Proposal(); var location = proposal["risk"]!["locations"]![0]!["id"]!.GetValue<string>();
        var condition = JsonNode.Parse("{\"code\":\"provide-cc-location-proof\",\"riskItemId\":\"" + location + "\"}")!;
        Assert.Equal(Guid.Parse(location), Assert.Single(ReferralRules.Condition(Element(condition), Element(proposal)).TargetIds));
        proposal["risk"]!["locations"]!.AsArray().RemoveAt(0);
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Element(condition), Element(proposal)));
        condition["riskItemId"] = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Element(condition), Element(proposal)));
        condition["code"] = "provide-driver-proof";
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(Element(condition), Element(proposal)));
        Assert.All(CommercialEvidenceRules.Requirements(Element(proposal)), x => Assert.StartsWith("cc-", x.Code));
    }

    [Fact]
    public void AuthorityUsesPublishedCommercialLimitsAndCannotApproveOutsideAppetite()
    {
        var definitions = JsonNode.Parse(typeof(CommercialReferralTests).Assembly.GetManifestResourceStream("CommercialExamples.Underwriting")!)!;
        var proposal = Proposal();
        Answer(proposal, "755d136885fb", true);
        Assert.Contains(CommercialReferralRules.AssessAuthority(Element(definitions["authority"]!), Element(proposal), 1000), x => x.RuleCode == "PR-17");
        definitions["authority"]!["limits"]!["annualPremium"] = "500.00";
        Assert.Contains(CommercialReferralRules.AssessAuthority(Element(definitions["authority"]!), Element(proposal), 500.01m), x => x.RuleCode == "CC-premium-limit");
        Assert.DoesNotContain(CommercialReferralRules.AssessAuthority(Element(definitions["authority"]!), Element(proposal), 500m), x => x.RuleCode == "CC-premium-limit");
    }

    [Theory]
    [InlineData("composite-panels", 2, "PR-11")]
    [InlineData("composite-panels", 3, "PR-11")]
    [InlineData("heritage-listed", 2, "PR-14")]
    [InlineData("heritage-listed", 3, "PR-14")]
    [InlineData("heritage-listed", 4, "PR-14")]
    public void ConstructionReferralsRetainEachLocationIdentity(string field, int answer, string rule)
    {
        var proposal = Proposal(); var location = proposal["risk"]!["locations"]![0]!;
        location["responses"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.addloc." + field)!["value"]!["value"] = answer;
        Assert.Contains(CommercialReferralRules.SourceReferrals(Element(proposal)), x => x.RuleCode == rule && x.TargetId == Guid.Parse(location["id"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void LossCountBoundaryIsStrictlyMoreThanTwo(int count, bool referred)
    {
        var proposal = Proposal(); var losses = proposal["risk"]!["losses"]!.AsArray();
        var loss = JsonNode.Parse("{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"occurredOn\":\"2025-01-01\",\"description\":\"Fictional loss\",\"amount\":\"1.00\",\"paid\":\"1.00\",\"reserve\":\"0.00\",\"status\":\"settled\"}")!; losses.Clear();
        for (var i = 0; i < count; i++) { var row = loss.DeepClone(); row["id"] = Guid.NewGuid(); losses.Add(row); }
        Assert.Equal(referred, CommercialReferralRules.SourceReferrals(Element(proposal)).Any(x => x.RuleCode == "UW-30"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void FloodHistoryUsesSourceIdentity(int answer, bool refer)
    {
        var proposal = Proposal();
        proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote.ade0f3f0df5e")!["value"]!["value"] = answer;
        Assert.Equal(refer, CommercialReferralRules.SourceReferrals(Element(proposal)).Any(x => x.RuleCode == "PR-04"));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void MovementSignsAreOutsideAppetiteOnlyWhenSubsidenceIsRequested(bool selected, bool signs, bool outside)
    {
        var proposal = Proposal(); Answer(proposal, "be47c08f530f", selected);
        var answers = proposal["risk"]!["declarations"]!["answers"]!.AsArray();
        foreach (var id in new[] { "aaccb5c97c33", "8293bc041f81", "02d9c6be528a", "a6a4579c177e", "7a732bc99a5f", "fcc7e22c33ea", "923e600eb3a4" })
        {
            var answer = answers.SingleOrDefault(x => x!["questionId"]!.GetValue<string>() == "prototype.quote." + id);
            if (answer is null) answers.Add(new JsonObject { ["questionId"] = "prototype.quote." + id, ["kind"] = "boolean", ["value"] = id == "aaccb5c97c33" && signs });
            else answer["value"] = id == "aaccb5c97c33" && signs;
        }
        Assert.Equal(outside, CommercialReferralRules.SourceReferrals(Element(proposal)).Any(x => x.RuleCode == "PR-08" && x.Disposition == "outside-appetite"));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10000, true)]
    public void UsaCanadaPercentageUsesBasisPoints(int bps, bool refer)
    {
        var proposal = Proposal();
        proposal["risk"]!["business"]!["responses"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote-value.c2b0372b4040")!["value"] = bps;
        Assert.Equal(refer, CommercialReferralRules.SourceReferrals(Element(proposal)).Any(x => x.RuleCode == "LI-12"));
    }

    [Fact]
    public void TimberConstructionCannotBeSuppressedByContradictoryTimberAnswer()
    {
        var proposal = Proposal(); var location = proposal["risk"]!["locations"]![0]!;
        var answers = location["responses"]!["answers"]!.AsArray();
        answers.Single(x => x!["questionId"]!.GetValue<string>() == "prototype.addloc.timber-frame")!["value"] = false;
        answers.Single(x => x!["questionId"]!.GetValue<string>() == "prototype.addloc.wall-construction")!["value"]!["value"] = 5;
        Assert.Contains(CommercialReferralRules.SourceReferrals(Element(proposal)), x => x.RuleCode == "PR-12" && x.TargetId == Guid.Parse(location["id"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimberAnswerAndNonReferredConstructionHaveExactLocationScope(bool timber)
    {
        var proposal = Proposal(); var location = proposal["risk"]!["locations"]![0]!; var id = Guid.Parse(location["id"]!.GetValue<string>());
        var answers = location["responses"]!["answers"]!.AsArray();
        foreach (var field in new[] { "wall-construction", "composite-panels", "heritage-listed" })
            answers.Single(x => x!["questionId"]!.GetValue<string>() == "prototype.addloc." + field)!["value"]!["value"] = 1;
        answers.Single(x => x!["questionId"]!.GetValue<string>() == "prototype.addloc.timber-frame")!["value"] = timber;
        var result = CommercialReferralRules.SourceReferrals(Element(proposal)).Where(x => x.TargetId == id).ToArray();
        Assert.DoesNotContain(result, x => x.RuleCode is "PR-11" or "PR-14");
        Assert.Equal(timber, result.Any(x => x.RuleCode == "PR-12"));
    }

    [Fact]
    public void DisabledSectionsCannotRetainApplicableWageOrBiConditions()
    {
        var proposal = Proposal(); Answer(proposal, "36ef01068295", false);
        proposal["cover"]!["responses"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote.7660fc5eb42e")!["value"] = false;
        Assert.DoesNotContain(CommercialEvidenceRules.Requirements(Element(proposal)), x => x.Code is "cc-wage-proof" or "cc-bi-proof");
        Assert.Throws<ArgumentException>(() => ReferralRules.Condition(JsonSerializer.SerializeToElement(new { code = "provide-cc-bi-proof" }), Element(proposal)));
    }

    [Fact]
    public void SourceDocumentsHaveDistinctPurposesAndConditionalStructuralSubjects()
    {
        var proposal = Proposal(); Answer(proposal, "be47c08f530f", true);
        var proofs = CommercialEvidenceRules.Requirements(Element(proposal));
        Assert.Single(proofs, x => x.Code == "cc-claims-experience-proof");
        Assert.Single(proofs, x => x.Code == "cc-health-safety-proof");
        foreach (var code in new[] { "cc-electrical-proof", "cc-alarm-proof", "cc-structural-proof" })
        {
            Assert.Equal(2, proofs.Count(x => x.Code == code));
            foreach (var proof in proofs.Where(x => x.Code == code))
                Assert.Equal(code, ReferralRules.Condition(JsonSerializer.SerializeToElement(new { code = "provide-" + code, riskItemId = proof.RiskItemId }), Element(proposal)).RequirementCode);
        }
        Answer(proposal, "be47c08f530f", false);
        Assert.DoesNotContain(CommercialEvidenceRules.Requirements(Element(proposal)), x => x.Code == "cc-structural-proof");
    }
}
