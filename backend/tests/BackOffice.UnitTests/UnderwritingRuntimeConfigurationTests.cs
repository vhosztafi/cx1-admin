using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Agencies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class UnderwritingRuntimeConfigurationTests
{
    private static JsonObject Settings(int count = 1) => JsonSerializer.SerializeToNode(new {
        demo = true, kind = "underwriting-runtime", schemaVersion = "1", scenarioVersionId = Guid.NewGuid(), routingTeamId = Guid.NewGuid(),
        products = Enumerable.Range(0, count).Select(_ => new { productVersionId = Guid.NewGuid(), ratingRuleVersionId = Guid.NewGuid(), binderVersionId = Guid.NewGuid(), authorityVersionId = Guid.NewGuid() })
    })!.AsObject();

    [Fact]
    public void EmptyProductsRevokeAndEveryPinnedIdentifierIsMandatory()
    {
        Assert.Empty(UnderwritingRuntimeConfiguration.Parse(Settings(0).ToJsonString())!.Products);
        Assert.Single(UnderwritingRuntimeConfiguration.Parse(Settings().ToJsonString())!.Products);
        foreach (var field in new[] { "productVersionId", "ratingRuleVersionId", "binderVersionId", "authorityVersionId" })
        {
            var settings = Settings(); settings["products"]![0]![field] = Guid.Empty.ToString();
            Assert.Null(UnderwritingRuntimeConfiguration.Parse(settings.ToJsonString()));
            settings["products"]![0]!.AsObject().Remove(field);
            Assert.Null(UnderwritingRuntimeConfiguration.Parse(settings.ToJsonString()));
        }
        foreach (var field in new[] { "scenarioVersionId", "routingTeamId" })
        {
            var settings = Settings(); settings[field] = Guid.Empty.ToString();
            Assert.Null(UnderwritingRuntimeConfiguration.Parse(settings.ToJsonString()));
        }
    }

    [Fact]
    public void DuplicateUnknownAndOversizedConfigurationFailsClosed()
    {
        var settings = Settings(); var rows = settings["products"]!.AsArray(); rows.Add(rows[0]!.DeepClone());
        Assert.Null(UnderwritingRuntimeConfiguration.Parse(settings.ToJsonString()));
        settings = Settings(); settings["fallback"] = true;
        Assert.Null(UnderwritingRuntimeConfiguration.Parse(settings.ToJsonString()));
        Assert.Null(UnderwritingRuntimeConfiguration.Parse(Settings().ToJsonString().Replace("\"demo\":true", "\"demo\":true,\"demo\":true")));
        Assert.Equal(32, UnderwritingRuntimeConfiguration.Parse(Settings(32).ToJsonString())!.Products.Count);
        Assert.Null(UnderwritingRuntimeConfiguration.Parse(Settings(33).ToJsonString()));
        Assert.Null(UnderwritingRuntimeConfiguration.Parse("[]"));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("reject")]
    [InlineData("fail-once")]
    [InlineData("timeout-after-success")]
    public void OnlyExplicitDemoScenariosCanDispatch(string scenario)
    {
        var json = JsonSerializer.Serialize(new { demo = true, kind = "quote-rating", scenario });
        Assert.Equal(scenario, UnderwritingRuntimeConfiguration.Scenario(json));
        Assert.Null(UnderwritingRuntimeConfiguration.Scenario(json.Replace("true", "false")));
        Assert.Null(UnderwritingRuntimeConfiguration.Scenario(json.Replace(scenario, "live")));
        Assert.Null(UnderwritingRuntimeConfiguration.Scenario(json.Replace("\"demo\":true", "\"demo\":true,\"demo\":true")));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void CaptureAndDistributionRetainBoundedMultipleVersions(int count, bool valid)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var capture = JsonSerializer.Serialize(new { demo = true, kind = "quote-capture", products = ids.Select(id => new {
            productVersionId = id, schemaVersion = "1.0", questionSetVersion = QuoteCatalogueIdentity.Version, referenceVersion = QuoteCatalogueIdentity.Version }) });
        var distribution = JsonSerializer.Serialize(new { demo = true, kind = "agency-distribution", productVersionIds = ids });
        Assert.Equal(valid, QuoteCaptureConfiguration.Parse(capture) is not null);
        Assert.Equal(valid, AgencyDistributionRules.Parse(distribution) is not null);
    }
}
