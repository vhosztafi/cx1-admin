using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteDynamicOptionsTests
{
    private static JsonNode Reference(string collection,int value)
    {
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var catalogue = JsonNode.Parse(stream)!;
        return new JsonObject { ["collection"] = collection, ["version"] = QuoteCatalogueIdentity.Version, ["value"] = value,
            ["label"] = catalogue["collections"]![collection]!.AsArray().First(row => row!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }
    private static JsonNode Answer(string id,JsonNode value,string kind = "reference") => new JsonObject { ["questionId"] = id, ["kind"] = kind, ["value"] = value };
    private static JsonNode Proposal(int own = 5) => new JsonObject {
        ["productCode"] = "motor-trade-road-risks", ["termIntent"] = new JsonObject { ["localStartDate"] = "2026-01-01" },
        ["cover"] = new JsonObject { ["responses"] = new JsonObject { ["answers"] = new JsonArray(Answer("MTS-05-Q01",Reference("coverLevels",1)),Answer("MTS-05-Q02",Reference("indemnityOwnVehicles",own))) } },
        ["risk"] = new JsonObject { ["business"] = new JsonObject { ["activities"] = new JsonArray(new JsonObject { ["code"] = Reference("mtOccupations",5) }) }, ["drivers"] = new JsonArray() }
    };
    private static JsonArray Cover(JsonNode p) => p["cover"]!["responses"]!["answers"]!.AsArray();
    private static JsonNode AddDriver(JsonNode p,string birth,string issued = "2025-01-01")
    {
        var driver = new JsonObject { ["dateOfBirth"] = birth, ["licence"] = new JsonObject { ["issuedOn"] = issued }, ["responses"] = new JsonObject { ["answers"] = new JsonArray() } };
        p["risk"]!["drivers"]!.AsArray().Add(driver); return driver;
    }
    private static JsonArray Answers(JsonNode driver) => driver["responses"]!["answers"]!.AsArray();
    private static QuoteDynamicOptionsResult Check(JsonNode p,bool required = true)
    { using var document = JsonDocument.Parse(p.ToJsonString()); return QuoteDriverRules.AssessDynamic(document.RootElement,required); }
    [Fact]
    public void YoungDriverRequiredAnswersRetainFalseAndBecomeInactiveWhenCoverChanges()
    {
        var p = Proposal(); var d = AddDriver(p,"2008-01-01"); Assert.Empty(Check(p,false).Issues);
        Assert.Equal(new[] { "MTS-06-Q58","MTS-06-Q59","MTS-06-Q60" },Check(p).Issues.Select(i => i.QuestionId));
        Answers(d).Add(Answer("MTS-06-Q58",JsonValue.Create(false)!,"boolean"));
        Answers(d).Add(Answer("MTS-06-Q59",Reference("youngDriverConfiguration/0/indemnities",1)));
        Answers(d).Add(Answer("MTS-06-Q60",Reference("youngDriverConfiguration/0/cCs",1)));
        var before = p.ToJsonString(); Assert.Empty(Check(p).Issues); Assert.Equal(before,p.ToJsonString());
        Cover(p)[0]!["value"] = Reference("coverLevels",3); Assert.Contains(Check(p).Issues,i => i.Code == "inactive-driver-option-retained");
        Assert.Equal(3,Answers(d).Count);
    }
    [Fact]
    public void ExperienceUsesExactLicenceAnniversaryAndEligibleExcessValues()
    {
        var p = Proposal(); var d = AddDriver(p,"1990-01-01","2025-01-02");
        Assert.Equal(new[] { "MTS-06-Q61","MTS-06-Q62" },Check(p).Issues.Select(i => i.QuestionId));
        Answers(d).Add(Answer("MTS-06-Q61",Reference("driverExperienceBasedExcesses",1)));
        Answers(d).Add(Answer("MTS-06-Q62",Reference("driverExperienceBasedVehicleCCLimits",1))); Assert.Empty(Check(p).Issues);
        Answers(d)[0]!["value"] = Reference("driverExperienceBasedExcesses",2);
        Assert.Contains(Check(p).Issues,i => i.Code == "experience-excess-exceeds-policy-limit");
        d["licence"]!["issuedOn"] = "2025-01-01"; Assert.Equal(2,Check(p).Issues.Count(i => i.Code == "inactive-driver-option-retained"));
    }
    [Fact]
    public void UnknownDependencyAndEmptyEligibleFamilyRemainDistinct()
    {
        var p = Proposal(2); var d = AddDriver(p,"2008-01-01");
        Assert.Equal(new[] { "MTS-06-Q58","MTS-06-Q60" },Check(p).Issues.Select(i => i.QuestionId));
        p["risk"]!["business"]!["activities"] = new JsonArray(); Assert.Contains(Check(p).Issues,i => i.Code == "driver-option-context-required");
        d.AsObject().Remove("dateOfBirth"); Assert.Contains(Check(p).Issues,i => i.QuestionId == "MTS-06-Q60" && i.Code == "driver-option-context-required");
    }
    [Theory]
    [InlineData("2008-01-01",0,1)]
    [InlineData("2006-01-01",1,2)]
    [InlineData("2004-01-01",2,3)]
    [InlineData("2002-01-01",3,4)]
    public void EachDriverUsesTheirOwnAgeBandAndRetainedChoicesExpireAt25(string birth,int band,int value)
    {
        var p = Proposal(); var d = AddDriver(p,birth); var collection = $"youngDriverConfiguration/{band}/cCs";
        Answers(d).Add(Answer("MTS-06-Q60",Reference(collection,value)));
        var result = Check(p,false); Assert.Empty(result.Issues); Assert.Equal(collection,Assert.Single(result.SelectedCollections["/risk/drivers/0/responses/answers/0/value"]));
        d["dateOfBirth"] = "2001-01-01"; Assert.Contains(Check(p,false).Issues,i => i.Code == "inactive-dynamic-answer");
    }
    [Fact]
    public void ReferenceFromAnotherValidCandidateBandCannotPassContextAssessment()
    {
        var p = Proposal(); var d = AddDriver(p,"2008-01-01");
        Answers(d).Add(Answer("MTS-06-Q60",Reference("youngDriverConfiguration/1/cCs",2)));
        Assert.Contains(Check(p,false).Issues,i => i.Code == "reference-collection-mismatch" && i.QuestionId == "MTS-06-Q60");
    }
    [Fact]
    public void YoungIndemnityNeedsCompleteTrustedActivitiesAndRespectsPolicyLimits()
    {
        var p = Proposal(2); var d = AddDriver(p,"2008-01-01"); Answers(d).Add(Answer("MTS-06-Q59",Reference("youngDriverConfiguration/0/indemnities",1)));
        Assert.Contains(Check(p,false).Issues,i => i.Code == "indemnity-exceeds-policy-limit");
        Cover(p)[1]!["value"] = Reference("indemnityOwnVehicles",5); Assert.Empty(Check(p,false).Issues);
        p["risk"]!["business"]!["activities"] = new JsonArray(); Assert.Contains(Check(p,false).Issues,i => i.Code == "missing-dynamic-dependency");
    }
    [Fact]
    public void CoverFactsResolveEquivalentPrototypeIdsButRejectConflictsAndUnsupportedOptions()
    {
        var p = Proposal(); Cover(p).Clear();
        Cover(p).Add(Answer("prototype.quote.2beaf3b8d546",Reference("prototype.quote.2beaf3b8d546",1)));
        Cover(p).Add(Answer("prototype.quote.d9dd069a314c",Reference("prototype.quote.d9dd069a314c",2)));
        Cover(p).Add(Answer("MTS-05-Q04",Reference("indemnityOwnVehicles/number:5/excesses",4)));
        var before = p.ToJsonString(); var result = Check(p,false); Assert.Empty(result.Issues); Assert.Equal(before,p.ToJsonString());
        Assert.Equal("indemnityOwnVehicles/number:5/excesses",Assert.Single(result.SelectedCollections["/cover/responses/answers/2/value"]));
        Cover(p).Add(Answer("MTS-05-Q02",Reference("indemnityOwnVehicles",2))); result = Check(p,false);
        Assert.Equal(2,result.Issues.Count(i => i.Code == "conflicting-cover-declarations")); Assert.Contains(result.Issues,i => i.Code == "missing-dynamic-dependency"); Assert.Empty(result.SelectedCollections);
        p = Proposal(2); Cover(p).Add(Answer("prototype.quote.b4c7e25f7781",Reference("prototype.quote.b4c7e25f7781",1)));
        Cover(p).Add(Answer("prototype.quote.00216de47ab5",Reference("prototype.quote.00216de47ab5",4)));
        Assert.Equal(2,Check(p,false).Issues.Count(i => i.Code == "unsupported-cover-configuration"));
    }

    private static JsonNode NumericReference(string collection, Func<decimal,bool> predicate)
    {
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var rows = JsonNode.Parse(stream)!["collections"]![collection]!.AsArray();
        return Reference(collection,rows.First(row => row!["numericValue"] is not null && predicate(row["numericValue"]!.GetValue<decimal>()))!["value"]!.GetValue<int>());
    }
    [Fact]
    public void CustomerLimitsAffectDriverOptionsOnlyForAnEligibleDeclaredActivity()
    {
        var p = Proposal(2); var d = AddDriver(p,"2008-01-01");
        Cover(p).Add(Answer("MTS-05-Q03",NumericReference("indemnityCustomerVehicles",value => value == 15000)));
        Answers(d).Add(Answer("MTS-06-Q59",Reference("youngDriverConfiguration/0/indemnities",2)));
        Assert.Contains(Check(p,false).Issues,i => i.Code == "indemnity-exceeds-policy-limit");
        p["risk"]!["business"]!["activities"]![0]!["code"] = Reference("mtOccupations",1); Assert.Empty(Check(p,false).Issues);
        Cover(p)[2]!["value"]!["label"] = "Forged";
        Assert.Contains(Check(p,false).Issues,i => i.Code == "missing-dynamic-dependency");
    }
    [Fact]
    public void FireTheftCapAndThirdPartyOnlyCoverCannotBeBypassedByACatalogueValidExcess()
    {
        var p = Proposal(); Cover(p)[0]!["value"] = Reference("coverLevels",2);
        var own = NumericReference("indemnityOwnVehicles",value => value > 15000); Cover(p)[1]!["value"] = own;
        var collection = $"indemnityOwnVehicles/number:{own["value"]!.GetValue<int>()}/excesses";
        using var stream = typeof(QuoteDriverRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!;
        var value = JsonNode.Parse(stream)!["collections"]![collection]![0]!["value"]!.GetValue<int>();
        Cover(p).Add(Answer("MTS-05-Q04",Reference(collection,value)));
        Assert.Contains(Check(p,false).Issues,i => i.Code == "missing-dynamic-dependency");
        Cover(p)[0]!["value"] = Reference("coverLevels",3); Assert.Contains(Check(p,false).Issues,i => i.Code == "inactive-dynamic-answer");
    }
}
