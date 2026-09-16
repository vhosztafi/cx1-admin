using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class UnderwritingHashTests
{
    [Fact]
    public void EqualRiskUnderAnotherOwnerOrRevisionCannotReusePricing()
    {
        var owner = new UnderwritingOwnership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", "questions-1", "references-1");
        using var input = JsonDocument.Parse("{\"drivers\":[{\"id\":\"b\",\"age\":35},{\"id\":\"a\",\"age\":40}],\"stock\":150000}");
        using var reordered = JsonDocument.Parse("{\"stock\":150000.00,\"drivers\":[{\"age\":40,\"id\":\"a\"},{\"age\":35,\"id\":\"b\"}]}");
        var original = UnderwritingHashes.Pricing(owner, pins, input.RootElement).ContentHash;
        Assert.Equal(original, UnderwritingHashes.Pricing(owner, pins, reordered.RootElement).ContentHash);
        foreach (var changed in new[] { owner with { QuoteId = Guid.NewGuid() }, owner with { RevisionId = Guid.NewGuid() },
            owner with { ClientId = Guid.NewGuid() }, owner with { RelationshipId = Guid.NewGuid() }, owner with { BinderVersionId = Guid.NewGuid() } })
            Assert.NotEqual(original, UnderwritingHashes.Pricing(changed, pins, input.RootElement).ContentHash);
        Assert.NotEqual(original, UnderwritingHashes.Pricing(owner, pins with { AgencyTermsVersionId = Guid.NewGuid() }, input.RootElement).ContentHash);
    }

    [Fact]
    public void EvidenceReviewChangesAssuranceWithoutChangingPricingOrPreparedTerms()
    {
        var pins = new QuoteVersionPins(Guid.NewGuid(), Guid.NewGuid(), "1.0", "questions-1", "references-1");
        var cycle = Guid.NewGuid(); var rating = Guid.NewGuid();
        using var terms = JsonDocument.Parse("{\"premium\":\"600.00\",\"endorsements\":[]}");
        var termHash = UnderwritingHashes.Terms(cycle, rating, pins, terms.RootElement);
        var proof = new UnderwritingAssuranceItem(Guid.NewGuid(), "evidence", null, "unreviewed", new string('a', 64));
        var before = UnderwritingHashes.Assurance(cycle, pins, [proof]);
        var after = UnderwritingHashes.Assurance(cycle, pins, [proof with { LatestDecisionId = Guid.NewGuid(), State = "accepted" }]);
        Assert.NotEqual(before, after);
        Assert.Equal(termHash, UnderwritingHashes.Terms(cycle, rating, pins, terms.RootElement));
        Assert.NotEqual(termHash, UnderwritingHashes.Terms(cycle, Guid.NewGuid(), pins, terms.RootElement));
        Assert.NotEqual(after, UnderwritingHashes.Assurance(Guid.NewGuid(), pins, [proof]));
        Assert.Throws<ArgumentException>(() => UnderwritingHashes.Assurance(cycle, pins, [proof, proof]));
    }
}
