using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class RenewalPreparationSelectionTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-09-18T12:00:00Z");
    private static PolicyTerm Term()=>new(){PolicyId=Guid.NewGuid(),StartsAt=DateTimeOffset.Parse("2026-01-01T00:00:00Z"),EndsAt=DateTimeOffset.Parse("2027-01-01T00:00:00Z")};
    private static PolicyTemporalCandidate Version(PolicyTerm term,int sequence,DateTimeOffset effective,string kind="adjustment")=>
        new(term.PolicyId,term.Id,Guid.NewGuid(),term.StartsAt,term.EndsAt,effective,Now,sequence,1,kind);

    [Fact]
    public void RenewalPreparationSelectionUsesKnownFutureAdjustedRiskStrictlyBeforeExpiry()
    {
        var term=Term();var first=Version(term,1,term.StartsAt,"new-business");var future=Version(term,2,term.EndsAt.AddDays(-1));
        var boundary=Version(term,3,term.EndsAt);var unknown=future with{VersionId=Guid.NewGuid(),TransactionSequence=4,ProcessedAt=Now.AddMinutes(1)};
        var selected=PolicyTemporalSelector.RenewalBase([unknown,boundary,first,future],term,term.EndsAt.AddYears(1),[term],Now);
        Assert.Equal(future.VersionId,selected.VersionId);
    }

    [Fact]
    public void RenewalPreparationSelectionRejectsKnownCancellationBeforeExpiry()
    {
        var term=Term();var first=Version(term,1,term.StartsAt,"new-business");var cancellation=Version(term,2,term.EndsAt.AddDays(-1),"cancellation");
        Assert.Equal("renewal-expiring-term-cancelled",Assert.Throws<QuoteOperationException>(()=>PolicyTemporalSelector.RenewalBase([first,cancellation],term,term.EndsAt.AddYears(1),[term],Now)).Code);
        Assert.Equal(first.VersionId,PolicyTemporalSelector.RenewalBase([first,cancellation with{EffectiveAt=term.EndsAt}],term,term.EndsAt.AddYears(1),[term],Now).VersionId);
    }

    [Fact]
    public void RenewalPreparationSelectionRejectsSamePolicyOverlapButAllowsAdjacentAndForeignTerms()
    {
        var term=Term();var first=Version(term,1,term.StartsAt,"new-business");var nextEnd=term.EndsAt.AddYears(1);
        var other=new PolicyTerm{PolicyId=term.PolicyId,StartsAt=term.EndsAt,EndsAt=nextEnd};
        Assert.Equal("renewal-term-overlap",Assert.Throws<QuoteOperationException>(()=>PolicyTemporalSelector.RenewalBase([first],term,nextEnd,[term,other],Now)).Code);
        other.StartsAt=nextEnd;other.EndsAt=nextEnd.AddYears(1);
        Assert.Equal(first.VersionId,PolicyTemporalSelector.RenewalBase([first],term,nextEnd,[term,other],Now).VersionId);
        other.PolicyId=Guid.NewGuid();other.StartsAt=term.EndsAt;
        Assert.Equal(first.VersionId,PolicyTemporalSelector.RenewalBase([first],term,nextEnd,[term,other],Now).VersionId);
    }
}
