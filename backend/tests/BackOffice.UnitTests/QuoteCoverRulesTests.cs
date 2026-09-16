using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteCoverRulesTests
{
    private static JsonNode Example(string product = "motor-trade-road-risks")
    { using var stream = typeof(QuoteCoverRulesTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!; return JsonNode.Parse(stream)!["proposal"]!; }
    private static JsonNode Reference(string collection,int value)
    {
        using var stream = typeof(QuoteCoverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!; var catalogue = JsonNode.Parse(stream)!;
        return new JsonObject { ["collection"] = collection,["version"] = QuoteCatalogueIdentity.Version,["value"] = value,["label"] = catalogue["collections"]![collection]!.AsArray().First(row => row!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }
    private static void Set(JsonNode holder,string id,JsonNode value,string kind = "reference")
    {
        var rows = holder["responses"]!["answers"]!.AsArray(); var row = rows.FirstOrDefault(item => item!["questionId"]!.GetValue<string>() == id);
        if (row is null) rows.Add(new JsonObject { ["questionId"] = id,["kind"] = kind,["value"] = value }); else row["value"] = value;
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode p) { using var doc = JsonDocument.Parse(p.ToJsonString()); return QuoteCoverRules.Assess(doc.RootElement); }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void BothProductSourceFixturesPassCoverAndExtras(string product) => Assert.Empty(Check(Example(product)));
    [Fact]
    public void ThirdPartyOnlyRetainsInactiveLimitsRatherThanDeletingThem()
    {
        var p = Example(); Set(p["cover"]!,"MTS-05-Q01",Reference("coverLevels",3)); var before = p.ToJsonString(); var issues = Check(p);
        Assert.Contains(issues,i => i.Code == "inactive-cover-answer-retained" && i.QuestionId == "MTS-05-Q02"); Assert.Equal(before,p.ToJsonString());
    }
    [Fact]
    public void CustomerLoanVehiclesRequireTheCoverDeclarationAndEligibleLevel()
    {
        var p = Example(); p["risk"]!["vehicles"]![0]!["customerLoan"] = true;
        Assert.Contains(Check(p),i => i.Code == "customer-loan-cover-required" && i.Path == "/risk/vehicles/0/customerLoan");
        Set(p["cover"]!,"MTS-05-Q08",JsonValue.Create(true)!,"boolean"); Assert.Contains(Check(p),i => i.Code == "cover-answer-required" && i.QuestionId == "MTS-05-Q09");
    }
    [Fact]
    public void UnsupportedFixedExcessAnswersAreNotTreatedAsSupportedProducts()
    {
        var p = Example(); Set(p["cover"]!,"MTS-05-Q05",JsonValue.Create(true)!,"boolean"); Assert.Contains(Check(p),i => i.Code == "unsupported-all-sections-excess");
        p["risk"]!["business"]!["activities"] = new JsonArray(); Assert.Contains(Check(p),i => i.Code == "cover-activity-context-required");
    }
    [Fact]
    public void DemoAndPrivateUseDeclarationsAreIndependentAndReconciled()
    {
        var p = Example(); Set(p["cover"]!,"prototype.quote.becc2653d0de",JsonValue.Create(true)!,"boolean"); Set(p["cover"]!,"MTS-11-Q01",JsonValue.Create(false)!,"boolean");
        Assert.Equal(2,Check(p).Count(i => i.Code == "conflicting-demonstration-cover"));
        Set(p["cover"]!,"prototype.quote.4c5df77ffba1",JsonValue.Create(true)!,"boolean"); Assert.Contains(Check(p),i => i.Code == "private-use-driver-required");
    }
    [Fact]
    public void EuropeanTripDatesUseActualShortTermAndReferencedDrivers()
    {
        var p = Example(); p["termIntent"] = new JsonObject { ["kind"] = "short-period",["localStartDate"] = "2026-10-01",["localStartTime"] = "12:00",["localEndDate"] = "2026-10-31",["localEndTime"] = "12:00",["timeZone"] = "Europe/London" };
        Set(p["cover"]!,"MTS-11-Q13",JsonValue.Create(true)!,"boolean");
        p["cover"]!["temporaryEuropeanCover"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(),["registration"] = "AA11AAA",["startsOn"] = "2026-09-30",["endsOn"] = "2026-11-01" });
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "european-trip-before-policy"); Assert.Contains(issues,i => i.Code == "european-trip-after-policy"); Assert.Contains(issues,i => i.Code == "european-trip-drivers-required");
        var trip = p["cover"]!["temporaryEuropeanCover"]![0]!; trip["startsOn"] = "2026-10-10"; trip["endsOn"] = "2026-10-10"; Assert.Contains(Check(p),i => i.Code == "european-trip-end-must-follow-start");
        p["termIntent"]!["localEndDate"] = "2026-09-01"; Assert.Contains(Check(p),i => i.Code == "european-trip-term-required");
    }
    [Fact]
    public void AnnualRowsRetainParentContradictionsAndNormalizedDuplicates()
    {
        var p = Example(); Set(p["cover"]!,"MTS-11-Q10",JsonValue.Create(false)!,"boolean"); p["cover"]!["annualEuropeanCover"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(),["registration"] = "AB12 CDE" },new JsonObject { ["id"] = Guid.NewGuid().ToString(),["registration"] = "AB12CDE" });
        var before = p.ToJsonString(); var issues = Check(p); Assert.Contains(issues,i => i.Code == "inactive-european-cover-retained"); Assert.Contains(issues,i => i.Code == "duplicate-registration" && i.Path == "/cover/annualEuropeanCover/1/registration"); Assert.Equal(before,p.ToJsonString());
        Set(p["cover"]!,"MTS-11-Q10",JsonValue.Create(true)!,"boolean"); p["cover"]!["annualEuropeanCover"] = new JsonArray(); Assert.Contains(Check(p),i => i.Code == "european-cover-row-required");
    }
    [Fact]
    public void PremisesPrototypeReferencesAndAnswersOnlyApplyToCombined()
    {
        var p = Example("motor-trade-combined"); p["risk"]!["premises"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString() });
        using var doc = JsonDocument.Parse(p.ToJsonString()); var issues = QuoteAdditionalRules.Assess(doc.RootElement);
        Assert.Equal(2,issues.Count(i => i.Code == "required-prototype-reference")); Assert.Equal(2,issues.Count(i => i.Code == "required-prototype-detail-answer"));
        p["productCode"] = "motor-trade-road-risks"; p["risk"]!["premises"]![0]!["security"] = Reference("prototype.premises-security",1);
        using var road = JsonDocument.Parse(p.ToJsonString()); Assert.Contains(QuoteAdditionalRules.Assess(road.RootElement),i => i.Code == "inapplicable-prototype-reference");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SocialEuropeanTripsRespectTheSelectedDriverPlan(int plan)
    {
        var p = Example(); Set(p["risk"]!, "MTS-06-Q01", Reference("driverPlans", plan));
        p["risk"]!["drivers"]![0]!["usage"] = Reference("driverUsages", 1);
        p["cover"]!["temporaryEuropeanCover"] = new JsonArray(new JsonObject {
            ["id"] = Guid.NewGuid().ToString(), ["registration"] = "AA11AAA",
            ["startsOn"] = "2026-10-01", ["endsOn"] = "2026-10-08",
            ["usage"] = Reference("europeanTripUsage", 2),
            ["driverIds"] = new JsonArray(p["risk"]!["drivers"]![0]!["id"]!.DeepClone()) });
        Assert.Contains(Check(p), x => x.Code == "european-trip-usage-ineligible");
        if (plan == 1) {
            p["risk"]!["drivers"]![0]!["usage"] = Reference("driverUsages", 2);
            Assert.DoesNotContain(Check(p), x => x.Code == "european-trip-usage-ineligible");
        }
    }
    [Fact]
    public void ComprehensiveTripCoverCannotExceedMainCover()
    {
        var p = Example(); Set(p["cover"]!, "MTS-05-Q01", Reference("coverLevels", 3));
        p["cover"]!["temporaryEuropeanCover"] = new JsonArray(new JsonObject {
            ["id"] = Guid.NewGuid().ToString(), ["cover"] = Reference("europeanTripCover", 2) });
        Assert.Contains(Check(p), x => x.Code == "extras-comprehensive-cover-required" && x.Path == "/cover/temporaryEuropeanCover/0/cover");
    }
    [Fact]
    public void CarJockeyActivityRequiresItsOwnRadiusDeclaration()
    {
        var p = Example(); p["risk"]!["business"]!["activities"]![0]!["code"] = Reference("mtOccupations", 9);
        using var doc = JsonDocument.Parse(p.ToJsonString());
        Assert.Contains(QuoteAdditionalRules.Assess(doc.RootElement), x => x.Code == "car-jockey-radius-required" && x.QuestionId == "MTS-13-Q02");
    }
}
