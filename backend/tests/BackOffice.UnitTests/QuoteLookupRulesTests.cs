using System.Text.Json;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteLookupRulesTests
{
    private static readonly Guid Driver = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Vehicle = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly QuoteVersionPins Pins = new(Guid.NewGuid(), Guid.NewGuid(), "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version);
    private static JsonDocument Proposal(string registration = "AB12 CDE", string number = "DEMO123456", string postcode = "AB1 2CD") =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { insured = new { address = new { postcode } }, risk = new {
            drivers = new[] { new { id = Driver, licence = new { number }, address = new { postcode } } },
            vehicles = new[] { new { id = Vehicle, registration, make = "Fictional Motors" } } } }));

    [Theory]
    [InlineData("address", "insured", "AB12CD")]
    [InlineData("address", "driver", "AB12CD")]
    [InlineData("vehicle", "vehicle", "AB12CDE")]
    [InlineData("licence", "driver", "DEMO123456")]
    public void QueriesDeriveFromTheActualSavedTargetWithoutChangingDeclarations(string kind, string scope, string query)
    {
        using var p = Proposal(); var before = p.RootElement.GetRawText();
        var target = new QuoteLookupTarget(kind, scope, scope == "insured" ? null : scope == "driver" ? Driver : Vehicle);
        var result = QuoteLookupRules.Prepare(p.RootElement, target, Pins);
        Assert.Equal(query, result.Query); Assert.Equal(64, result.InputFingerprint.Length);
        Assert.Equal(before, p.RootElement.GetRawText());
        Assert.Equal(result, QuoteLookupRules.EnsureCurrent(p.RootElement, target, Pins, result.InputFingerprint));
    }

    [Theory]
    [InlineData("vehicle", "driver")]
    [InlineData("licence", "insured")]
    [InlineData("https://provider.invalid", "vehicle")]
    public void UnsupportedTargetsNeverBecomeProviderRequests(string kind, string scope)
    {
        using var p = Proposal(); Assert.Throws<QuoteInputException>(() => QuoteLookupRules.Prepare(p.RootElement, new(kind, scope, Driver), Pins));
    }

    [Fact]
    public void TargetMembershipIsCheckedInTheCorrectCollection()
    {
        using var p = Proposal();
        Assert.Throws<QuoteInputException>(() => QuoteLookupRules.Prepare(p.RootElement, new("vehicle", "vehicle", Driver), Pins));
        Assert.Throws<QuoteInputException>(() => QuoteLookupRules.Prepare(p.RootElement, new("vehicle", "vehicle"), Pins));
        Assert.Throws<QuoteInputException>(() => QuoteLookupRules.Prepare(p.RootElement, new("address", "insured", Driver), Pins));
    }

    [Fact]
    public void ChangedSubjectAndVersionPinsInvalidateStoredResults()
    {
        using var before = Proposal(); using var changed = Proposal(registration: "CD34 EFG");
        var target = new QuoteLookupTarget("vehicle", "vehicle", Vehicle); var input = QuoteLookupRules.Prepare(before.RootElement, target, Pins);
        Assert.Equal("lookup-input-changed", Assert.Throws<QuoteInputException>(() => QuoteLookupRules.EnsureCurrent(changed.RootElement, target, Pins, input.InputFingerprint)).Code);
        Assert.Throws<QuoteInputException>(() => QuoteLookupRules.EnsureCurrent(before.RootElement, target, Pins with { ReferenceVersion = "different" }, input.InputFingerprint));
        using var unrelated = Proposal(number: "CHANGED123");
        Assert.Equal(input.InputFingerprint, QuoteLookupRules.Prepare(unrelated.RootElement, target, Pins).InputFingerprint);
    }

    [Fact]
    public void MalformedQueriesAndManualReasonsFailClosed()
    {
        using var p = Proposal(registration: "https://x");
        Assert.Throws<QuoteInputException>(() => QuoteLookupRules.Prepare(p.RootElement, new("vehicle", "vehicle", Vehicle), Pins));
        foreach (var reason in new[] { " ", new string('x', 1001), "reason\0" }) Assert.Throws<QuoteInputException>(() => QuoteLookupRules.ManualReason(reason));
        Assert.Equal("Provider had no matching vehicle", QuoteLookupRules.ManualReason(" Provider had no matching vehicle "));
        Assert.Throws<QuoteInputException>(() => QuoteLookupRules.Scenario("real-provider"));
        Assert.Equal("timeout-after-success", QuoteLookupRules.Scenario("timeout-after-success"));
    }
}
