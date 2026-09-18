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

    private static ServicingRatingRequestInput Renewal()
    {
        var basis = Input();
        return basis with { Format = "servicing-rating-input-2", Fee = 35m,
            Renewal = new(Guid.NewGuid(), null, null, null, null, false, "demo-servicing-1", 5000, 800),
            Slices = [basis.Slices[0] with { EffectiveAt = basis.Term.StartsAt, ChangeIds = [] }] };
    }

    [Fact]
    public void RenewalChargesFullTermAndPreservesUnknownExperienceWithoutInventingChanges()
    {
        var input = Renewal(); var encoded = ServicingRatingInput.Encode(input);
        var restored = ServicingRatingInput.Read(encoded.Json, encoded.ContentHash);
        var price = ServicingRatingInput.Calculate(restored);
        Assert.Empty(restored.Slices[0].ChangeIds);
        Assert.Equal(600m, price.Premium); Assert.Equal(0m, price.Slices[0].AnnualDelta);
        Assert.Equal(707m, price.GrossPayable); Assert.Equal(647m, price.NetDue);
        Assert.Null(restored.Renewal!.Experience);
        Assert.False(restored.Renewal.EvidenceAccepted);
        Assert.NotEqual(encoded.ContentHash, ServicingRatingInput.Encode(input with {
            Renewal = input.Renewal! with { PreparationVersionId = Guid.NewGuid() } }).ContentHash);
    }

    [Fact]
    public void RenewalReviewedExperienceLoadsFullPremiumAndPinsEachSource()
    {
        var input = Renewal();
        var facts = new RenewalExperienceFacts(new(2025, 1, 1), new(2026, 1, 1), 2, 600m, 0m, 1000m,
            "agency", "claims statement", Guid.NewGuid());
        input = input with { Renewal = input.Renewal! with { ExperienceVersionId = Guid.NewGuid(),
            ExperienceReviewId = Guid.NewGuid(), Experience = facts, EvidenceAccepted = true } };
        var encoded = ServicingRatingInput.Encode(input); var price = ServicingRatingInput.Calculate(input);
        Assert.Equal(648m, price.Premium); Assert.Equal(48m, price.Slices[0].AnnualDelta);
        Assert.Equal(760.76m, price.GrossPayable);
        foreach (var changed in new[] { input.Renewal! with { ExperienceVersionId = Guid.NewGuid() },
            input.Renewal! with { ExperienceReviewId = Guid.NewGuid() },
            input.Renewal! with { FairValueAssessmentId = Guid.NewGuid() },
            input.Renewal! with { Experience = facts with { Outstanding = 1m } } })
            Assert.NotEqual(encoded.ContentHash, ServicingRatingInput.Encode(input with { Renewal = changed }).ContentHash);
    }

    [Fact]
    public void RenewalRejectsMixedFormatsMissingSourcesAndNonInceptionSchedules()
    {
        var input = Renewal();
        foreach (var changed in new[] { input with { Renewal = null }, input with { Format = "servicing-rating-input-1" },
            input with { Renewal = input.Renewal! with { PreparationVersionId = Guid.Empty } },
            input with { Renewal = input.Renewal! with { EvidenceAccepted = true } },
            input with { Renewal = input.Renewal! with { ExperienceReviewId = Guid.NewGuid() } },
            input with { Slices = [input.Slices[0] with { EffectiveAt = input.Term.StartsAt.AddDays(1) }] },
            input with { Slices = [input.Slices[0], input.Slices[0]] },
            input with { Slices = [input.Slices[0] with { ChangeIds = [Guid.Empty] }] } })
            Assert.Throws<ArgumentException>(() => ServicingRatingInput.Encode(changed));
    }

    [Fact]
    public void ExistingAdjustmentSerializationOmitsRenewalAndRoundTripsByteForByte()
    {
        var encoded = ServicingRatingInput.Encode(Input());
        Assert.DoesNotContain("renewal", encoded.Json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(encoded.Json, ServicingRatingInput.Encode(ServicingRatingInput.Read(encoded.Json, encoded.ContentHash)).Json);
        Assert.Throws<ArgumentException>(() => ServicingRatingInput.Encode(Input() with { Slices = [Input().Slices[0] with { ChangeIds = [] }] }));
    }

    [Theory]
    [InlineData("motor-trade-road-risks", 600)]
    [InlineData("motor-trade-combined", 1200)]
    public void ShortRenewalChargesCivilDayProportionOfFullRiskForBothProducts(string product, decimal annual)
    {
        var input = Renewal(); var term = RenewalPreparationRules.Term(DateTimeOffset.Parse("2027-01-01T00:00:00Z"), 6, [6, 12]).Term;
        input = input with { Term = term, RatingDefinition = QuoteRatingRulesTests.Definition("rating", product),
            BaseAnnualPremium = annual, Slices = [input.Slices[0] with { EffectiveAt = term.StartsAt,
                Input = input.Slices[0].Input with { Term = term, Rating = QuoteRatingRulesTests.Facts(product) } }] };
        var encoded = ServicingRatingInput.Encode(input);
        var price = ServicingRatingInput.Calculate(ServicingRatingInput.Read(encoded.Json, encoded.ContentHash));
        Assert.Equal(decimal.Round(annual * 181 / 365, 2, MidpointRounding.AwayFromZero), price.Premium);
        Assert.Equal(181, price.Slices[0].RemainingDays); Assert.Equal(365, price.Slices[0].AnnualDays);
        Assert.Equal(35m, price.Fee); Assert.Equal(0m, price.Slices[0].AnnualDelta);
    }

    [Fact]
    public void UnreviewedOrRejectedExperienceNeverAppliesApprovedLoading()
    {
        var input = Renewal(); var facts = new RenewalExperienceFacts(new(2025,1,1), new(2026,1,1), 1, 600m, 0m, 1000m,
            "agency", "unreviewed statement", Guid.NewGuid());
        foreach (var reviewId in new Guid?[] { null, Guid.NewGuid() })
        {
            var pending = input with { Renewal = input.Renewal! with { ExperienceVersionId = Guid.NewGuid(),
                ExperienceReviewId = reviewId, Experience = facts } };
            var encoded = ServicingRatingInput.Encode(pending);
            Assert.Equal(600m, ServicingRatingInput.Calculate(ServicingRatingInput.Read(encoded.Json, encoded.ContentHash)).Premium);
        }
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
