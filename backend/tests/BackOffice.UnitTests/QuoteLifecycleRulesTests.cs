using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteLifecycleRulesTests
{
    private static QuoteVersionPins Pins => new(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"),
        Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"), "1.0", QuoteCatalogueIdentity.Version, QuoteCatalogueIdentity.Version);

    [Fact]
    public void ClonesEveryComposedFixtureWithFreshItemsAndAllTypedLinksRemapped()
    {
        var names = typeof(QuoteLifecycleRulesTests).Assembly.GetManifestResourceNames().Where(x => x.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, names.Length);
        foreach (var name in names)
        {
            using var stream = typeof(QuoteLifecycleRulesTests).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var source = JsonNode.Parse(reader.ReadToEnd())!["proposal"]!;
            var original = source.ToJsonString(); var product = source["productCode"]!.GetValue<string>();
            var destination = Pins with { AgencyTermsVersionId = Guid.NewGuid() };
            var clone = QuoteLifecycleRules.Clone(original, product, Pins, destination);
            Assert.NotEmpty(clone.ItemIds); Assert.Equal(clone.ItemIds.Count, clone.ItemIds.Values.Distinct().Count());
            Assert.DoesNotContain(clone.ItemIds.Values, id => id == Guid.Empty || clone.ItemIds.ContainsKey(id));
            using var document = JsonDocument.Parse(clone.Capture.Input.Json);
            Assert.Empty(QuoteItemIdentity.Validate(document.RootElement));
            // Reverse only the known identity substitutions. All declarations,
            // nested loss/conviction/modification rows and links must survive.
            var restored = clone.Capture.Input.Json;
            foreach (var pair in clone.ItemIds) restored = restored.Replace(pair.Value.ToString("D"), pair.Key.ToString("D"), StringComparison.OrdinalIgnoreCase);
            Assert.True(JsonNode.DeepEquals(source, JsonNode.Parse(restored)));
            Assert.Equal(original, source.ToJsonString());
            Assert.NotEqual(QuoteRules.Prepare(original, product, Pins).Input.ContentHash, clone.Capture.Input.ContentHash);
            var second = QuoteLifecycleRules.Clone(original, product, Pins, destination);
            Assert.DoesNotContain(second.ItemIds.Values, clone.ItemIds.Values.Contains);
        }
    }

    [Fact]
    public void CloneDoesNotCarryUnsupportedProcessStateOrSilentlyUpgradeCaptureConfiguration()
    {
        const string input = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}";
        foreach (var name in new[] { "evidence", "lookupId", "ratingId", "accepted", "verified" })
        {
            var source = JsonNode.Parse(input)!; source[name] = "forged";
            Assert.Throws<QuoteValidationException>(() => QuoteLifecycleRules.Clone(source.ToJsonString(), "motor-trade-road-risks", Pins, Pins));
        }
        foreach (var destination in new[] { Pins with { ProductVersionId = Guid.NewGuid() }, Pins with { QuestionSetVersion = "changed" },
            Pins with { ReferenceVersion = "changed" }, Pins with { SchemaVersion = "2.0" } })
            Assert.Equal("quote-clone-configuration-mismatch", Assert.Throws<QuoteInputException>(() => QuoteLifecycleRules.Clone(input, "motor-trade-road-risks", Pins, destination)).Code);
    }

    [Fact]
    public void ReorderingItemsKeepsTheirIdentityAndReportsOnlyActualFieldChanges()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var left = JsonDocument.Parse(JsonSerializer.Serialize(new { risk = new { drivers = new[] { new { id = a, name = "Alex" }, new { id = b, name = "Blair" } } } }));
        using var right = JsonDocument.Parse(JsonSerializer.Serialize(new { risk = new { drivers = new[] { new { id = b, name = "Blair" }, new { id = a, name = "Alex revised" } } } }));
        var changes = QuoteRevisionDiff.Compare(left.RootElement, right.RootElement);
        Assert.Equal(2, changes.Count(x => x.Kind == "reordered"));
        var edit = Assert.Single(changes, x => x.Kind == "changed");
        Assert.Equal(a, edit.ItemId); Assert.Equal("/risk/drivers/0/name", edit.Before!.Path); Assert.Equal("/risk/drivers/1/name", edit.After!.Path);
        Assert.Equal("\"Alex\"", edit.Before.Json); Assert.Equal("\"Alex revised\"", edit.After.Json);
        Assert.DoesNotContain(changes, x => x.Kind is "added" or "removed");
    }

    [Fact]
    public void ComparisonPreservesMissingVersusNullFalseZeroExactMoneyAndCompleteValues()
    {
        using var left = JsonDocument.Parse("{\"flag\":false,\"count\":0,\"money\":\"0.00\",\"removed\":null,\"a/b~\":\"before\"}");
        var text = new string('x', 6000);
        using var right = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?> { ["flag"] = true, ["count"] = 0,
            ["money"] = "0.00", ["added"] = null, ["a/b~"] = text }));
        var changes = QuoteRevisionDiff.Compare(left.RootElement, right.RootElement);
        Assert.Equal(4, changes.Count);
        var removed = Assert.Single(changes, x => x.Kind == "removed"); Assert.Equal("null", removed.Before!.Json); Assert.Null(removed.After);
        var added = Assert.Single(changes, x => x.Kind == "added"); Assert.Null(added.Before); Assert.Equal("null", added.After!.Json);
        Assert.Equal("false", changes.Single(x => x.Path == "/flag").Before!.Json);
        Assert.Equal(JsonSerializer.Serialize(text), changes.Single(x => x.Path == "/a~1b~0").After!.Json);
    }

    [Fact]
    public void RemovedAndAddedItemsHaveTheirOwnSnapshotPathsAndNestedIds()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        using var left = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { id = a, value = 0 } }));
        using var right = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { id = b, value = 0 } }));
        var changes = QuoteRevisionDiff.Compare(left.RootElement, right.RootElement);
        Assert.Equal(2, changes.Count); Assert.Equal(a, changes.Single(x => x.Kind == "removed").ItemId);
        Assert.Equal(b, changes.Single(x => x.Kind == "added").ItemId); Assert.All(changes, x => Assert.Equal("/0", x.Path));
    }

    [Fact]
    public void FutureConsumersMustMatchBothCurrentIdentityAndCanonicalHash()
    {
        var current = new QuoteRevisionToken(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64));
        Assert.True(QuoteLifecycleRules.MatchesCurrent(current, current));
        foreach (var requested in new[] { current with { QuoteId = Guid.NewGuid() }, current with { RevisionId = Guid.NewGuid() },
            current with { ContentHash = new string('b', 64) }, current with { ContentHash = new string('A', 64) }, current with { ContentHash = "a" },
            current with { QuoteId = Guid.Empty }, current with { RevisionId = Guid.Empty } })
            Assert.False(QuoteLifecycleRules.MatchesCurrent(current, requested));
    }
}
