using System.Text;
using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Api;

public sealed class QuoteHttpException(int status, string code) : Exception("The quote request is invalid.")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record QuoteCreateInput(Guid RelationshipId, Guid ProductVersionId, Guid? MatchSubmissionId, string? Proposal);
public sealed record QuoteSaveInput(string Proposal, string? Reason);

// HTTP transport boundary only. The held-authority service performs product,
// catalogue, capture shape and ownership validation after this bounded parse.
public static class QuoteHttpInput
{
    public static async Task<QuoteCreateInput> CreateAsync(HttpRequest request, CancellationToken token = default)
    {
        using var document = await Read(request, token);
        var root = document.RootElement;
        Keys(root, "relationshipId", "productVersionId", "matchSubmissionId", "proposal");
        var relationship = Id(root, "relationshipId"); var product = Id(root, "productVersionId");
        Guid? match = root.TryGetProperty("matchSubmissionId", out _) ? Id(root, "matchSubmissionId") : null;
        return new(relationship, product, match, Proposal(root, required: false));
    }

    public static async Task<QuoteSaveInput> SaveAsync(HttpRequest request, CancellationToken token = default)
    {
        using var document = await Read(request, token); var root = document.RootElement;
        Keys(root, "proposal", "reason");
        var proposal = Proposal(root, required: true)!;
        string? reason = null;
        if (root.TryGetProperty("reason", out var value))
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 1000)
                throw new QuoteHttpException(422, "invalid-reason");
            reason = value.GetString();
        }
        return new(proposal, reason);
    }

    public static void NoQuery(HttpRequest request)
    {
        if (request.Query.Count != 0) throw new QuoteHttpException(400, "invalid-query");
    }

    public static string Key(HttpRequest request)
    {
        var values = request.Headers["Idempotency-Key"];
        if (values.Count != 1 || values[0] is not { Length: >= 16 and <= 200 } key || key != key.Trim() || key.Any(char.IsControl))
            throw new QuoteHttpException(400, "idempotency-key-required");
        return key;
    }

    public static byte[] Version(HttpRequest request)
    {
        var values = request.Headers.IfMatch;
        if (values.Count == 0) throw new QuoteHttpException(428, "version-required");
        if (values.Count == 1 && values[0] is { Length: 14 } text && text[0] == '"' && text[^1] == '"')
        {
            try
            {
                var bytes = Convert.FromBase64String(text[1..^1]);
                if (bytes.Length == 8 && "\"" + Convert.ToBase64String(bytes) + "\"" == text) return bytes;
            }
            catch (FormatException) { }
        }
        throw new QuoteHttpException(400, "invalid-version");
    }

    private static async Task<JsonDocument> Read(HttpRequest request, CancellationToken token)
    {
        NoQuery(request);
        if (!request.HasJsonContentType()) throw new QuoteHttpException(415, "json-required");
        if (request.ContentLength > QuoteCanonicalJson.MaximumBytes) throw new QuoteHttpException(413, "quote-request-too-large");
        using var body = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, QuoteCanonicalJson.MaximumBytes + 1 - (int)body.Length)), token);
            if (count == 0) break;
            body.Write(buffer, 0, count);
            if (body.Length > QuoteCanonicalJson.MaximumBytes) throw new QuoteHttpException(413, "quote-request-too-large");
        }
        JsonDocument? document = null;
        try
        {
            // Validate UTF-8 explicitly; replacement characters must not silently
            // change names or values before canonical command hashing.
            var text = new UTF8Encoding(false, true).GetString(body.GetBuffer(), 0, (int)body.Length);
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = QuoteCanonicalJson.MaximumDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new QuoteHttpException(400, "object-required");
            ValidateTree(document.RootElement);
            return document;
        }
        catch (JsonException) { document?.Dispose(); throw new QuoteHttpException(400, "invalid-json"); }
        catch (DecoderFallbackException) { document?.Dispose(); throw new QuoteHttpException(400, "invalid-json"); }
        catch (InvalidOperationException) { document?.Dispose(); throw new QuoteHttpException(400, "invalid-json"); }
        catch { document?.Dispose(); throw; }
    }

    private static void ValidateTree(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) throw new QuoteHttpException(422, "null-field");
        if (value.ValueKind == JsonValueKind.String) _ = value.GetString(); // Decode escaped Unicode before accepting the envelope.
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new QuoteHttpException(400, "duplicate-field");
                ValidateTree(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) ValidateTree(item);
    }

    private static void Keys(JsonElement root, params string[] allowed)
    {
        if (root.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal))) throw new QuoteHttpException(400, "unknown-field");
    }

    private static Guid Id(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: 36 } text || !Guid.TryParseExact(text, "D", out var id) || id == Guid.Empty)
            throw new QuoteHttpException(422, "invalid-identity");
        return id;
    }

    private static string? Proposal(JsonElement root, bool required)
    {
        if (!root.TryGetProperty("proposal", out var value))
        {
            if (required) throw new QuoteHttpException(422, "proposal-required");
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object) throw new QuoteHttpException(422, "invalid-proposal");
        return value.GetRawText();
    }
}
