using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteRulesTests
{
    private static QuoteVersionPins Pins => new(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"), "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version);

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void OmittedInitialProposalCreatesOnlyAnIncompleteTrustedEnvelope(string code)
    {
        var capture = QuoteRules.Prepare(null, code, Pins);
        Assert.Equal(code, JsonNode.Parse(capture.Input.Json)!["productCode"]!.GetValue<string>());
        Assert.Equal("{}", capture.TermIntentJson); Assert.Empty(capture.Registrations);
        Assert.True(QuoteRules.IsUnchanged(Convert.FromHexString(capture.Input.ContentHash), capture));
    }

    [Fact]
    public void ActualCapturesPrepareCanonicalTermAndCurrentVehicleProjection()
    {
        var examples = typeof(QuoteRulesTests).Assembly.GetManifestResourceNames().Where(x => x.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, examples.Length);
        foreach (var name in examples)
        {
            using var stream = typeof(QuoteRulesTests).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var proposal = JsonNode.Parse(reader.ReadToEnd())!["proposal"]!;
            var capture = QuoteRules.Prepare(proposal.ToJsonString(), proposal["productCode"]!.GetValue<string>(), Pins);
            Assert.True(JsonNode.DeepEquals(proposal["termIntent"], JsonNode.Parse(capture.TermIntentJson)));
            var vehicles = proposal["risk"]!["vehicles"]!.AsArray(); Assert.Equal(vehicles.Count, capture.Registrations.Count);
            foreach (var registration in capture.Registrations)
                Assert.Contains(vehicles, x => x!["id"]!.GetValue<Guid>() == registration.VehicleId &&
                    x["registration"]!.GetValue<string>().Replace(" ", "") == registration.NormalizedRegistration);
        }
    }

    [Fact]
    public void InvalidOrCrossProductInputCannotProduceAWrite()
    {
        Assert.Throws<QuoteValidationException>(() => QuoteRules.Prepare("null", "motor-trade-road-risks", Pins));
        var error = Assert.Throws<QuoteValidationException>(() => QuoteRules.Prepare("{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-combined\"}", "motor-trade-road-risks", Pins));
        Assert.Equal("quote-product-mismatch", Assert.Single(error.Issues).Code);
        Assert.Throws<InvalidOperationException>(() => QuoteRules.Prepare(null, "commercial-combined", Pins));
        Assert.Throws<InvalidOperationException>(() => QuoteRules.Prepare(null, "motor-trade-combined", Pins with { AgencyTermsVersionId = Guid.Empty }));
    }

    [Fact]
    public void TermIntentChangesHashAndProjectionDoesNotChangeDeclarations()
    {
        var proposal = new JsonObject { ["schemaVersion"] = "1.0", ["productCode"] = "motor-trade-road-risks",
            ["risk"] = new JsonObject { ["vehicles"] = new JsonArray(new JsonObject { ["id"] = "bbbbbbbb-0000-4000-8000-000000000001", ["registration"] = "DEMO 01" },
                new JsonObject { ["id"] = "bbbbbbbb-0000-4000-8000-000000000002", ["registration"] = "  " }) } };
        var first = QuoteRules.Prepare(proposal.ToJsonString(), "motor-trade-road-risks", Pins);
        Assert.Equal("DEMO01", Assert.Single(first.Registrations).NormalizedRegistration);
        Assert.Equal("DEMO 01", JsonNode.Parse(first.Input.Json)!["risk"]!["vehicles"]![0]!["registration"]!.GetValue<string>());
        proposal["termIntent"] = new JsonObject { ["localStartDate"] = "2026-10-01" };
        var second = QuoteRules.Prepare(proposal.ToJsonString(), "motor-trade-road-risks", Pins);
        Assert.False(QuoteRules.IsUnchanged(Convert.FromHexString(first.Input.ContentHash), second));
    }

    [Fact]
    public void CaptureClosureAndEveryVersionPinAreEnforced()
    {
        QuoteRules.EnsureEditable("draft", null); QuoteRules.EnsureRetainedPins(Pins, Pins);
        foreach (var state in new[] { "withdrawn", "issued", "unknown" }) Assert.Throws<QuoteInputException>(() => QuoteRules.EnsureEditable(state, null));
        Assert.Throws<QuoteInputException>(() => QuoteRules.EnsureEditable("draft", DateTimeOffset.UtcNow));
        foreach (var changed in new[] { Pins with { ProductVersionId = Guid.NewGuid() }, Pins with { AgencyTermsVersionId = Guid.NewGuid() },
            Pins with { SchemaVersion = "2.0" }, Pins with { QuestionSetVersion = "new" }, Pins with { ReferenceVersion = "new" } })
            Assert.Throws<QuoteInputException>(() => QuoteRules.EnsureRetainedPins(Pins, changed));
    }
}
