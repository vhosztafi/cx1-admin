using System.Text.Json;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class CapacityRulesTests
{
    [Theory]
    [InlineData("2026-09-17T09:00:00Z", "2026-09-21T09:00:00Z")]
    [InlineData("2026-09-19T09:00:00Z", "2026-09-22T09:00:00Z")]
    [InlineData("2026-10-23T09:00:00Z", "2026-10-27T10:00:00Z")]
    public void ResponseDeadlineUsesWorkingDaysAndRetainsLondonClockAcrossDst(string submitted, string expected)
    {
        Assert.Equal(DateTimeOffset.Parse(expected), CapacityRules.ResponseDue(DateTimeOffset.Parse(submitted), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => CapacityRules.ResponseDue(DateTimeOffset.Parse(submitted), 0));
    }
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly CapacityContext Context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 64),
        Now.AddDays(1), Now.AddYears(1), "stock-limit", 150000m);
    private static CapacityDecision Approval => new(Context.QuoteId, Context.CycleId, Context.SubmissionId, Context.SubmissionHash,
        "approve", Now, Now.AddYears(2), [new("stock-limit", 150000m)], []);

    [Fact]
    public void ExtensionRequiresExactSubmissionCycleDimensionAndWholeTermValidity()
    {
        Assert.True(CapacityRules.Applies(Approval, Context, Now));
        Assert.False(CapacityRules.Applies(Approval, Context with { CycleId = Guid.NewGuid() }, Now));
        Assert.False(CapacityRules.Applies(Approval, Context with { SubmissionId = Guid.NewGuid() }, Now));
        Assert.False(CapacityRules.Applies(Approval, Context with { SubmissionHash = new string('b', 64) }, Now));
        Assert.False(CapacityRules.Applies(Approval, Context with { Dimension = "vehicle-limit" }, Now));
        Assert.False(CapacityRules.Applies(Approval, Context with { RequestedAmount = 150000.01m }, Now));
        Assert.False(CapacityRules.Applies(Approval with { ValidTo = Context.EndsAt.AddTicks(-1) }, Context, Now));
        Assert.False(CapacityRules.Applies(Approval with { ValidTo = Now }, Context, Now));
        Assert.False(CapacityRules.Applies(Approval with { Outcome = "query" }, Context, Now));
        Assert.False(CapacityRules.Applies(Approval with { Outcome = "decline" }, Context, Now));
    }

    [Fact]
    public void ClosedExtensionsRejectWildcardAuthorityAndPreserveFinancialPrecision()
    {
        var exact = CapacityRules.Extension(JsonSerializer.SerializeToElement(new { dimension = "stock-limit", maximumAmount = "150000.01" }));
        Assert.Equal(150000.01m, exact.MaximumAmount);
        Assert.Throws<ArgumentException>(() => CapacityRules.Extension(JsonSerializer.SerializeToElement(new { dimension = "stock-limit", maximumAmount = "150000.00", allQuotes = true })));
        Assert.Throws<ArgumentException>(() => CapacityRules.Extension(JsonSerializer.SerializeToElement(new { dimension = "stock-limit", maximumAmount = "150000.001" })));
        Assert.Throws<ArgumentException>(() => CapacityRules.Extension(JsonSerializer.SerializeToElement(new { dimension = "all", permitted = true })));
        Assert.Equal("stock-limit", CapacityRules.Dimension("cover-stock-custody", "cover-restriction"));
        Assert.Equal("tools-limit", CapacityRules.Dimension("cover-tools-equipment", "cover-restriction"));
    }

    [Fact]
    public void TypedDriverAndTradeExtentCannotWaiveOtherNamedRequirements()
    {
        var age = Context with { Dimension = "driver-age", RequestedAmount = null, MinimumAge = 21, MaximumAge = 70 };
        var decision = Approval with { Extensions = [new("driver-age", MinimumAge: 21, MaximumAge: 70)] };
        Assert.True(CapacityRules.Applies(decision, age, Now));
        Assert.False(CapacityRules.Applies(decision, age with { MinimumAge = 20 }, Now));
        Assert.False(CapacityRules.Applies(decision, age with { Dimension = "licence-years" }, Now));
        var trade = Context with { Dimension = "trade-restriction", RequestedAmount = null, QuestionId = "salvage-breaking" };
        var tradeDecision = Approval with { Extensions = [new("trade-restriction", QuestionId: "salvage-breaking", Permitted: true)] };
        Assert.True(CapacityRules.Applies(tradeDecision, trade, Now));
        Assert.False(CapacityRules.Applies(tradeDecision, trade with { QuestionId = "trade-8" }, Now));
    }
}
