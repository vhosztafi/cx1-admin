using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CommercialOperationalPayloadTests
{
    [Fact]
    public void OperationalOccurrenceCommercialPayloadPreservesApproximationWithoutInventingObservedInstant()
    {
        var source=Source();var location=JsonNode.Parse(source.SnapshotJson)!["risk"]!["locations"]![0]!["id"]!.GetValue<Guid>();
        var observed=new BackOffice.Application.Operations.IncidentOccurrence(new(2026,10,1),"Europe/London","approximate",new TimeOnly(12,30));
        var subject=JsonSerializer.SerializeToElement(new{kind="property",locationId=location,coverCode="buildings",itemDescription="Fictional building damage"});
        var payload=CommercialIncidentPayload.CreateResolved(source,observed,Known,subject);
        Assert.False(payload.TryGetProperty("occurredAt",out _));Assert.Equal("approximate",payload.GetProperty("occurrence").GetProperty("precision").GetString());
        Assert.Equal("12:30",payload.GetProperty("occurrence").GetProperty("approximateLocalTime").GetString());
        Assert.Equal(source.SourceContentHash,payload.GetProperty("sourceContentHash").GetString());
        Assert.Throws<ArgumentException>(()=>CommercialIncidentPayload.CreateResolved(source with{EffectiveUntil=Occurred},observed,Known,subject));
        Assert.Throws<ArgumentException>(()=>CommercialIncidentPayload.CreateResolved(source,observed,Known,JsonSerializer.SerializeToElement(new{kind="property",locationId=Guid.NewGuid(),coverCode="buildings"})));
    }
    [Theory]
    [InlineData("foreign-location")]
    [InlineData("uncovered-location")]
    [InlineData("unselected-cover")]
    [InlineData("foreign-occupation")]
    [InlineData("motor-subject")]
    public void OperationalOccurrenceRejectsSelectionsOutsideRetainedCommercialCover(string change)
    {
        var source=Source(true);var snapshot=JsonNode.Parse(source.SnapshotJson)!;
        var subject=JsonSerializer.SerializeToNode(new{kind="property",locationId=snapshot["risk"]!["locations"]![0]!["id"]!.GetValue<Guid>(),coverCode="buildings"})!;
        if(change=="foreign-location")subject["locationId"]=Guid.NewGuid();
        if(change=="uncovered-location")subject["locationId"]=snapshot["risk"]!["locations"]![1]!["id"]!.DeepClone();
        if(change=="unselected-cover")subject["coverCode"]="business-interruption";
        if(change=="foreign-occupation")subject=JsonSerializer.SerializeToNode(new{kind="liability",coverCode="employers-liability",occupationId=Guid.NewGuid()})!;
        if(change=="motor-subject")subject=JsonSerializer.SerializeToNode(new{kind="registered-vehicle",vehicleId=Guid.NewGuid()})!;
        Assert.Throws<ArgumentException>(()=>CommercialIncidentPayload.CreateResolved(source,new(new(2026,10,1),"Europe/London","date"),Known,JsonSerializer.SerializeToElement(subject)));
    }
    [Fact]
    public void OperationalOccurrenceEmployerSelectionUsesRetainedWageIdentity()
    {
        var source=Source(true);var occupation=JsonNode.Parse(source.SnapshotJson)!["risk"]!["wages"]![0]!["id"]!.GetValue<Guid>();
        var payload=CommercialIncidentPayload.CreateResolved(source,new(new(2026,10,1),"Europe/London","date"),Known,JsonSerializer.SerializeToElement(new{kind="liability",coverCode="employers-liability",occupationId=occupation}));
        Assert.Equal(occupation,payload.GetProperty("subject").GetProperty("occupationId").GetGuid());
    }
    private static readonly DateTimeOffset Occurred = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
    private static readonly DateTimeOffset Known = Occurred.AddDays(1);
    private static CommercialPayloadSource Source(bool employers = false)
    {
        var snapshot = CommercialIssueSnapshotShapeTests.Snapshot();
        snapshot["cover"]!["endorsements"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { code = "demo-security", version = "1", text = "Maintain the declared alarm protection." }));
        snapshot["cover"]!["warranties"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { code = "demo-inspection", version = "1", text = "Retain the reviewed electrical inspection." }));
        if (employers) snapshot["cover"]!["sections"]!.AsArray().Add(JsonSerializer.SerializeToNode(new {
            id = Guid.NewGuid(), code = "employers-liability", limit = "10000000.00", targetIds = Array.Empty<string>() }));
        var json = snapshot.ToJsonString();
        return new(Guid.NewGuid(), Guid.NewGuid(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), json,
            DateTimeOffset.Parse(snapshot["term"]!["startsAt"]!.GetValue<string>()), DateTimeOffset.Parse(snapshot["term"]!["endsAt"]!.GetValue<string>()));
    }
    private static JsonElement Property(CommercialPayloadSource source) => JsonSerializer.SerializeToElement(new {
        kind = "property", locationId = JsonNode.Parse(source.SnapshotJson)!["risk"]!["locations"]![0]!["id"]!.GetValue<string>(), damageDescription = "Storm damage to the declared building" });

    [Fact]
    public void IncidentRetainsExactSourceAndOwnedPropertySubject()
    {
        var source = Source(); var payload = CommercialIncidentPayload.Create(source, Occurred, Known, Property(source));
        Assert.True(CommercialIncidentPayload.Valid(payload, source, Known));
        Assert.Equal(source.VersionId, payload.GetProperty("versionId").GetGuid());
        Assert.False(CommercialIncidentPayload.Valid(payload, source with { PolicyId = Guid.NewGuid() }, Known));
        Assert.False(CommercialIncidentPayload.Valid(payload, source with { VersionId = Guid.NewGuid() }, Known));
        Assert.False(CommercialIncidentPayload.Valid(payload, source with { SourceContentHash = new string('a', 64) }, Known));
    }

    [Theory]
    [InlineData("foreign-location")]
    [InlineData("uncovered-location")]
    [InlineData("unselected-section")]
    [InlineData("undeclared-occupation")]
    [InlineData("motor-field")]
    [InlineData("duplicate-field")]
    [InlineData("before-version")]
    [InlineData("at-replacement")]
    [InlineData("future-occurrence")]
    [InlineData("wrong-hash")]
    public void IncidentRejectsInvalidOrHistoricallyWrongSource(string change)
    {
        var source = Source(true); var subject = JsonNode.Parse(Property(source).GetRawText())!; var occurred = Occurred;
        if (change == "foreign-location") subject["locationId"] = Guid.NewGuid();
        if (change == "uncovered-location") subject["locationId"] = JsonNode.Parse(source.SnapshotJson)!["risk"]!["locations"]![1]!["id"]!.DeepClone();
        if (change == "unselected-section") subject = JsonNode.Parse("{\"kind\":\"liability\",\"section\":\"public-liability\"}")!;
        if (change == "undeclared-occupation") subject = JsonNode.Parse("{\"kind\":\"liability\",\"section\":\"employers-liability\",\"employeeOccupation\":\"Foreign occupation\"}")!;
        if (change == "motor-field") subject["registration"] = "DEMO123";
        if (change == "before-version") occurred = source.EffectiveFrom.AddTicks(-1);
        if (change == "at-replacement") source = source with { EffectiveUntil = Occurred };
        if (change == "future-occurrence") occurred = Known.AddSeconds(1);
        if (change == "wrong-hash") source = source with { SourceContentHash = new string('a', 64) };
        var value = JsonSerializer.SerializeToElement(subject);
        if (change == "duplicate-field") value = JsonDocument.Parse(Property(source).GetRawText().Replace("\"kind\":\"property\"", "\"kind\":\"liability\",\"kind\":\"property\"")).RootElement.Clone();
        Assert.Throws<ArgumentException>(() => CommercialIncidentPayload.Create(source, occurred, Known, value));
    }

    [Fact]
    public void LiabilityOccupationMustBelongToSelectedEmployersCover()
    {
        var source = Source(true); var occupation = JsonNode.Parse(source.SnapshotJson)!["risk"]!["wages"]![0]!["category"]!["label"]!.GetValue<string>();
        var subject = JsonSerializer.SerializeToElement(new { kind = "liability", section = "employers-liability", employeeOccupation = occupation });
        Assert.True(CommercialIncidentPayload.Valid(CommercialIncidentPayload.Create(source, Occurred, Known, subject), source, Known));
        Assert.Throws<ArgumentException>(() => CommercialIncidentPayload.Create(Source(), Occurred, Known, subject));
    }

    [Theory]
    [InlineData("policy-schedule")]
    [InlineData("policy-statement")]
    [InlineData("policy-certificate")]
    public void DocumentsCarryExactImmutableContentAndRejectSubstitution(string kind)
    {
        var source = Source(true); var payload = CommercialDocumentPayload.Create(source, kind);
        Assert.True(CommercialDocumentPayload.Valid(payload, source, kind));
        Assert.Equal(source.SourceContentHash, payload.GetProperty("sourceContentHash").GetString());
        Assert.False(CommercialDocumentPayload.Valid(payload, source with { VersionId = Guid.NewGuid() }, kind));
        Assert.False(payload.TryGetProperty("vehicles", out _));
        var changed = JsonNode.Parse(payload.GetRawText())!; changed["insured"]!["legalName"] = "Foreign current business";
        Assert.False(CommercialDocumentPayload.Valid(JsonSerializer.SerializeToElement(changed), source, kind));
        using var snapshot = JsonDocument.Parse(source.SnapshotJson);
        Assert.True(JsonElement.DeepEquals(snapshot.RootElement.GetProperty("insured"), payload.GetProperty("insured")));
        Assert.True(JsonElement.DeepEquals(snapshot.RootElement.GetProperty("cover").GetProperty("endorsements"), payload.GetProperty("endorsements")));
        if (kind == "policy-statement") Assert.True(JsonElement.DeepEquals(snapshot.RootElement.GetProperty("risk"), payload.GetProperty("declarations")));
        if (kind == "policy-schedule") Assert.True(JsonElement.DeepEquals(snapshot.RootElement.GetProperty("cover").GetProperty("sections"), payload.GetProperty("sections")));
        if (kind == "policy-certificate") { Assert.Equal("employers-liability", payload.GetProperty("section").GetProperty("code").GetString()); Assert.False(payload.TryGetProperty("declarations", out _)); }
    }

    [Fact]
    public void RetainedEmployersLimitDoesNotAuthorizeCertificate()
    {
        Assert.Throws<ArgumentException>(() => CommercialDocumentPayload.Create(Source(), "policy-certificate"));
        Assert.Throws<ArgumentException>(() => CommercialDocumentPayload.Create(Source(), "motor-certificate"));
    }

    [Theory]
    [InlineData("before-effective")]
    [InlineData("before-known")]
    [InlineData("forged-effective")]
    public void ServicingIncidentCannotUseAnInapplicableHistoricalVersion(string change)
    {
        var source = Source(); var snapshot = JsonNode.Parse(source.SnapshotJson)!;
        snapshot["snapshotFormat"] = "issued-commercial-servicing-1";
        snapshot["provenance"] = JsonSerializer.SerializeToNode(new { source = "backoffice", sourceQuoteId = Guid.NewGuid(), servicingIssueDecisionId = Guid.NewGuid(),
            baseVersionId = Guid.NewGuid(), revisionId = Guid.NewGuid(), transactionId = Guid.NewGuid(), effectiveAt = Occurred,
            processedAt = Known, sliceOrdinal = 1, inputHash = new string('a',64) });
        var json = snapshot.ToJsonString();
        source = source with { SnapshotJson = json, SourceContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), EffectiveFrom = Occurred };
        var occurrence = change == "before-effective" ? Occurred.AddTicks(-1) : Occurred;
        var knowledge = change == "before-known" ? Known.AddTicks(-1) : Known;
        if (change == "forged-effective") source = source with { EffectiveFrom = Occurred.AddDays(-1) };
        Assert.Throws<ArgumentException>(() => CommercialIncidentPayload.Create(source, occurrence, knowledge, Property(source)));
    }

    [Fact]
    public void CancellationDoesNotAuthorizeIncidentCoverOrNewPolicyDocuments()
    {
        var source = Source(true); using var basis = JsonDocument.Parse(source.SnapshotJson);
        var json = CancellationIssueSnapshot.Create(basis.RootElement, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), source.VersionId,
            Guid.NewGuid(), Guid.NewGuid(), Occurred, Occurred.AddDays(-1), new string('b',64), "insured-request", "demo-servicing-1"));
        var cancelled = source with { VersionId = Guid.NewGuid(), SnapshotJson = json, SourceContentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))), EffectiveFrom = Occurred };
        Assert.Throws<ArgumentException>(() => CommercialIncidentPayload.Create(cancelled, Occurred, Known, Property(source)));
        foreach (var kind in new[] { "policy-schedule", "policy-statement", "policy-certificate" })
            Assert.Throws<ArgumentException>(() => CommercialDocumentPayload.Create(cancelled, kind));
        Assert.True(CommercialIncidentPayload.Valid(CommercialIncidentPayload.Create(source, Occurred.AddTicks(-1), Known, Property(source)), source, Known));
    }
}
