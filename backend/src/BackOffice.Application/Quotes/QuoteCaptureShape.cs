using System.Text.Json;
using Json.Schema;

namespace BackOffice.Application.Quotes;

// Structural capture validation only. Call after bounded, duplicate-safe parsing;
// follow with the pinned question/reference and item-identity gates before save.
// Business completeness, evidence and authority are independent later checks.
public static class QuoteCaptureShape
{
    public const int MaximumIssues = 100;
    private static readonly Lazy<JsonSchema> Draft = new(() => BuildBundled("QuoteCapture.DraftSchema"));

    public static IReadOnlyList<QuoteFieldIssue> Validate(JsonElement proposal)
    {
        var result = Draft.Value.Evaluate(proposal, new EvaluationOptions
        {
            RequireFormatValidation = true,
            OutputFormat = OutputFormat.List
        });
        if (result.IsValid) return [];
        var issues = new List<QuoteFieldIssue>();
        var seen = new HashSet<QuoteFieldIssue>();
        void Collect(EvaluationResults node)
        {
            if (issues.Count >= MaximumIssues) return;
            if (!node.IsValid && node.Errors is not null)
            {
                var path = node.InstanceLocation.ToString();
                // Do not emit raw library messages or unbounded request keys.
                if (path.Length > 1024) path = "";
                foreach (var keyword in node.Errors.Keys.Order(StringComparer.Ordinal))
                {
                    var issue = new QuoteFieldIssue("schema-" + keyword, path);
                    if (seen.Add(issue)) issues.Add(issue);
                    if (issues.Count >= MaximumIssues) return;
                }
            }
            if (node.Details is not null) foreach (var child in node.Details) Collect(child);
        }
        Collect(result);
        // A failed evaluation must never become an empty, apparently valid list.
        if (issues.Count == 0) issues.Add(new("schema-invalid", ""));
        return issues;
    }

    internal static JsonSchema BuildBundled(string resourceName)
    {
        using var stream = typeof(QuoteCaptureShape).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Bundled quote schema is missing.");
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        using (var document = JsonDocument.Parse(text)) CheckReferences(document.RootElement);
        // No fetcher, request-controlled schemas or global registry mutation.
        var registry = new SchemaRegistry { Fetch = (_, _) => throw new InvalidOperationException("External quote schema resolution is forbidden.") };
        return JsonSchema.FromText(text, new BuildOptions { SchemaRegistry = registry });
    }

    private static void CheckReferences(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name is "$ref" or "$dynamicRef" &&
                    (property.Value.ValueKind != JsonValueKind.String || !property.Value.GetString()!.StartsWith("#/$defs/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Quote schema must use bundled local definitions.");
                CheckReferences(property.Value);
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) CheckReferences(child);
    }
}
