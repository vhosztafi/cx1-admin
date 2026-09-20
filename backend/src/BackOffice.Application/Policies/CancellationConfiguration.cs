using System.Text.Json;

namespace BackOffice.Application.Policies;

public sealed record CancellationSettings(string RuleVersion, IReadOnlyList<string> AuthorityVersions, IReadOnlyList<string> SeniorAuthorityVersions);

public static class CancellationConfiguration
{
    public const string Scope = "cancellation-review";
    public const string CommercialScope = "commercial-cancellation-review";
    public const string CommercialDemoJson = """{"demo":true,"kind":"commercial-cancellation-review","schemaVersion":"1","ruleVersion":"demo-servicing-1","authorityVersions":["commercial-demo-senior-1"],"seniorAuthorityVersions":["commercial-demo-senior-1"]}""";
    public const string DemoJson = """{"demo":true,"kind":"cancellation-review","schemaVersion":"1","ruleVersion":"demo-servicing-1","authorityVersions":["demo-underwriter-1","demo-senior-1"],"seniorAuthorityVersions":["demo-senior-1"]}""";

    public static CancellationSettings? Parse(string json,string scope=Scope)
    {
        if (json is null || json.Length > 8192 || scope is not(Scope or CommercialScope)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 3 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var expected = new HashSet<string>(["demo", "kind", "schemaVersion", "ruleVersion", "authorityVersions", "seniorAuthorityVersions"], StringComparer.Ordinal);
            foreach (var p in root.EnumerateObject()) if (!expected.Remove(p.Name)) return null;
            if (expected.Count != 0 || root.GetProperty("demo").ValueKind != JsonValueKind.True ||
                Text("kind") != scope || Text("schemaVersion") != "1" || Text("ruleVersion") != CancellationReviewRules.Version) return null;
            var grants = Versions("authorityVersions"); var senior = Versions("seniorAuthorityVersions");
            if (grants is null || senior is null || senior.Any(x => !grants.Contains(x))) return null;
            return new(CancellationReviewRules.Version, grants, senior);

            string? Text(string name) => root.GetProperty(name).ValueKind == JsonValueKind.String ? root.GetProperty(name).GetString() : null;
            IReadOnlyList<string>? Versions(string name)
            {
                var array = root.GetProperty(name);
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 20) return null;
                var values = new List<string>();
                foreach (var value in array.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= 60 } text ||
                        text.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || values.Contains(text)) return null;
                    values.Add(text);
                }
                return values.AsReadOnly();
            }
        }
        catch (JsonException) { return null; }
    }
}
