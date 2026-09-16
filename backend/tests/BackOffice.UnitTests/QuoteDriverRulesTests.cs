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
    { using var document = JsonDocument.Parse(proposal.ToJsonString()); return QuoteDriverRules.Assess(document.RootElement, new DateOnly(2026,9,15)); }
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
    public void ComposedReadinessIncludesDriverQuestionIdentityAndRemainsIncomplete()
    {
        var proposal = Example(); Answer(proposal["risk"]!,1)["value"] = Reference("driverPlans",3);
        using var document = JsonDocument.Parse(proposal.ToJsonString());
        var result = QuoteReadiness.Assess(Guid.NewGuid(),Guid.NewGuid(),document.RootElement,QuoteTerm.Assess(),null, new DateOnly(2026,9,15));
        Assert.Contains(result.Issues,issue => issue.Code == "any-driver-answer-required" && issue.QuestionId == "MTS-06-Q02");
        Assert.Contains(result.Issues,issue => issue.Category == "evidence");
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

    private static void SetAnswer(JsonNode driver,string id,JsonNode value)
    {
        var answers = driver["responses"]!["answers"]!.AsArray();
        var entry = answers.FirstOrDefault(row => row!["questionId"]!.GetValue<string>() == id);
        if (entry is null) answers.Add(new JsonObject { ["questionId"] = id, ["value"] = value });
        else entry["value"] = value;
    }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void RelationshipsFollowEveryPinnedCompanyAllowlist(string product)
    {
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var collections = JsonNode.Parse(stream)!["collections"]!;
        foreach (var company in collections["companyTypes"]!.AsArray())
        foreach (var relationship in collections["driverRelationshipsPolicyHolder"]!.AsArray())
        {
            var proposal = Example(product); var value = relationship!["value"]!.GetValue<int>();
            proposal["insured"]!["declaredCompanyType"] = Reference("companyTypes",company!["value"]!.GetValue<int>());
            Driver(proposal)["relationship"] = Reference("driverRelationshipsPolicyHolder",value);
            Assert.Equal(!company["relationshipOptions"]!.AsArray().Any(item => item!.GetValue<int>() == value),Check(proposal).Any(issue => issue.Code == "driver-relationship-ineligible"));
        }
        var forged = Example(product); forged["insured"]!["declaredCompanyType"]!["label"] = "Forged";
        Assert.Contains(Check(forged),issue => issue.Code == "driver-relationship-context-required");
    }
    [Fact]
    public void SpouseThresholdAndDuplicatePolicyholderAreAssessedPerProposal()
    {
        var p = Example(); var d = Driver(p); p["termIntent"]!["localStartDate"] = "2026-01-01";
        d["relationship"] = Reference("driverRelationshipsPolicyHolder",5); d["dateOfBirth"] = "2001-01-02";
        Assert.Contains(Check(p),i => i.Code == "spouse-under-25"); d["dateOfBirth"] = "2001-01-01";
        Assert.DoesNotContain(Check(p),i => i.Code == "spouse-under-25");
        d["relationship"] = Reference("driverRelationshipsPolicyHolder",3); var second = d.DeepClone(); second["id"] = Guid.NewGuid();
        p["risk"]!["drivers"]!.AsArray().Add(second);
        Assert.Equal(2,Check(p).Count(i => i.Code == "duplicate-policyholder-driver"));
    }
    [Fact]
    public void PersonalCoverUsesTrustedUsageRelationshipAndAge()
    {
        var p = Example(); var d = Driver(p); p["termIntent"]!["localStartDate"] = "2026-01-01";
        d["usage"] = Reference("driverUsages",1); Answer(d,31)["value"] = true; Answer(d,32)["value"] = true;
        Assert.Contains(Check(p),i => i.Code == "personal-cover-motor-trade-only"); Assert.Contains(Check(p),i => i.Code == "other-cover-motor-trade-only");
        d["usage"] = Reference("driverUsages",2); d["dateOfBirth"] = "2005-01-02";
        Assert.Contains(Check(p),i => i.Code == "other-cover-under-21"); d["dateOfBirth"] = "2005-01-01";
        Assert.DoesNotContain(Check(p),i => i.Code == "other-cover-under-21");
        d["relationship"] = Reference("driverRelationshipsPolicyHolder",3); Answer(d,31)["value"] = false; Answer(d,32)["value"] = false;
        Assert.Contains(Check(p),i => i.Code == "personal-cover-required-for-relationship"); Assert.Contains(Check(p),i => i.Code == "other-cover-required-for-relationship");
        d["usage"]!["label"] = "Forged"; Assert.Contains(Check(p),i => i.Code == "driver-usage-context-required");
    }
    [Theory]
    [InlineData("1996-01-01","2024-01-01",null)]
    [InlineData("1996-01-02","2024-01-01","motorcycle-over-1000-ineligible")]
    [InlineData("1996-01-01","2024-01-02","motorcycle-over-1000-ineligible")]
    [InlineData("1996-01-01","2027-01-01","motorcycle-eligibility-context-required")]
    public void MotorcycleEligibilityUsesCompleteYears(string birth,string licence,string? expected)
    {
        var p = Example(); var d = Driver(p); p["termIntent"]!["localStartDate"] = "2026-01-01"; d["dateOfBirth"] = birth;
        Answer(d,25)["value"] = Reference("driverMotorcycleCovers",6); SetAnswer(d,"MTS-06-Q26",JsonValue.Create(licence)!);
        var issues = Check(p).Where(i => i.Code is "motorcycle-over-1000-ineligible" or "motorcycle-eligibility-context-required").ToArray();
        if (expected is null) Assert.Empty(issues); else Assert.Equal(expected,Assert.Single(issues).Code);
    }
    [Theory]
    [InlineData("2024-01-31","2024-02-28",true)]
    [InlineData("2024-01-31","2024-02-29",false)]
    [InlineData("2025-01-31","2025-02-28",false)]
    [InlineData("9999-12-31","9999-12-31",true)]
    public void BanExpiryClampsCalendarMonthsAndHandlesMaximumDate(string occurred,string start,bool active)
    {
        var p = Example(); p["termIntent"]!["localStartDate"] = start;
        Driver(p)["convictions"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid(), ["occurredOn"] = occurred, ["disqualified"] = true, ["banMonths"] = 1 });
        var before = p.ToJsonString(); Assert.Equal(active,Check(p).Any(i => i.Code == "driver-ban-active-at-policy-start")); Assert.Equal(before,p.ToJsonString());
        p["termIntent"]!.AsObject().Remove("localStartDate"); Assert.Contains(Check(p),i => i.Code == "ban-policy-start-required");
    }
    [Fact]
    public void NamesAreComparedWithoutSplittingOrMutatingCompoundNames()
    {
        var p = Example(); var d = Driver(p); d["firstName"] = "Alex Jane"; d["surname"] = "Example"; d["fullName"] = " alex   jane EXAMPLE ";
        var before = p.ToJsonString(); Assert.DoesNotContain(Check(p),i => i.Code == "conflicting-driver-name"); Assert.Equal(before,p.ToJsonString());
        d["surname"] = "Different"; Assert.Contains(Check(p),i => i.Code == "conflicting-driver-name" && i.Path == "/risk/drivers/0/fullName");
    }
    [Fact]
    public void OverlappingEmploymentDeclarationsRemainIndependentAndRetainInactiveDetails()
    {
        var p = Example(); var d = Driver(p); Answer(d,28)["value"] = Reference("driverTradeEmploymentBasises",1);
        SetAnswer(d,"prototype.adddriver.trade-employment",Reference("prototype.adddriver.trade-employment",2));
        Assert.Equal(2,Check(p).Count(i => i.Code == "conflicting-driver-employment")); Assert.Contains(Check(p),i => i.Code == "prototype-other-occupation-required");
        Answer(d,28)["value"] = Reference("driverTradeEmploymentBasises",2); SetAnswer(d,"prototype.adddriver.other-occupation",JsonValue.Create("Example occupation")!);
        Assert.DoesNotContain(Check(p),i => i.Code is "conflicting-driver-employment" or "prototype-other-occupation-required");
        Answer(d,28)["value"] = Reference("driverTradeEmploymentBasises",1); SetAnswer(d,"prototype.adddriver.trade-employment",Reference("prototype.adddriver.trade-employment",1));
        Assert.Contains(Check(p),i => i.Code == "inactive-prototype-other-occupation");
        Answer(d,28)["value"]!["version"] = "forged"; Assert.Contains(Check(p),i => i.Code == "driver-employment-context-required");
    }
    [Fact]
    public void DeclaredYearsNeedActualDatesAndCompleteAnniversaries()
    {
        var p = Example(); var d = Driver(p); p["termIntent"]!["localStartDate"] = "2026-01-01"; d["dateOfBirth"] = "2000-01-01";
        d["licence"]!["type"] = Reference("driverLicenceTypes",2); d["licence"]!["issuedOn"] = "2020-01-02"; Answer(d,21)["value"] = true;
        SetAnswer(d,"prototype.adddriver.residency-years",JsonValue.Create(26)!); SetAnswer(d,"prototype.adddriver.licence-years",JsonValue.Create(5)!);
        Assert.DoesNotContain(Check(p),i => i.Code is "driver-years-context-required" or "conflicting-driver-years");
        SetAnswer(d,"prototype.adddriver.licence-years",JsonValue.Create(6)!); Assert.Contains(Check(p),i => i.Code == "conflicting-driver-years");
        p["termIntent"]!["localStartDate"] = "2026-01-02"; Assert.DoesNotContain(Check(p),i => i.Code == "conflicting-driver-years");
        Answer(d,21)["value"] = false; Assert.Contains(Check(p),i => i.Code == "driver-years-context-required");
        SetAnswer(d,"MTS-06-Q22",JsonValue.Create("2000-01-01")!); Assert.DoesNotContain(Check(p),i => i.Code == "driver-years-context-required");
        d["licence"]!["type"] = Reference("driverLicenceTypes",4); Assert.Contains(Check(p),i => i.Code == "driver-years-context-required");
    }
}
