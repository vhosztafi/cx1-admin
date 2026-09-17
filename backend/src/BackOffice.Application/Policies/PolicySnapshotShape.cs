using System.Text.Json;
using BackOffice.Application.Quotes;
using Json.Schema;

namespace BackOffice.Application.Policies;

public static class PolicySnapshotShape
{
    private static readonly Lazy<JsonSchema> Schema = new(() => QuoteCaptureShape.BuildBundled("Policy.SnapshotSchema"));
    public static bool Valid(JsonElement snapshot) => Schema.Value.Evaluate(snapshot, new EvaluationOptions { RequireFormatValidation = true }).IsValid;
    public static IReadOnlyList<string> Errors(JsonElement snapshot)
    {
        var result = Schema.Value.Evaluate(snapshot, new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.Hierarchical });
        if (result.IsValid) return [];
        var errors = new List<string>();
        void Visit(EvaluationResults node)
        {
            if (node.IsValid || errors.Count >= 20) return;
            if (node.Errors is not null) errors.Add(node.InstanceLocation + ": " + string.Join(",", node.Errors.Keys));
            foreach (var child in node.Details ?? []) Visit(child);
        }
        Visit(result); return errors.Count == 0 ? ["schema-invalid"] : errors;
    }
}
