using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteVehicleRulesTests
{
    private static JsonNode Example(string product = "motor-trade-road-risks")
    { using var stream = typeof(QuoteVehicleRulesTests).Assembly.GetManifestResourceStream($"QuoteExamples.quote-capture-{product}.json")!; return JsonNode.Parse(stream)!["proposal"]!; }
    private static JsonNode Vehicle(JsonNode p) => p["risk"]!["vehicles"]![0]!;
    private static JsonNode Reference(string collection,int value)
    {
        using var stream = typeof(QuoteVehicleRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!; var catalogue = JsonNode.Parse(stream)!;
        return new JsonObject { ["collection"] = collection,["version"] = QuoteCatalogueIdentity.Version,["value"] = value,["label"] = catalogue["collections"]![collection]!.AsArray().First(row => row!["value"]!.GetValue<int>() == value)!["text"]!.DeepClone() };
    }
    private static JsonNode Answer(JsonNode holder,string id) => holder["responses"]!["answers"]!.AsArray().First(row => row!["questionId"]!.GetValue<string>() == id)!;
    private static void SetAnswer(JsonNode holder,string id,JsonNode value,string kind = "reference")
    {
        var rows = holder["responses"]!["answers"]!.AsArray(); var row = rows.FirstOrDefault(item => item!["questionId"]!.GetValue<string>() == id);
        if (row is null) rows.Add(new JsonObject { ["questionId"] = id,["kind"] = kind,["value"] = value }); else row["value"] = value;
    }
    private static IReadOnlyList<QuoteFieldIssue> Check(JsonNode p,bool manual = true)
    {
        using var doc = JsonDocument.Parse(p.ToJsonString());
        var modes = manual ? p["risk"]!["vehicles"]!.AsArray().ToDictionary(v => Guid.Parse(v!["id"]!.GetValue<string>()),_ => "manual") : null;
        return QuoteVehicleRules.Assess(doc.RootElement,new DateOnly(2026,9,15),modes);
    }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void SourceBaselinePassesWithTrustedManualContext(string product) => Assert.Empty(Check(Example(product)));

    [Fact]
    public void MissingProviderContextCannotBeInventedByVehicleDeclarations()
    {
        var p = Example(); var issue = Assert.Single(Check(p,false)); Assert.Equal("vehicle-capture-context-required",issue.Code); Assert.Equal("/risk/vehicles/0",issue.Path);
        Vehicle(p).AsObject().Remove("make"); Assert.Contains(Check(p),i => i.Code == "required-vehicle-field" && i.Path.EndsWith("/make",StringComparison.Ordinal));
    }
    [Fact]
    public void OrdinaryAndSpecifiedVehiclesRetainDistinctValuePurchaseAndCustomerLoanRules()
    {
        var p = Example(); var vehicle = Vehicle(p); vehicle["purchasedOn"] = "2026-09-16"; vehicle["value"] = "49999.99"; vehicle.AsObject().Remove("customerLoan");
        Assert.Contains(Check(p),i => i.Code == "purchase-in-future"); Assert.Contains(Check(p),i => i.Path.EndsWith("/customerLoan",StringComparison.Ordinal));
        p["risk"]!["specifiedVehiclesRequested"] = true; p["risk"]!["specifiedVehicleIds"] = new JsonArray(vehicle["id"]!.DeepClone());
        var issues = Check(p); Assert.DoesNotContain(issues,i => i.Code == "purchase-in-future" || i.Path.EndsWith("/customerLoan",StringComparison.Ordinal)); Assert.Contains(issues,i => i.Code == "specified-vehicle-value-minimum");
        vehicle["value"] = "50000.00"; Assert.Empty(Check(p));
        p["risk"]!["specifiedVehiclesRequested"] = false; Assert.Contains(Check(p),i => i.Code == "inactive-specified-vehicles-retained"); Assert.Single(p["risk"]!["specifiedVehicleIds"]!.AsArray());
    }
    [Fact]
    public void NormalizedDuplicatesAndModificationLeaseContradictionsKeepTheirRowPaths()
    {
        var p = Example(); var first = Vehicle(p); var second = first.DeepClone(); second["id"] = Guid.NewGuid().ToString(); second["registration"] = first["registration"]!.GetValue<string>().Replace(" ",""); p["risk"]!["vehicles"]!.AsArray().Add(second);
        first["partOfLeaseAgreement"] = true; first.AsObject().Remove("leaseLengthYears");
        first["modified"] = true; first["modifications"] = new JsonArray();
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "duplicate-registration" && i.Path == "/risk/vehicles/1/registration"); Assert.Contains(issues,i => i.Path == "/risk/vehicles/0/leaseLengthYears"); Assert.Contains(issues,i => i.Code == "vehicle-modification-required");
        using var stream = typeof(QuoteVehicleRules).Assembly.GetManifestResourceStream("QuoteCapture.References")!; var catalogue = JsonNode.Parse(stream)!;
        var family = catalogue["bindings"]!.AsArray().First(row => row!["owner"]!.GetValue<string>() == "MTS-08-Q26")!["collections"]![0]!.GetValue<string>();
        var code = Reference(family,catalogue["collections"]![family]![0]!["value"]!.GetValue<int>());
        first["modifications"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(),["code"] = code },new JsonObject { ["id"] = Guid.NewGuid().ToString(),["code"] = code.DeepClone() });
        first["modified"] = false; var before = p.ToJsonString(); issues = Check(p);
        Assert.Contains(issues,i => i.Code == "inactive-vehicle-modifications-retained"); Assert.Contains(issues,i => i.Code == "duplicate-vehicle-modification" && i.Path == "/risk/vehicles/0/modifications/1/code"); Assert.Equal(before,p.ToJsonString());
    }
    [Fact]
    public void OvernightPostcodesMustBelongToTheCurrentProposalOrAnEligiblePersonalDriver()
    {
        var p = Example(); Vehicle(p)["keptOvernightAddress"] = "ZZ1 9ZZ"; Assert.Contains(Check(p),i => i.Code == "overnight-postcode-not-in-proposal");
        var driver = p["risk"]!["drivers"]![0]!; driver["address"]!["postcode"] = "ZZ1 9ZZ"; Assert.Contains(Check(p),i => i.Code == "overnight-postcode-not-in-proposal");
        Answer(driver,"MTS-06-Q31")["value"] = true; Vehicle(p)["keptOvernightAddress"] = "zz19zz"; Assert.DoesNotContain(Check(p),i => i.Code.StartsWith("overnight-",StringComparison.Ordinal));
        Vehicle(p)["keptOvernightAddress"] = " "; Assert.Contains(Check(p),i => i.Code == "overnight-postcode-required");
    }
    [Fact]
    public void TradePlatesKeepHeldInventorySeparateFromCoveredRowsAndCount()
    {
        var p = Example(); var risk = p["risk"]!;
        Answer(risk["business"]!,"prototype.quote-value.315960b57ab1")["value"] = true;
        Answer(risk,"MTS-07-Q01")["value"] = true;
        risk["responses"]!["answers"]!.AsArray().Add(new JsonObject { ["questionId"] = "MTS-07-Q02",["kind"] = "count",["value"] = 1 });
        risk["heldTradePlates"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(),["number"] = "123 AB" });
        risk["tradePlates"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(),["number"] = "123AB" }); Assert.Empty(Check(p));
        risk["tradePlates"]!.AsArray().Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(),["number"] = "999ZZ" });
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "covered-trade-plate-not-held"); Assert.Contains(issues,i => i.Code == "trade-plate-count-below-rows");
        Answer(risk,"MTS-07-Q01")["value"] = false; Assert.Contains(Check(p),i => i.Code == "inactive-trade-plate-data"); Assert.Equal(2,risk["tradePlates"]!.AsArray().Count);
    }
    [Fact]
    public void PortfolioSharesRequireExactTotalAndPreserveInactiveDetails()
    {
        var p = Example(); var risk = p["risk"]!; Answer(risk,"MTS-10-Q02")["value"] = 9999; Assert.Contains(Check(p),i => i.Code == "portfolio-total-must-equal-100-percent");
        Answer(risk,"MTS-10-Q02")["value"] = 50; Assert.Contains(Check(p),i => i.Code == "portfolio-minimum-one-percent" && i.QuestionId == "MTS-10-Q02");
        Answer(risk,"MTS-10-Q01")["value"] = false; Assert.Contains(Check(p),i => i.Code == "inactive-answer-retained" && i.QuestionId == "MTS-10-Q02");
        Assert.Contains(Check(p),i => i.Code == "vehicle-category-required");
    }
    [Fact]
    public void VehicleValueAndAbiLimitsUseCurrentDriverAndCoverContext()
    {
        var p = Example(); Vehicle(p)["value"] = "999999.99"; Vehicle(p)["abiGroup"] = 99;
        Assert.Contains(Check(p),i => i.Code == "vehicle-value-limit-exceeded"); Assert.Contains(Check(p),i => i.Code == "vehicle-abi-limit-exceeded");
        Answer(p["risk"]!["drivers"]![0]!,"MTS-06-Q27")["value"]!["label"] = "Forged"; Assert.Contains(Check(p),i => i.Code == "vehicle-abi-context-required");
        Answer(p["cover"]!,"MTS-05-Q01")["value"] = Reference("coverLevels",3); Assert.DoesNotContain(Check(p),i => i.Code.StartsWith("vehicle-value-",StringComparison.Ordinal));
    }
    [Fact]
    public void ManufacturerImportCannotSilentlySatisfyTheGreyImportDeclaration()
    {
        var p = Example(); Vehicle(p)["imported"] = true;
        var selected = Answer(Vehicle(p),"prototype.addveh.special-characteristics"); selected["value"] = Reference("prototype.addveh.special-characteristics",4);
        Assert.Contains(Check(p),i => i.Code == "vehicle-characteristic-declaration-required"); Assert.Contains(Check(p),i => i.Code == "portfolio-declaration-conflicts-with-vehicle" && i.QuestionId == "MTS-10-Q11");
        selected["value"] = Reference("prototype.addveh.special-characteristics",1); Assert.DoesNotContain(Check(p),i => i.Code == "vehicle-characteristic-declaration-required");
    }
    [Fact]
    public void CommercialWeightUsesKilogramsAndItsAbiExemptionDoesNotBypassWeight()
    {
        var p = Example(); var vehicle = Vehicle(p); vehicle["vehicleType"] = Reference("vehicleType",7); vehicle["grossWeightKg"] = 3501; vehicle["abiGroup"] = 99;
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "vehicle-gvw-limit-exceeded"); Assert.DoesNotContain(issues,i => i.Code == "vehicle-abi-limit-exceeded");
        Assert.Contains(issues,i => i.Code == "portfolio-declaration-conflicts-with-vehicle" && i.QuestionId == "MTS-10-Q05");
        vehicle["grossWeightKg"] = 3500; Assert.DoesNotContain(Check(p),i => i.Code == "vehicle-gvw-limit-exceeded");
        Answer(p["risk"]!["drivers"]![0]!,"MTS-06-Q20")["value"]!["version"] = "old"; Assert.Contains(Check(p),i => i.Code == "vehicle-gvw-context-required");
    }
    [Fact]
    public void MotorcycleLimitsApplyToBothPopulationsUnlessAnExplicitUnlimitedOptionExists()
    {
        var p = Example(); var risk = p["risk"]!; var driver = risk["drivers"]![0]!; var vehicle = Vehicle(p);
        vehicle["vehicleType"] = Reference("vehicleType",3); vehicle["declaredEngineSize"] = "501";
        SetAnswer(driver,"MTS-06-Q25",Reference("driverMotorcycleCovers",3)); Assert.Contains(Check(p),i => i.Code == "motorcycle-cc-limit-exceeded");
        vehicle["declaredEngineSize"] = "500"; Assert.DoesNotContain(Check(p),i => i.Code == "motorcycle-cc-limit-exceeded");
        SetAnswer(risk,"MTS-06-Q01",Reference("driverPlans",2)); SetAnswer(risk,"MTS-06-Q07",Reference("aadMaxMotorcycleCc",2));
        Assert.Contains(Check(p),i => i.Code == "motorcycle-cc-limit-exceeded");
        SetAnswer(risk,"MTS-06-Q07",Reference("aadMaxMotorcycleCc",6)); Assert.DoesNotContain(Check(p),i => i.Code == "motorcycle-cc-limit-exceeded");
        Answer(risk,"MTS-06-Q07")["value"]!["label"] = "Unlimited"; Assert.Contains(Check(p),i => i.Code == "vehicle-motorcycle-context-required");
        SetAnswer(risk,"MTS-06-Q01",Reference("driverPlans",1)); vehicle["declaredEngineSize"] = "500cc"; Assert.Contains(Check(p),i => i.Code == "motorcycle-engine-size-invalid");
    }
    [Fact]
    public void SportsAndTransporterPortfolioConditionsRetainExactQuestionIdentity()
    {
        var p = Example(); var risk = p["risk"]!; SetAnswer(risk,"MTS-10-Q03",JsonValue.Create(true)!,"boolean"); SetAnswer(risk,"MTS-10-Q04",JsonValue.Create(1)!,"percentage");
        var issues = Check(p); Assert.Contains(issues,i => i.Code == "sports-portfolio-ineligible" && i.QuestionId == "MTS-10-Q03"); Assert.DoesNotContain(issues,i => i.Code == "portfolio-minimum-one-percent" && i.QuestionId == "MTS-10-Q04");
        SetAnswer(risk,"MTS-10-Q19",JsonValue.Create(true)!,"boolean"); SetAnswer(risk,"MTS-10-Q21",JsonValue.Create(2)!,"count"); Assert.Contains(Check(p),i => i.Code == "transporter-minimum-three-vehicles" && i.QuestionId == "MTS-10-Q21");
        SetAnswer(risk,"MTS-10-Q19",JsonValue.Create(false)!,"boolean"); Assert.Contains(Check(p),i => i.Code == "inactive-answer-retained" && i.QuestionId == "MTS-10-Q21");
    }
    [Fact]
    public void MissingAndRetainedSpecifiedRowsAreNotImplicitlySelectedOrRemoved()
    {
        var p = Example(); p["risk"]!["specifiedVehiclesRequested"] = true; Assert.Contains(Check(p),i => i.Code == "specified-vehicle-required");
        p["risk"]!.AsObject().Remove("specifiedVehiclesRequested"); Assert.Contains(Check(p),i => i.Code == "specified-vehicle-declaration-required");
        Vehicle(p)["registeredOn"] = "1899-12-31"; Vehicle(p)["registrationYear"] = 1899;
        Assert.Contains(Check(p),i => i.Code == "vehicle-date-too-early"); Assert.Contains(Check(p),i => i.Code == "vehicle-year-too-early");
    }
}
