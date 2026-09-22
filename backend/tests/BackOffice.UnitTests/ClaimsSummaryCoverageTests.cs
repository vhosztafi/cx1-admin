using System.Text.Json;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ClaimsSummaryCoverageTests
{
    private static readonly DateTimeOffset Requested = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, string> Details = new()
    {
        ["liability"] = "Not yet determined by administrator",
        ["incurred"] = "6500.00",
        ["recoveryExpected"] = "Recovery enquiries pending",
        ["excessApplied"] = "750.00",
        ["movementNote"] = "Security footage requested"
    };

    private static ClaimsAdministratorSummary Read(Dictionary<string, string>? details = null)
    {
        var input = new Dictionary<string, object?>
        {
            ["providerReference"] = "CLM-DEMO-SOURCE", ["eventId"] = "claims/source-fields",
            ["asOf"] = Requested.AddMinutes(1), ["status"] = "open", ["paid"] = "0.00",
            ["reserved"] = "6500.00", ["currency"] = "GBP"
        };
        if (details is not null) foreach (var pair in details) input[pair.Key] = pair.Value;
        return JsonSerializer.Deserialize<ClaimsAdministratorSummary>(JsonSerializer.Serialize(input, Json), Json)!;
    }

    [Fact]
    public void AdministratorDetailFieldsSurviveTheSavedSummaryRoundTrip()
    {
        var summary = Read(Details);
        ClaimsRules.Summary(summary, Requested, Requested.AddMinutes(2));
        var saved = JsonSerializer.SerializeToElement(summary, Json);
        foreach (var pair in Details)
        {
            Assert.True(saved.TryGetProperty(pair.Key, out var value), $"Provider field {pair.Key} was lost.");
            Assert.Equal(pair.Value, value.GetString());
        }
    }

    [Fact]
    public void OlderSummariesKeepUnreportedDetailsUnknown()
    {
        var summary = Read();
        ClaimsRules.Summary(summary, Requested, Requested.AddMinutes(2));
        var saved = JsonSerializer.SerializeToElement(summary, Json);
        foreach (var field in Details.Keys)
        {
            Assert.True(!saved.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null,
                $"Unreported provider field {field} must remain unknown.");
        }
        // Provider inbox hashes use the exact serialized outcome. Optional fields
        // must not change the bytes of an already recorded older provider event.
        const string legacy = "{\"providerReference\":\"CLM-DEMO-SOURCE\",\"eventId\":\"claims/source-fields\",\"asOf\":\"2026-09-22T10:01:00+00:00\",\"status\":\"open\",\"paid\":\"0.00\",\"reserved\":\"6500.00\",\"currency\":\"GBP\"}";
        Assert.Equal(legacy, JsonSerializer.Serialize(summary, Json));
    }

    [Theory]
    [InlineData("incurred", "-1.00")]
    [InlineData("incurred", "6500")]
    [InlineData("excessApplied", "1.001")]
    [InlineData("excessApplied", "10000000000000.00")]
    public void InvalidAdditionalProviderMoneyCannotEnterTheSummary(string field, string value)
    {
        var details = new Dictionary<string, string>(Details) { [field] = value };
        Assert.Throws<ClaimsRuleException>(() => ClaimsRules.Summary(Read(details), Requested, Requested.AddMinutes(2)));
    }

    [Theory]
    [InlineData("liability", 301)]
    [InlineData("recoveryExpected", 1001)]
    [InlineData("movementNote", 2001)]
    public void ProviderNarrativesHaveExplicitStorageBounds(string field, int length)
    {
        var details = new Dictionary<string, string>(Details) { [field] = new string('x', length) };
        Assert.Throws<ClaimsRuleException>(() => ClaimsRules.Summary(Read(details), Requested, Requested.AddMinutes(2)));
    }
}
