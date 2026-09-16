using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class UnderwritingAuthorityViewTests
{
    [Fact]
    public void DisplayKeepsStockAndAgeIndependentFromPremiumAndNeverInventsAnActorGrant()
    {
        var binder = QuoteRatingRulesTests.Definition("binder");
        var actor = QuoteRatingRulesTests.Definition("authority");
        var risk = new UnderwritingRisk(600m, 150000m, 30000m, 2, false, false, [1],
            [new(Guid.NewGuid(), 16, 0, false, false)], new Dictionary<string, decimal> { ["road-risks"] = 30000m });
        var rows = UnderwritingAuthorityView.Rows(risk, binder, actor);
        Assert.True(rows.Single(x => x.Code == "premium-limit").ActorAllows);
        Assert.False(rows.Single(x => x.Code == "stock-limit").ActorAllows);
        Assert.Equal("GBP 150000.00", rows.Single(x => x.Code == "stock-limit").Requested);
        Assert.False(rows.Single(x => x.Code.StartsWith("driver-age:")).ActorAllows);
        var missing = UnderwritingAuthorityView.Rows(risk, binder, null);
        Assert.All(missing, x => { Assert.False(x.ActorAllows); Assert.Equal("No current authority grant", x.ActorLimit); });
    }

    [Fact]
    public void UnnamedDriverExperienceIsUnknownAndDoesNotFabricateNamedPeople()
    {
        var config = QuoteRatingRulesTests.Definition("authority");
        var risk = new UnderwritingRisk(600m, 0m, 30000m, 10, false, false, [1], [],
            new Dictionary<string, decimal> { ["road-risks"] = 30000m }) { AnyDriverCount = 2, AnyDriverMinimumAge = 25, AnyDriverMaximumAge = 65 };
        var rows = UnderwritingAuthorityView.Rows(risk, QuoteRatingRulesTests.Definition("binder"), config);
        Assert.Equal("Not established by captured age range", rows.Single(x => x.Code == "any-driver-licence-years").Requested);
        Assert.DoesNotContain(rows, x => x.Code.StartsWith("driver-age:"));
        Assert.False(rows.Single(x => x.Code == "any-driver-licence-years").ActorAllows);
    }
}
