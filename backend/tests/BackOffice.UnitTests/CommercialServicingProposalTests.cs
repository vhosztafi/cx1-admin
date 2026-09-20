using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialServicingProposalTests
{
    private static readonly Guid Policy = Guid.NewGuid(), Version = Guid.NewGuid(), Client = Guid.NewGuid();
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-09-15T23:00:00Z"), End = DateTimeOffset.Parse("2027-09-15T23:00:00Z"), Now = DateTimeOffset.Parse("2026-09-16T12:00:00Z");
    private static ServicingProposalContext Context => new(Policy, Version, Start, End, Start, Now, false);
    private static JsonObject Snapshot()
    {
        using var stream = typeof(CommercialServicingProposalTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!;
        var value = JsonNode.Parse(stream)!.AsObject(); value.Remove("format"); value.Remove("termIntent"); value["snapshotFormat"] = "issued-commercial-1";
        value["insured"]!["clientId"] = Client; value["insured"]!["clientAgencyRelationshipId"] = Guid.NewGuid();
        value["term"] = JsonSerializer.SerializeToNode(new { kind = "annual", startsAt = Start, endsAt = End, timeZone = "Europe/London" });
        value["cover"]!["sections"] = new JsonArray(); value["cover"]!["endorsements"] = new JsonArray(); value["cover"]!["warranties"] = new JsonArray();
        return value;
    }
    private static JsonObject Intent(string date = "2026-10-01", string time = "00:00") => new() { ["localDate"] = date, ["localTime"] = time, ["timeZone"] = "Europe/London" };
    private static JsonObject Change(string kind, Guid id, object? payload = null, string operation = "update")
    {
        var value = new JsonObject { ["changeId"] = Guid.NewGuid(), ["riskItemId"] = id, ["kind"] = kind, ["operation"] = operation };
        if (payload is not null) value["payload"] = JsonSerializer.SerializeToNode(payload); return value;
    }
    private static JsonObject Proposal(params JsonObject[] changes) => new() { ["schemaVersion"] = "1.0", ["baseVersionId"] = Version, ["reason"] = "Fictional commercial adjustment",
        ["requestedBy"] = new JsonObject { ["kind"] = "internal" }, ["commonEffectiveIntent"] = Intent(), ["changes"] = new JsonArray(changes.Select(x => (JsonNode)x).ToArray()) };
    private static Guid Row(JsonObject snapshot, string collection, int index = 0) => Guid.Parse(snapshot["risk"]![collection]![index]!["id"]!.GetValue<string>());
    private static ServicingProposalAssessment Assess(JsonObject snapshot, JsonObject proposal, ServicingProposalContext? context = null) => CommercialServicingProposalRules.Assess(snapshot.ToJsonString(), proposal.ToJsonString(), context ?? Context);

    [Fact]
    public void CommercialServicingProposalProjectsEveryTypedGroupWithoutChangingIssuedBytes()
    {
        var snapshot = Snapshot(); var original = snapshot.ToJsonString();
        var proposal = Proposal(Change("commercial-property", Row(snapshot,"locations"), new { buildings = "100.10" }),
            Change("commercial-business", Policy, new { description = "Changed fictional trade" }), Change("commercial-bi", Policy, new { sumInsured = "250000.01" }),
            Change("commercial-liability", Policy, new { publicLimit = "2500000.00" }), Change("commercial-wage", Row(snapshot,"wages"), new { employees = "125000.01" }),
            Change("commercial-loss", Guid.NewGuid(), new { amount = "0.00" }, "add"), Change("commercial-cover", Policy, new { contractWorks = new { selected = false } }),
            Change("commercial-insured", Client, new { tradingName = "Retained customer identity" }), Change("commercial-declarations", Policy, new { materialFacts = "Changed fictional material facts" }));
        var result = Assess(snapshot, proposal); Assert.Single(result.Slices); Assert.Equal(9, result.Slices[0].ChangeIds.Count);
        Assert.Equal("100.10", result.Proposed.GetProperty("risk").GetProperty("locations")[0].GetProperty("buildings").GetString());
        Assert.Equal("125000.01", result.Proposed.GetProperty("risk").GetProperty("wages")[0].GetProperty("employees").GetString());
        Assert.False(result.Proposed.GetProperty("insured").TryGetProperty("clientId", out _)); Assert.False(result.Proposed.GetProperty("cover").TryGetProperty("sections", out _));
        Assert.Equal("commercial-combined-capture-1", result.Proposed.GetProperty("format").GetString()); Assert.Equal(original, snapshot.ToJsonString());
        Assert.NotEmpty(result.Changes); Assert.Equal(result.Proposed.GetRawText(), Assess(snapshot, proposal).Proposed.GetRawText());
        Assert.Equal(result.Proposed.GetRawText(), ServicingProposalRules.Assess(original, proposal.ToJsonString(), Context).Proposed.GetRawText());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("deleted")]
    [InlineData("reused")]
    [InlineData("wrong-singleton")]
    [InlineData("motor")]
    public void CommercialServicingProposalRejectsUnownedConflictingAndMotorSubjects(string scenario)
    {
        var snapshot = Snapshot(); var location = Row(snapshot,"locations");
        var proposal = scenario switch {
            "foreign" => Proposal(Change("commercial-location",Guid.NewGuid(),new { reference="Foreign" })),
            "duplicate" => Proposal(Change("commercial-location",location,new { reference="A" }),Change("commercial-location",location,new { reference="B" })),
            "deleted" => Proposal(Change("commercial-location",location,operation:"remove"),Change("commercial-property",location,new { stock="0.00" })),
            "reused" => Proposal(Change("commercial-location",location,operation:"remove"),Change("commercial-location",location,new { reference="Resurrected" },"add")),
            "wrong-singleton" => Proposal(Change("commercial-liability",Guid.NewGuid(),new { publicLimit="1.00" })),
            _ => Proposal(Change("driver",Guid.NewGuid(),new { })) };
        Assert.Throws<QuoteValidationException>(()=>Assess(snapshot,proposal));
    }

    [Fact]
    public void CommercialServicingProposalPreservesUnknownZeroAndLossOwnership()
    {
        var snapshot=Snapshot(); var location=Row(snapshot,"locations"); var replacement=snapshot["risk"]!["locations"]![0]!.DeepClone().AsObject();replacement.Remove("id");replacement.Remove("stock");
        replacement["sprinklers"]=false;replacement["contents"]="0.00";
        var change=Change("commercial-location",location,replacement);change["payloadMode"]="replace";
        var result=Assess(snapshot,Proposal(change));var stored=result.Proposed.GetProperty("risk").GetProperty("locations")[0];
        Assert.False(stored.TryGetProperty("stock",out _));Assert.Equal("0.00",stored.GetProperty("contents").GetString());Assert.False(stored.GetProperty("sprinklers").GetBoolean());Assert.NotEmpty(result.ReadinessIssues);
        Assert.Throws<QuoteValidationException>(()=>Assess(snapshot,Proposal(Change("commercial-loss",Guid.NewGuid(),new {riskItemId=Guid.NewGuid()},"add"))));
    }

    [Fact]
    public void CommercialServicingProposalCumulativeCoverDatesRetainTemporaryChanges()
    {
        var snapshot=Snapshot();var first=Change("commercial-cover",Policy,new {contractWorks=new {sumInsured="12345.67"}});
        var later=Change("commercial-cover",Policy,new {contractWorks=new {sumInsured="10000.01"}});later["effectiveIntent"]=Intent("2026-11-01");
        var proposal=Proposal(first,later);proposal["dateBasis"]="per-cover-change";var result=Assess(snapshot,proposal);
        Assert.Equal(2,result.Slices.Count);Assert.Equal("12345.67",result.Slices[0].Proposed.GetProperty("cover").GetProperty("contractWorks").GetProperty("sumInsured").GetString());
        Assert.Equal("10000.01",result.Slices[1].Proposed.GetProperty("cover").GetProperty("contractWorks").GetProperty("sumInsured").GetString());
        proposal["dateBasis"]="shared";Assert.Contains(Assess(snapshot,proposal).ReadinessIssues,x=>x.Code=="shared-date-override-forbidden");Assert.Empty(Assess(snapshot,proposal).Slices);
    }

    [Theory]
    [InlineData("2026-09-15","effective-outside-term")]
    [InlineData("2027-09-16","effective-outside-term")]
    [InlineData("2026-09-16","senior-backdate-authority-required")]
    public void CommercialServicingProposalDatesDoNotGrantAuthority(string date,string code)
    {
        var snapshot=Snapshot();var proposal=Proposal(Change("commercial-business",Policy,new {description="Dated change"}));proposal["commonEffectiveIntent"]=Intent(date);
        var result=Assess(snapshot,proposal);Assert.Contains(result.ReadinessIssues,x=>x.Code==code);Assert.Empty(result.Slices);
    }

    [Fact]
    public void CommercialServicingProposalRejectsMasterTransfersAndStaleBase()
    {
        var snapshot=Snapshot();Assert.Throws<QuoteInputException>(()=>Assess(snapshot,Proposal(Change("commercial-insured",Client,new {clientId=Guid.NewGuid()}))));
        var proposal=Proposal();proposal["baseVersionId"]=Guid.NewGuid();Assert.Throws<QuoteInputException>(()=>Assess(snapshot,proposal));
        var dated=Proposal(Change("commercial-business",Policy,new {description="Stale effective base"}));
        Assert.Contains(Assess(snapshot,dated,Context with {LatestIssuedEffectiveAt=DateTimeOffset.Parse("2026-11-01T00:00:00Z")}).ReadinessIssues,x=>x.Code=="effective-before-latest-issued-slice");
    }

    [Theory]
    [InlineData("2027-03-28", "nonexistent-local-time")]
    [InlineData("2026-10-25", "ambiguous-local-time")]
    public void CommercialServicingProposalRejectsInvalidLondonClockChoice(string date, string code)
    {
        var proposal = Proposal(Change("commercial-business", Policy, new { description = "Clock boundary change" }));
        proposal["commonEffectiveIntent"] = Intent(date,"01:30");
        Assert.Contains(Assess(Snapshot(),proposal).ReadinessIssues,x=>x.Code==code);Assert.Empty(Assess(Snapshot(),proposal).Slices);
        if (code == "ambiguous-local-time") { proposal["commonEffectiveIntent"]!["utcOffsetMinutes"]=0;Assert.Single(Assess(Snapshot(),proposal).Slices); }
    }
}
