using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class ServicingRatingConfigurationTests
{
    private const string Valid = """{"demo":true,"kind":"servicing-rating","schemaVersion":"1","currency":"GBP","adjustmentFee":"15.00","earningBasis":"london-calendar-days"}""";
    [Fact]
    public void ExplicitFictionalFeeIsIndependentOfNewBusinessFee()
        => Assert.Equal(15m, ServicingRatingConfiguration.Parse(Valid)!.AdjustmentFee);

    [Theory]
    [InlineData("-1.00")]
    [InlineData("15.001")]
    [InlineData("15")]
    [InlineData("1e2")]
    [InlineData("10000000000000.00")]
    public void RejectsInexactNegativeOrUnboundedFees(string fee)
    {
        var value = JsonNode.Parse(Valid)!; value["adjustmentFee"] = fee;
        Assert.Null(ServicingRatingConfiguration.Parse(value.ToJsonString()));
    }

    [Fact]
    public void RejectsUnknownDuplicateMissingAndUnsupportedFields()
    {
        var value = JsonNode.Parse(Valid)!; value["extra"] = true;
        Assert.Null(ServicingRatingConfiguration.Parse(value.ToJsonString()));
        Assert.Null(ServicingRatingConfiguration.Parse(Valid[..^1] + ",\"currency\":\"GBP\"}"));
        foreach (var field in new[] { "demo", "kind", "schemaVersion", "currency", "adjustmentFee", "earningBasis" })
        { value = JsonNode.Parse(Valid)!; value.AsObject().Remove(field); Assert.Null(ServicingRatingConfiguration.Parse(value.ToJsonString())); }
        Assert.Null(ServicingRatingConfiguration.Parse(Valid.Replace("GBP", "USD", StringComparison.Ordinal)));
        Assert.Null(ServicingRatingConfiguration.Parse(Valid.Replace("london-calendar-days", "elapsed-hours", StringComparison.Ordinal)));
        Assert.Null(ServicingRatingConfiguration.Parse("null"));
    }
}
