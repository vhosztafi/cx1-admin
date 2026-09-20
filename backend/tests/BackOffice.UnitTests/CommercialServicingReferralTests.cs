using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;
public sealed class CommercialServicingReferralTests
{
    private static JsonNode Proposal() => JsonNode.Parse(typeof(CommercialServicingReferralTests).Assembly.GetManifestResourceStream("CommercialExamples.Ready")!)!;
    private static JsonElement Definition(string kind)
    {
        using var stream = typeof(CommercialServicingReferralTests).Assembly.GetManifestResourceStream("CommercialExamples.Underwriting")!;
        using var document = JsonDocument.Parse(stream); return document.RootElement.GetProperty(kind).Clone();
    }
    [Fact]
    public void TemporaryCommercialLocationExposureRetainsItsOwnDatedReferralAfterRestoration()
    {
        var low = Proposal(); var high = low.DeepClone();
        var id = high["risk"]!["locations"]![0]!["id"]!.GetValue<Guid>();
        high["risk"]!["locations"]![0]!["buildings"] = "2500000.01";
        high["risk"]!["locations"]![0]!["contents"] = "0.00"; high["risk"]!["locations"]![0]!["stock"] = "0.00";
        var first = DateTimeOffset.Parse("2026-10-01T00:00:00Z"); var second = first.AddDays(10);
        var term = QuoteTerm.Assess(JsonSerializer.SerializeToElement(low["termIntent"])).Term!;
        var needs = CommercialServicingReferralRules.Assess(term,
            [new(first, JsonSerializer.SerializeToElement(high), 10000m), new(second, JsonSerializer.SerializeToElement(low), 9000m)], Definition("binder"), Definition("authority"));
        var location = Assert.Single(needs, x => x.RuleCode == "AU-05" && x.RiskItemId == id);
        Assert.All(location.Triggers, trigger => Assert.Equal(first, trigger.EffectiveAt));
        Assert.Contains(location.Triggers, trigger => trigger.Source == "source" && trigger.Requirement.RequestedAmount == 2500000.01m);
        Assert.DoesNotContain(needs, x => x.Dimension.StartsWith("driver", StringComparison.Ordinal));
    }
    [Fact]
    public void ScheduleAndSettingCannotCrossProductBoundaries()
    {
        var proposal = Proposal(); var element = JsonSerializer.SerializeToElement(proposal);
        var term = QuoteTerm.Assess(element.GetProperty("termIntent")).Term!; var date = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        Assert.Throws<ArgumentException>(() => CommercialServicingReferralRules.Assess(term,
            [new(date, element, 9000m), new(date, element, 9000m)], Definition("binder"), Definition("authority")));
        Assert.Throws<ArgumentException>(() => CommercialServicingReferralRules.Assess(term,
            [new(date, element, 9000m)], QuoteRatingRulesTests.Definition("binder"), Definition("authority")));
        var setting = JsonSerializer.Serialize(new { demo=true,kind="commercial-servicing-rating",schemaVersion="1",currency="GBP",adjustmentFee="25.00",earningBasis="london-calendar-days" });
        Assert.Null(ServicingRatingConfiguration.Parse(setting));
        Assert.Equal(25m, ServicingRatingConfiguration.Parse(setting, "commercial-servicing-rating")!.AdjustmentFee);
        Assert.Null(ServicingRatingConfiguration.Parse(setting, "arbitrary"));
    }
}
