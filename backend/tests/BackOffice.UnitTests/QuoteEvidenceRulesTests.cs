using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteEvidenceRulesTests
{
    private static readonly Guid Driver = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly QuoteVersionPins Pins = new(Guid.NewGuid(), Guid.NewGuid(), "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version);
    private static JsonObject Proposal() => JsonNode.Parse(JsonSerializer.Serialize(new { insured = new { legalName = "Fictional trade" },
        risk = new { business = new { description = "Vehicle repairs" }, previousInsurance = new { noClaimsYears = 3 },
            drivers = new[] { new { id = Driver, firstName = "Alex", licence = new { number = "DEMO123456" } },
                new { id = Guid.NewGuid(), firstName = "Other", licence = new { number = "DEMO654321" } } } } }))!.AsObject();
    private static QuoteEvidenceInput Input(JsonNode proposal, string code = "photocard-both-sides", Guid? item = null, QuoteVersionPins? pins = null)
    { using var document = JsonDocument.Parse(proposal.ToJsonString()); return QuoteEvidenceRules.Prepare(document.RootElement, code, item ?? Driver, pins ?? Pins); }

    [Fact]
    public void DriverFingerprintUsesActualIdentityAndRelevantSubjectRatherThanArrayPosition()
    {
        var proposal = Proposal(); var original = Input(proposal);
        Assert.True(QuoteEvidenceRules.Matches(original, original.InputFingerprint));
        var drivers = proposal["risk"]!["drivers"]!.AsArray(); var first = drivers[0]!.DeepClone(); drivers.RemoveAt(0); drivers.Add(first);
        Assert.Equal(original.InputFingerprint, Input(proposal).InputFingerprint);
        drivers[0]!["firstName"] = "Unrelated edit";
        Assert.Equal(original.InputFingerprint, Input(proposal).InputFingerprint);
        drivers[1]!["licence"]!["number"] = "CHANGED123456";
        Assert.NotEqual(original.InputFingerprint, Input(proposal).InputFingerprint);
        Assert.False(QuoteEvidenceRules.Matches(Input(proposal), original.InputFingerprint));
        Assert.False(QuoteEvidenceRules.Matches(original, "not-a-hash"));
    }

    [Fact]
    public void FingerprintsBindInsuredRequirementAndTrustedPins()
    {
        var proposal = Proposal(); var original = Input(proposal);
        Assert.NotEqual(original.InputFingerprint, Input(proposal, "driving-record").InputFingerprint);
        Assert.NotEqual(original.InputFingerprint, Input(proposal, pins: Pins with { ReferenceVersion = "changed" }).InputFingerprint);
        proposal["insured"]!["legalName"] = "Different insured";
        Assert.NotEqual(original.InputFingerprint, Input(proposal).InputFingerprint);
    }

    [Fact]
    public void RequirementsRejectForeignMissingDuplicateOrInapplicableItems()
    {
        var proposal = Proposal();
        Assert.Throws<QuoteInputException>(() => Input(proposal, item: Guid.NewGuid()));
        Assert.Throws<QuoteInputException>(() => Input(proposal, item: Guid.Empty));
        Assert.Throws<QuoteInputException>(() => Input(proposal, "motor-trader-proof"));
        Assert.Throws<QuoteInputException>(() => Input(proposal, "unknown-proof"));
        var drivers = proposal["risk"]!["drivers"]!.AsArray(); drivers.Add(drivers[0]!.DeepClone());
        Assert.Throws<QuoteInputException>(() => Input(proposal));
    }

    [Fact]
    public void BusinessAndNoClaimsFingerprintsAreIndependentAndConditional()
    {
        var proposal = Proposal();
        string Fingerprint(string code)
        { using var document = JsonDocument.Parse(proposal.ToJsonString()); return QuoteEvidenceRules.Prepare(document.RootElement, code, null, Pins).InputFingerprint; }
        var business = Fingerprint("motor-trader-proof"); var noClaims = Fingerprint("no-claims-proof");
        proposal["risk"]!["business"]!["description"] = "Changed business";
        Assert.NotEqual(business, Fingerprint("motor-trader-proof")); Assert.Equal(noClaims, Fingerprint("no-claims-proof"));
        proposal["risk"]!["previousInsurance"]!["noClaimsYears"] = 0;
        Assert.Throws<QuoteInputException>(() => Fingerprint("no-claims-proof"));
    }

    [Theory]
    [InlineData("proof.pdf", "application/pdf", "JVBERi0xLjc=")]
    [InlineData("proof.png", "image/png", "iVBORw0KGgo=")]
    [InlineData("proof.jpg", "image/jpeg", "/9j/AAH/2Q==")]
    [InlineData("proof.txt", "text/plain", "RmljdGlvbmFsIGV2aWRlbmNl")]
    public void FilesRetainExactBytesAndContentHash(string name, string media, string encoded)
    {
        var bytes = Convert.FromBase64String(encoded); var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var file = QuoteEvidenceRules.File(name, media, bytes); Assert.Equal(expected, file.Sha256); Assert.Equal(bytes, file.Content);
        bytes[0] ^= 1; Assert.NotEqual(bytes, file.Content); Assert.Equal(expected, Convert.ToHexStringLower(SHA256.HashData(file.Content)));
    }

    [Fact]
    public void FileScreenRejectsUnsafeNamesSpoofedMediaInvalidUtf8AndOversize()
    {
        var bytes = Encoding.UTF8.GetBytes("Fictional evidence");
        foreach (var name in new[] { "../proof.txt", "folder\\proof.txt", "proof.txt:stream", ".txt", " proof.txt", "proof.txt.", "proof\r\n.txt", "proof\u202e.txt" })
            Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.File(name, "text/plain", bytes));
        foreach (var bad in new[] { Encoding.UTF8.GetBytes("<script>example</script>"), new byte[] { 0xff }, new byte[] { 0 } })
            Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.File("proof.txt", "text/plain", bad));
        Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.File("proof.pdf", "application/pdf", bytes));
        Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.File("proof.txt", "text/html", bytes));
        Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.File("proof.txt", "text/plain", []));
        Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.File("proof.txt", "text/plain", new byte[QuoteEvidenceRules.MaximumFileBytes + 1]));
        Assert.Equal(QuoteEvidenceRules.MaximumFileBytes, QuoteEvidenceRules.File("proof.txt", "text/plain", Enumerable.Repeat((byte)'x', QuoteEvidenceRules.MaximumFileBytes).ToArray()).Content.Length);
    }

    [Fact]
    public void AttestationAndWithdrawalReasonsMustBeMeaningfulAndBounded()
    {
        Assert.Equal("Checked fictional proof", QuoteEvidenceRules.Reason(" Checked fictional proof "));
        foreach (var reason in new[] { null, "", " ", new string('x', 1001), "reason\0" })
            Assert.Throws<QuoteInputException>(() => QuoteEvidenceRules.Reason(reason));
    }
}
