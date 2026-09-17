using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingRatingInputTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static ServicingRatingRequestInput Input()
    {
        var term = QuoteRatingRulesTests.Term(); var driver = Guid.NewGuid();
        var projected = new ProjectedUnderwritingInput(QuoteRatingRulesTests.Facts(), term,
            JsonSerializer.SerializeToElement(new { drivers = new[] { new { id = driver, age = 35 } } }),
            [new(driver, 35, 10, false, false)], [], 5, 0m, new Dictionary<string, decimal>(), false, 0, null, null);
        return new() { Format = "servicing-rating-input-1", DraftId = Guid.NewGuid(), RevisionId = Guid.NewGuid(), PolicyId = Guid.NewGuid(),
            BaseTermId = Guid.NewGuid(), BaseVersionId = Guid.NewGuid(), ProductVersionId = Guid.NewGuid(), AgencyTermsVersionId = Guid.NewGuid(),
            RatingRuleVersionId = Guid.NewGuid(), BinderVersionId = Guid.NewGuid(), AuthorityVersionId = Guid.NewGuid(), RuntimeVersionId = Guid.NewGuid(),
            ScenarioVersionId = Guid.NewGuid(), ServicingSettingVersionId = Guid.NewGuid(), RequestedBy = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.Parse("2026-09-01T00:00:00Z"), BaseContentHash = new string('a',64), RevisionContentHash = new string('b',64),
            Term = term, BaseAnnualPremium = 600m, CommissionBasisPoints = 1000, Fee = 15m,
            RatingDefinition = QuoteRatingRulesTests.Definition("rating"), Slices = [new(DateTimeOffset.Parse("2026-10-01T00:00:00Z"), [Guid.NewGuid()], projected)] };
    }

    [Fact]
    public void ExactInputRoundTripsAndEveryAuthorityPinChangesHash()
    {
        var input = Input(); var first = ServicingRatingInput.Encode(input);
        Assert.Equal(first.ContentHash, ServicingRatingInput.Encode(input).ContentHash);
        Assert.Equal(input.DraftId, ServicingRatingInput.Read(first.Json, first.ContentHash).DraftId);
        var mutations = new[] { input with { DraftId = Guid.NewGuid() }, input with { RevisionId = Guid.NewGuid() },
            input with { BaseVersionId = Guid.NewGuid() }, input with { ProductVersionId = Guid.NewGuid() },
            input with { AgencyTermsVersionId = Guid.NewGuid() }, input with { RatingRuleVersionId = Guid.NewGuid() },
            input with { BinderVersionId = Guid.NewGuid() }, input with { AuthorityVersionId = Guid.NewGuid() },
            input with { RuntimeVersionId = Guid.NewGuid() }, input with { ScenarioVersionId = Guid.NewGuid() },
            input with { ServicingSettingVersionId = Guid.NewGuid() }, input with { RequestedAt = input.RequestedAt.AddSeconds(1) },
            input with { Fee = 16m }, input with { RevisionContentHash = new string('c',64) }, input with { BaseContentHash = new string('d',64) } };
        foreach (var changed in mutations) Assert.NotEqual(first.ContentHash, ServicingRatingInput.Encode(changed).ContentHash);
    }

    [Fact]
    public void ScheduleDatesChangeIdsAndStableRiskIdentitiesBindThePrice()
    {
        var input = Input(); var original = ServicingRatingInput.Encode(input); var slice = input.Slices[0];
        foreach (var changed in new[] {
            slice with { EffectiveAt = slice.EffectiveAt.AddDays(1) }, slice with { ChangeIds = [Guid.NewGuid()] },
            slice with { Input = slice.Input with { Pricing = JsonSerializer.SerializeToElement(new { drivers = new[] { new { id = Guid.NewGuid(), age = 35 } } }) } } })
            Assert.NotEqual(original.ContentHash, ServicingRatingInput.Encode(input with { Slices = [changed] }).ContentHash);
    }

    [Fact]
    public void RejectsTamperingAndClosedContractViolationsEvenWithRecomputedHash()
    {
        var encoded = ServicingRatingInput.Encode(Input());
        Assert.Throws<ArgumentException>(() => ServicingRatingInput.Read(encoded.Json.Replace("15", "16", StringComparison.Ordinal), encoded.ContentHash));
        void Rejected(string json) => Assert.Throws<ArgumentException>(() => ServicingRatingInput.Read(json,
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
        var root = JsonNode.Parse(encoded.Json)!; root["unexpected"] = true; Rejected(root.ToJsonString());
        root = JsonNode.Parse(encoded.Json)!; root.AsObject().Remove("revisionId"); Rejected(root.ToJsonString());
        root = JsonNode.Parse(encoded.Json)!; root["slices"]![0]!["input"]!["rating"]!["injectedPremium"] = 1; Rejected(root.ToJsonString());
        Rejected(encoded.Json[..^1] + ",\"fee\":15}");
    }

    [Fact]
    public void PersistedInputIsBoundedAndRequiresNestedFactsWithoutStringNumberCoercion()
    {
        Assert.Throws<ArgumentException>(() => ServicingRatingInput.Read(new string(' ', ServicingRatingInput.MaximumBytes + 1), new byte[32]));
        var encoded = ServicingRatingInput.Encode(Input());
        foreach (var missing in new[] { true, false })
        {
            var root = JsonNode.Parse(encoded.Json)!;
            if (missing) root["slices"]![0]!["input"]!["rating"]!.AsObject().Remove("hasClaims");
            else root["commissionBasisPoints"] = "1000";
            var json = root.ToJsonString();
            Assert.Throws<ArgumentException>(() => ServicingRatingInput.Read(json,
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
        }
    }

    [Fact]
    public void RejectsEmptyPinsInvalidHashUnboundedFeeMismatchedTermsAndInvalidDefinitions()
    {
        var input = Input();
        foreach (var changed in new[] { input with { RevisionId = Guid.Empty }, input with { RevisionContentHash = "bad" },
            input with { Fee = 0.001m }, input with { Slices = [] }, input with { Format = "unknown" },
            input with { Slices = [input.Slices[0] with { Input = input.Slices[0].Input with { Drivers = [] } }] },
            input with { RatingDefinition = JsonSerializer.SerializeToElement(new { kind = "rating" }) },
            input with { RequestedAt = input.RequestedAt.ToOffset(TimeSpan.FromHours(1)) },
            input with { Slices = [input.Slices[0] with { Input = input.Slices[0].Input with { Term = input.Term with { EndsAt = input.Term.EndsAt.AddDays(1) } } }] } })
            Assert.Throws<ArgumentException>(() => ServicingRatingInput.Encode(changed));
    }
}
