using System.Text.Json;
using BackOffice.Application.Quotes;
using Json.Schema;

namespace BackOffice.Application.Policies;

public static class PolicySnapshotShape
{
    private static readonly Lazy<JsonSchema> Schema = new(() => QuoteCaptureShape.BuildBundled("Policy.SnapshotSchema"));
    private static readonly Lazy<JsonSchema> CommercialSchema = new(() => QuoteCaptureShape.BuildBundled("CommercialPolicy.SnapshotSchema"));
    private static readonly Lazy<JsonSchema> ServicingSchema = new(() => QuoteCaptureShape.BuildBundled("Servicing.SnapshotSchema"));
    private static readonly Lazy<JsonSchema> CancellationSchema = new(() => QuoteCaptureShape.BuildBundled("Cancellation.SnapshotSchema"));
    public static bool Valid(JsonElement snapshot) => Errors(snapshot).Count == 0;
    public static IReadOnlyList<string> Errors(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object) return ["snapshot-object-required"];
        var format = snapshot.TryGetProperty("snapshotFormat",out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (format is not ("issued-quote-1" or "issued-commercial-1" or "issued-servicing-1" or "issued-cancellation-1")) return ["unsupported-snapshot-format"];
        var schema = format switch { "issued-commercial-1" => CommercialSchema.Value, "issued-servicing-1" => ServicingSchema.Value, "issued-cancellation-1" => CancellationSchema.Value, _ => Schema.Value };
        var result = schema.Evaluate(snapshot, new EvaluationOptions { RequireFormatValidation = true, OutputFormat = OutputFormat.Hierarchical });
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
