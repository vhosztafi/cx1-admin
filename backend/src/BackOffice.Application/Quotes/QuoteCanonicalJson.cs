using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteVersionPins(Guid ProductVersionId, Guid AgencyTermsVersionId,
    string SchemaVersion, string QuestionSetVersion, string ReferenceVersion);

public sealed record CanonicalQuoteInput(string Json, string ContentHash);

public sealed class QuoteInputException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

// A prerequisite to strict capture validation, not a schema/readiness validator.
// A future service must also validate the closed capture schema, question/reference
// identities and item ownership before using this representation in a revision.
public static class QuoteCanonicalJson
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumDepth = 64;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static CanonicalQuoteInput Create(string json, QuoteVersionPins pins)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(pins);
        if (pins.ProductVersionId == Guid.Empty || pins.AgencyTermsVersionId == Guid.Empty ||
            !ValidVersion(pins.SchemaVersion) || !ValidVersion(pins.QuestionSetVersion) || !ValidVersion(pins.ReferenceVersion))
            throw new ArgumentException("Trusted quote version pins are required.", nameof(pins));
        byte[] bytes;
        try
        {
            if (Utf8.GetByteCount(json) > MaximumBytes) throw new QuoteInputException("quote-input-too-large");
            bytes = Utf8.GetBytes(json);
        }
        catch (EncoderFallbackException) { throw new QuoteInputException("quote-invalid-unicode"); }
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaximumDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new QuoteInputException("quote-object-required");
            using var canonical = new MemoryStream();
            using (var writer = NewWriter(canonical)) Write(document.RootElement, writer);
            if (canonical.Length > MaximumBytes) throw new QuoteInputException("quote-input-too-large");
            var result = Utf8.GetString(canonical.GetBuffer(), 0, checked((int)canonical.Length));

            // Hash an unambiguous envelope, never concatenated strings. The same
            // proposal under changed terms or capture configuration is different.
            using var envelope = new MemoryStream();
            using (var writer = NewWriter(envelope))
            {
                writer.WriteStartObject();
                writer.WriteString("hashFormat", "quote-canonical-1");
                writer.WriteStartObject("configuration");
                writer.WriteString("productVersionId", pins.ProductVersionId);
                writer.WriteString("agencyTermsVersionId", pins.AgencyTermsVersionId);
                writer.WriteString("schemaVersion", pins.SchemaVersion);
                writer.WriteString("questionSetVersion", pins.QuestionSetVersion);
                writer.WriteString("referenceVersion", pins.ReferenceVersion);
                writer.WriteEndObject();
                writer.WritePropertyName("proposal");
                writer.WriteRawValue(result);
                writer.WriteEndObject();
            }
            return new(result, Convert.ToHexStringLower(SHA256.HashData(envelope.GetBuffer().AsSpan(0, checked((int)envelope.Length)))));
        }
        catch (JsonException) { throw new QuoteInputException("quote-invalid-json"); }
        catch (InvalidOperationException) { throw new QuoteInputException("quote-invalid-json"); }
    }

    private static bool ValidVersion(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 100 && value == value.Trim() && value.All(c => c is >= '!' and <= '~');

    // Canonical JSON is stored/transmitted as JSON, never inserted into HTML.
    private static Utf8JsonWriter NewWriter(Stream stream) => new(stream, new JsonWriterOptions
    { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = MaximumDepth + 2 });

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().ToArray();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in properties)
                    if (!names.Add(property.Name)) throw new QuoteInputException("quote-duplicate-property");
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in value.EnumerateArray()) Write(child, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(NormalizeNumber(value.GetRawText()));
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            default:
                value.WriteTo(writer);
                break;
        }
        if (writer.BytesCommitted + writer.BytesPending > MaximumBytes)
            throw new QuoteInputException("quote-input-too-large");
    }

    // Exact base-ten normalization without double/decimal precision loss. JSON
    // numbers with equal mathematical values hash equally, strings stay strings.
    // Bound exponents to avoid expensive downstream numeric materialization.
    private static string NormalizeNumber(string raw)
    {
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0 && (!int.TryParse(raw.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out exponent) || exponent is < -1024 or > 1024))
            throw new QuoteInputException("quote-number-exponent-out-of-range");
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];
        var dot = mantissa.IndexOf('.');
        if (dot >= 0) exponent -= mantissa.Length - dot - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;
        if (exponent is < -1024 or > 1024) throw new QuoteInputException("quote-number-exponent-out-of-range");
        // Keep integral values directly readable by System.Text.Json's integer
        // accessors. Scientific notation is accepted at input, not persisted.
        var magnitude = exponent >= 0 ? trimmed + new string('0', exponent)
            : trimmed.Length + exponent > 0 ? trimmed.Insert(trimmed.Length + exponent, ".")
            : "0." + new string('0', -exponent - trimmed.Length) + trimmed;
        return (negative ? "-" : "") + magnitude;
    }
}
