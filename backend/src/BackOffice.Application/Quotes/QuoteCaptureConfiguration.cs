using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteCaptureVersion(Guid ProductVersionId, string SchemaVersion, string QuestionSetVersion, string ReferenceVersion);

// This explicit demo capture grant is separate from rating/publication authority.
// An empty product set revokes capture. Invalid current settings never fall back.
public static class QuoteCaptureConfiguration
{
    public static IReadOnlyDictionary<Guid, QuoteCaptureVersion>? Parse(string json)
    {
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(json) > 16384) return null;
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (!Keys(root, "demo", "kind", "products") || root.GetProperty("demo").ValueKind != JsonValueKind.True ||
                root.GetProperty("kind").ValueKind != JsonValueKind.String || root.GetProperty("kind").GetString() != "quote-capture") return null;
            var products = root.GetProperty("products");
            if (products.ValueKind != JsonValueKind.Array || products.GetArrayLength() > 32) return null;
            var result = new Dictionary<Guid, QuoteCaptureVersion>();
            foreach (var product in products.EnumerateArray())
            {
                if (!Keys(product, "productVersionId", "schemaVersion", "questionSetVersion", "referenceVersion")) return null;
                var idValue = product.GetProperty("productVersionId");
                if (idValue.ValueKind != JsonValueKind.String || !Guid.TryParseExact(idValue.GetString(), "D", out var id) || id == Guid.Empty) return null;
                foreach (var key in new[] { "schemaVersion", "questionSetVersion", "referenceVersion" })
                    if (product.GetProperty(key).ValueKind != JsonValueKind.String) return null;
                var schema = product.GetProperty("schemaVersion").GetString()!;
                var questions = product.GetProperty("questionSetVersion").GetString()!;
                var references = product.GetProperty("referenceVersion").GetString()!;
                var supported = (schema == "1.0" && questions == QuoteCatalogueIdentity.Version && references == QuoteCatalogueIdentity.Version)
                    || CommercialCaptureRules.Accepts(schema, questions, references);
                if (!supported ||
                    !result.TryAdd(id, new(id, schema, questions, references))) return null;
            }
            return result.ToFrozenDictionary();
        }
        catch (JsonException) { return null; }
        catch (EncoderFallbackException) { return null; }
    }

    private static bool Keys(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!expected.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;
        return seen.Count == expected.Length;
    }
}
