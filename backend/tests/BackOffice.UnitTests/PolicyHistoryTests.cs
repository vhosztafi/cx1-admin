using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class PolicyHistoryTests
{
    private static JsonObject Example(string product)
    {
        using var stream=typeof(PolicyHistoryTests).Assembly.GetManifestResourceStream("PolicyExamples.issued-"+product+".json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void CloneRetainsDeclarationsButRemapsRiskIdsAndDropsIssuedStateAndTerm(string product)
    {
        var source=Example(product);var bytes=source.ToJsonString();
        var pins=new QuoteVersionPins(Guid.NewGuid(),Guid.NewGuid(),"1.0",QuoteCatalogueIdentity.Version,QuoteCatalogueIdentity.Version);
        var clone=PolicyHistoryRules.Clone(JsonSerializer.SerializeToElement(source),pins,pins);
        using var document=JsonDocument.Parse(clone.Capture.Input.Json);var root=document.RootElement;
        Assert.False(root.TryGetProperty("premium",out _));Assert.False(root.TryGetProperty("provenance",out _));
        Assert.False(root.TryGetProperty("termIntent",out _));Assert.False(root.TryGetProperty("term",out _));
        Assert.False(root.GetProperty("insured").TryGetProperty("clientId",out _));
        Assert.False(root.GetProperty("cover").TryGetProperty("endorsements",out _));
        Assert.False(root.GetProperty("cover").TryGetProperty("sections",out _));
        Assert.NotEmpty(clone.ItemIds);Assert.All(clone.ItemIds,pair=>Assert.NotEqual(pair.Key,pair.Value));
        Assert.Equal(source["insured"]!["legalName"]?.ToString(),root.GetProperty("insured").TryGetProperty("legalName",out var legalName)?legalName.GetString():null);
        Assert.Equal(bytes,source.ToJsonString());
        var again=PolicyHistoryRules.Clone(JsonSerializer.SerializeToElement(source),pins,pins);
        Assert.Empty(clone.ItemIds.Values.Intersect(again.ItemIds.Values));
    }
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public void CompareTracksStableDriverIdentityWithoutOperationalProvenance(string product)
    {
        var before=Example(product);var after=before.DeepClone();
        var driver=after["risk"]!["drivers"]![0]!;var id=driver["id"]!.GetValue<Guid>();
        driver["fullName"]="Fictional changed driver";driver["firstName"]="Fictional";driver["surname"]="changed driver";
        var changes=PolicyHistoryRules.Compare(JsonSerializer.SerializeToElement(before),JsonSerializer.SerializeToElement(after));
        Assert.Contains(changes,x=>x.ItemId==id&&x.Path.EndsWith("/fullName",StringComparison.Ordinal));
        Assert.DoesNotContain(changes,x=>x.Path.StartsWith("/provenance",StringComparison.Ordinal));
        before["privateAuthorityNote"]="must never appear";
        Assert.Throws<ArgumentException>(()=>PolicyHistoryRules.Compare(JsonSerializer.SerializeToElement(before),JsonSerializer.SerializeToElement(after)));
    }
}
