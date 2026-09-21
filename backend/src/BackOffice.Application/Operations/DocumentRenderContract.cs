using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Operations;

// IDs and template ownership are loaded by the scoped SQL caller, never taken
// as proof of ownership from an HTTP request. Policy/servicing hashes cover
// stored UTF-8; quote hashes also bind the saved configuration envelope.
public sealed record DocumentRenderInput(Guid SourceId, string SourceKind, string SourceJson, string SourceHash,
    Guid TemplateId, string TemplateJson, string TemplateHash, string ProductCode, string Kind, string Reference,
    string TemplateProductCode, string TemplateKind, QuoteVersionPins? QuotePins = null, DocumentQuoteTerms? QuoteTerms = null);

public sealed record DocumentQuoteTerms(Guid Id, string Json, string Hash);

public sealed class DocumentRenderException(string code) : Exception("The document source or template cannot be rendered.")
{
    public string Code { get; } = code;
}

public sealed class DocumentRenderContract
{
    public const int MaximumSourceBytes = 8 * 1024 * 1024;
    public const int MaximumTemplateBytes = 16 * 1024;
    public const int MaximumValues = 30000;
    public DocumentRenderInput Input { get; }
    public JsonElement Source { get; }
    public string Title { get; }
    public string Notice { get; }
    public bool LegacyTemplate { get; }
    public JsonElement? QuotationTerms { get; }

    private DocumentRenderContract(DocumentRenderInput input, JsonElement source, string title, string notice, bool legacy, JsonElement? terms)
        => (Input, Source, Title, Notice, LegacyTemplate, QuotationTerms) = (input, source.Clone(), title, notice, legacy, terms);

    public static DocumentRenderContract Create(DocumentRenderInput input)
    {
        try
        {
            if (input is null || input.SourceId == Guid.Empty || input.TemplateId == Guid.Empty ||
                input.SourceKind is not ("policy-version" or "quote-revision" or "servicing-terms") || input.ProductCode is not ("motor-trade-road-risks" or "motor-trade-combined" or "commercial-combined") ||
                input.Kind is not ("policy-schedule" or "statement-of-fact" or "policy-statement" or "policy-certificate" or "endorsement" or "cancellation-notice" or "quotation" or "renewal-invitation") ||
                input.TemplateProductCode != input.ProductCode || !TemplateKindMatches(input) || !Text(input.Reference, 100, false))
                throw Invalid("document-render-identity");
            using var source = ReadSource(input);
            using var template = Read(input.TemplateJson, input.TemplateHash, MaximumTemplateBytes);
            var root = source.RootElement; var definition = template.RootElement;
            var quote = input.SourceKind == "quote-revision";
            var servicing = input.SourceKind == "servicing-terms";
            var policy = input.SourceKind == "policy-version";
            if (servicing) DocumentServicingTermsRules.Validate(input, root, definition);
            else if (root.GetProperty("productCode").GetString() != input.ProductCode ||
                (quote ? (input.ProductCode == "commercial-combined" ? QuoteCaptureShape.ValidateCommercial(root) : QuoteCaptureShape.Validate(root)).Count != 0 : !PolicySnapshotShape.Valid(root)))
                throw Invalid("document-render-source");
            if (quote && input.Kind is not ("statement-of-fact" or "quotation") || policy && input.Kind is "quotation" or "renewal-invitation") throw Invalid("document-render-kind-not-applicable");
            if ((!quote || input.Kind != "quotation") && input.QuoteTerms is not null) throw Invalid("document-render-unexpected-terms");
            var cancelled = policy && root.GetProperty("snapshotFormat").GetString() is "issued-cancellation-1" or "issued-commercial-cancellation-1";
            if (cancelled != (input.Kind == "cancellation-notice")) throw Invalid("document-render-kind-not-applicable");
            if (input.ProductCode == "commercial-combined" && input.Kind == "policy-certificate" &&
                root.GetProperty("cover").GetProperty("sections").EnumerateArray().Count(x => x.GetProperty("code").GetString() == "employers-liability") != 1)
                throw Invalid("document-render-kind-not-applicable");

            var format = definition.GetProperty("format").GetString();
            var legacy = format == (quote ? "quote-template-1" : servicing ? root.GetProperty("format").GetString() == "renewal-contract-1" ? "renewal-template-1" : "servicing-template-1" : "policy-template-1");
            if (!legacy && format != "document-template-1") throw Invalid("document-render-template");
            string[] fields = legacy ? ["format", "title", "notice"] : ["format", "title", "notice", "productCode", "kind"];
            if (definition.EnumerateObject().Any(x => !fields.Contains(x.Name, StringComparer.Ordinal)) ||
                definition.EnumerateObject().Count() != fields.Length ||
                !legacy && (definition.GetProperty("productCode").GetString() != input.ProductCode || CanonicalKind(definition.GetProperty("kind").GetString()!) != CanonicalKind(input.Kind)))
                throw Invalid("document-render-template");
            var title = definition.GetProperty("title").GetString(); var notice = definition.GetProperty("notice").GetString();
            if (!Text(title, 200, false) || !Text(notice, 4000, true) || title!.IndexOfAny(['<', '>']) >= 0 || notice!.IndexOfAny(['<', '>']) >= 0)
                throw Invalid("document-render-template-text");
            var terms = quote && input.Kind == "quotation" ? DocumentQuoteTermsRules.Validate(input, root, definition) : (JsonElement?)null;
            return new(input, root, title, notice, legacy, terms);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or QuoteInputException)
        { throw Invalid("document-render-invalid"); }
    }

    private static bool TemplateKindMatches(DocumentRenderInput input) =>
        CanonicalKind(input.TemplateKind) == CanonicalKind(input.Kind) ||
        input.SourceKind == "quote-revision" && input.TemplateKind == "quote-terms" && input.Kind is "statement-of-fact" or "quotation" ||
        input.SourceKind == "servicing-terms" && (input.TemplateKind == "servicing-terms" && input.Kind is "statement-of-fact" or "quotation" || input.TemplateKind == "renewal-invitation" && input.Kind == "statement-of-fact");

    private static JsonDocument ReadSource(DocumentRenderInput input)
    {
        if (input.SourceKind == "quote-revision")
        {
            if (input.QuotePins is null || QuoteCanonicalJson.Create(input.SourceJson, input.QuotePins).ContentHash != input.SourceHash)
                throw Invalid("document-render-quote-hash");
            return Read(input.SourceJson, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input.SourceJson))), MaximumSourceBytes);
        }
        if (input.QuotePins is not null) throw Invalid("document-render-unexpected-pins");
        return Read(input.SourceJson, input.SourceHash, MaximumSourceBytes);
    }

    internal static JsonDocument Read(string json, string hash, int maximum)
    {
        if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > maximum || hash is null || hash.Length != 64 ||
            hash.Any(c => !char.IsAsciiDigit(c) && c is not (>= 'a' and <= 'f')) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(json)), Convert.FromHexString(hash)))
            throw Invalid("document-render-hash");
        var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        try
        {
            var count = 0;
            void Visit(JsonElement value)
            {
                if (++count > MaximumValues) throw Invalid("document-render-size");
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in value.EnumerateObject())
                    {
                        if (!Text(property.Name, 200, false)) throw Invalid("document-render-property-name");
                        if (!keys.Add(property.Name)) throw Invalid("document-render-duplicate-property");
                        Visit(property.Value);
                    }
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    if (value.GetArrayLength() > 2000) throw Invalid("document-render-size");
                    foreach (var child in value.EnumerateArray()) Visit(child);
                }
                else if (value.ValueKind == JsonValueKind.String && !Text(value.GetString(), 16000, true, allowEmpty: true))
                    throw Invalid("document-render-text");
            }
            Visit(parsed.RootElement);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw Invalid("document-render-object");
            return parsed;
        }
        catch { parsed.Dispose(); throw; }
    }

    private static bool Text(string? value, int maximum, bool multiline, bool allowEmpty = false)
        => value is not null && value.Length <= maximum && (allowEmpty || !string.IsNullOrWhiteSpace(value)) &&
           !value.Any(c => char.IsControl(c) && !(multiline && c is '\n' or '\r' or '\t') || c is '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or '\u2066' or '\u2067' or '\u2068' or '\u2069');
    private static DocumentRenderException Invalid(string code) => new(code);
    public static string CanonicalKind(string kind) => kind == "policy-statement" ? "statement-of-fact" : kind;
}
