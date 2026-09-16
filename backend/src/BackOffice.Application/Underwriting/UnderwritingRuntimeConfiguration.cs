using System.Text.Json;

namespace BackOffice.Application.Underwriting;

public sealed record UnderwritingRuntimeProduct(Guid ProductVersionId, Guid RatingRuleVersionId, Guid BinderVersionId, Guid AuthorityVersionId);
public sealed record UnderwritingRuntimeSettings(Guid ScenarioVersionId, Guid RoutingTeamId, IReadOnlyDictionary<Guid, UnderwritingRuntimeProduct> Products);

public static class UnderwritingRuntimeConfiguration
{
    public static UnderwritingRuntimeSettings? Parse(string json)
    {
        if (json.Length > 65536) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 }); var root = doc.RootElement;
            if (!Keys(root, "demo", "kind", "schemaVersion", "scenarioVersionId", "routingTeamId", "products") ||
                root.GetProperty("demo").ValueKind != JsonValueKind.True || Text(root, "kind") != "underwriting-runtime" || Text(root, "schemaVersion") != "1") return null;
            var scenario = Id(root, "scenarioVersionId"); var team = Id(root, "routingTeamId");
            if (scenario is null || team is null) return null;
            var rows = root.GetProperty("products"); if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 32) return null;
            var result = new Dictionary<Guid, UnderwritingRuntimeProduct>();
            foreach (var row in rows.EnumerateArray())
            {
                if (!Keys(row, "productVersionId", "ratingRuleVersionId", "binderVersionId", "authorityVersionId")) return null;
                var product = Id(row, "productVersionId"); var rating = Id(row, "ratingRuleVersionId"); var binder = Id(row, "binderVersionId"); var authority = Id(row, "authorityVersionId");
                if (product is null || rating is null || binder is null || authority is null || !result.TryAdd(product.Value, new(product.Value, rating.Value, binder.Value, authority.Value))) return null;
            }
            return new(scenario.Value, team.Value, result);
        }
        catch (JsonException) { return null; }
    }

    public static string? Scenario(string json)
    {
        if (json.Length > 4096) return null;
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            if (!Keys(root, "demo", "kind", "scenario") || root.GetProperty("demo").ValueKind != JsonValueKind.True || Text(root, "kind") != "quote-rating") return null;
            var value = Text(root, "scenario"); return value is "success" or "reject" or "fail-once" or "timeout-after-success" ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement node, string key) => node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static Guid? Id(JsonElement node, string key) => Guid.TryParseExact(Text(node, key), "D", out var value) && value != Guid.Empty ? value : null;
    private static bool Keys(JsonElement node, params string[] expected)
    {
        if (node.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return node.EnumerateObject().All(p => expected.Contains(p.Name) && seen.Add(p.Name)) && seen.Count == expected.Length;
    }
}
