using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteCaptureConfigurationTests
{
    private static JsonObject Valid() => new()
    {
        ["demo"] = true, ["kind"] = "quote-capture", ["products"] = new JsonArray(new JsonObject
        {
            ["productVersionId"] = "aaaaaaaa-0000-4000-8000-000000000001", ["schemaVersion"] = "1.0",
            ["questionSetVersion"] = QuoteCatalogueIdentity.Version, ["referenceVersion"] = QuoteCatalogueIdentity.Version
        })
    };

    [Fact]
    public void PinsAreExplicitAndEmptyConfigurationRevokesCapture()
    {
        var input = Valid(); var config = QuoteCaptureConfiguration.Parse(input.ToJsonString());
        var pin = Assert.Single(config!).Value;
        Assert.Equal(QuoteCatalogueIdentity.Version, pin.QuestionSetVersion);
        Assert.Equal(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"), pin.ProductVersionId);
        input["products"] = new JsonArray(); Assert.Empty(QuoteCaptureConfiguration.Parse(input.ToJsonString())!);
    }

    [Theory]
    [InlineData("demo")]
    [InlineData("kind")]
    [InlineData("products")]
    public void MissingRootPropertiesFailClosed(string key)
    {
        var input = Valid(); input.Remove(key); Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
    }

    [Theory]
    [InlineData("productVersionId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("productVersionId", "aaaaaaaa000040008000000000000001")]
    [InlineData("schemaVersion", "2.0")]
    [InlineData("questionSetVersion", "demo-1")]
    [InlineData("referenceVersion", "stale")]
    public void UnsupportedOrInvalidPinsFailClosed(string key, string value)
    {
        var input = Valid(); input["products"]![0]![key] = value; Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
    }

    [Fact]
    public void DuplicateUnknownMistypedAndOversizedConfigurationFailsClosed()
    {
        var input = Valid(); input["unknown"] = true; Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
        input = Valid(); input["products"]![0]!["ratingAvailable"] = true; Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
        input = Valid(); input["demo"] = false; Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
        input = Valid(); input["products"]![0]!["schemaVersion"] = 1; Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
        input = Valid(); input["products"]!.AsArray().Add(input["products"]![0]!.DeepClone()); Assert.Null(QuoteCaptureConfiguration.Parse(input.ToJsonString()));
        Assert.Null(QuoteCaptureConfiguration.Parse(Valid().ToJsonString().Replace("\"demo\":true", "\"demo\":true,\"demo\":true")));
        var validJson = Valid().ToJsonString();
        Assert.NotNull(QuoteCaptureConfiguration.Parse(validJson.PadLeft(16384)));
        Assert.Null(QuoteCaptureConfiguration.Parse(validJson.PadLeft(16385)));
        Assert.Null(QuoteCaptureConfiguration.Parse("[]")); Assert.Null(QuoteCaptureConfiguration.Parse("{"));
    }
}
