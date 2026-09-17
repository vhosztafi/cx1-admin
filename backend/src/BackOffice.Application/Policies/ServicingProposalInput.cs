using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BackOffice.Application.Quotes;
using Json.Schema;

namespace BackOffice.Application.Policies;

public sealed record CanonicalServicingProposal(string Json, byte[] ContentHash);

// Capture is deliberately incomplete. Ownership, effective-date authority and
// rating readiness are separate held-scope checks; this parser grants none.
public static class ServicingProposalInput
{
    public const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Lazy<JsonSchema> Schema = new(() => QuoteCaptureShape.BuildBundled("Servicing.ProposalSchema"));

    public static CanonicalServicingProposal Parse(string json, Guid baseVersionId)
    {
        try
        {
            if (Utf8.GetByteCount(json) > MaximumBytes) throw new QuoteInputException("servicing-input-too-large");
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            using var output = new MemoryStream(); var items = 0;
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                Write(doc.RootElement, writer, ref items);
            if (output.Length > MaximumBytes) throw new QuoteInputException("servicing-input-too-large");
            var root = doc.RootElement;
            if (!Schema.Value.Evaluate(root, new EvaluationOptions { RequireFormatValidation = true }).IsValid)
                throw new QuoteInputException("servicing-invalid-proposal");
            if (baseVersionId == Guid.Empty || root.GetProperty("baseVersionId").GetGuid() != baseVersionId)
                throw new QuoteInputException("servicing-base-mismatch");
            if (root.GetProperty("reason").GetString()!.Trim().Length < 10)
                throw new QuoteInputException("servicing-reason-required");
            var ids = new HashSet<Guid>();
            foreach (var change in root.GetProperty("changes").EnumerateArray())
                if (change.GetProperty("changeId").GetGuid() is var id &&
                    (id == Guid.Empty || !ids.Add(id) || change.GetProperty("riskItemId").GetGuid() == Guid.Empty))
                    throw new QuoteInputException("servicing-invalid-change-identity");
            var bytes = output.ToArray();
            return new(Utf8.GetString(bytes), SHA256.HashData(bytes));
        }
        catch (JsonException) { throw new QuoteInputException("servicing-invalid-json"); }
        catch (EncoderFallbackException) { throw new QuoteInputException("servicing-invalid-unicode"); }
        catch (InvalidOperationException) { throw new QuoteInputException("servicing-invalid-json"); }
    }

    private static void Write(JsonElement node, Utf8JsonWriter writer, ref int items)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = node.EnumerateObject().ToArray(); var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in properties)
                    if (!names.Add(property.Name)) throw new QuoteInputException("servicing-duplicate-field");
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(x => x.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(property.Value, writer, ref items); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                items += node.GetArrayLength();
                if (items > 1000) throw new QuoteInputException("servicing-too-many-items");
                writer.WriteStartArray(); foreach (var child in node.EnumerateArray()) Write(child, writer, ref items);
                writer.WriteEndArray(); break;
            case JsonValueKind.Null: throw new QuoteInputException("servicing-null-field");
            case JsonValueKind.Number:
                if (!node.TryGetDecimal(out var number)) throw new QuoteInputException("servicing-invalid-number");
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture)); break;
            case JsonValueKind.String:
                var value = node.GetString()!; _ = Utf8.GetByteCount(value); writer.WriteStringValue(value); break;
            default: node.WriteTo(writer); break;
        }
    }
}
