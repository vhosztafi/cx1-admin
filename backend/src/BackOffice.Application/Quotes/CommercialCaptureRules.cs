using System.Text.Json;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Quotes;

// The closed commercial draft schema and semantic identities are independent of
// Motor Trade. Accepting a partial proposal grants neither readiness nor authority.
public static partial class CommercialCaptureRules
{
    public const string ProductCode = "commercial-combined";
    public const string Format = "commercial-combined-capture-1";
    public const string QuestionVersion = "commercial-questions-1";
    public const string ReferenceVersion = "commercial-reference-1";

    public static bool Accepts(string schema, string questions, string references) =>
        schema == "1.0" && questions == QuestionVersion && references == ReferenceVersion;

    public static PreparedQuoteCapture Prepare(string? proposal, QuoteVersionPins pins)
    {
        if (pins.ProductVersionId == Guid.Empty || pins.AgencyTermsVersionId == Guid.Empty ||
            !Accepts(pins.SchemaVersion, pins.QuestionSetVersion, pins.ReferenceVersion))
            throw new InvalidOperationException("Trusted commercial capture identity is unavailable.");
        proposal ??= JsonSerializer.Serialize(new { schemaVersion = "1.0", format = Format, productCode = ProductCode });
        var result = Validate(proposal, pins);
        if (result.Input is null) throw new QuoteValidationException(result.Issues);
        using var doc = JsonDocument.Parse(result.Input.Json);
        return new(result.Input, doc.RootElement.TryGetProperty("termIntent", out var intent) ? intent.GetRawText() : "{}", []);
    }

    public static QuoteDraftValidation Validate(string text, QuoteVersionPins pins)
    {
        if (!Accepts(pins.SchemaVersion, pins.QuestionSetVersion, pins.ReferenceVersion))
            throw new InvalidOperationException("Pinned commercial capture configuration is unavailable.");
        CanonicalQuoteInput canonical;
        try { canonical = QuoteCanonicalJson.Create(text, pins); }
        catch (QuoteInputException error) { return new(null, [new(error.Code, "")]); }
        using var document = JsonDocument.Parse(canonical.Json);
        var root = document.RootElement;
        var shapeIssues = QuoteCaptureShape.ValidateCommercial(root);
        if (shapeIssues.Count > 0) return new(null, shapeIssues);
        var issues = new List<QuoteFieldIssue>();
        var ids = new HashSet<Guid>();
        var locations = new HashSet<Guid>();
        if (root.TryGetProperty("risk", out var risk))
        {
            foreach (var collection in new[] { "locations", "wages", "losses" })
                CheckRows(risk, collection, "/risk/" + collection, ids, issues);
            if (risk.TryGetProperty("business", out var business)) CheckRows(business, "activities", "/risk/business/activities", ids, issues);
            if (risk.TryGetProperty("businessInterruption", out var bi)) CheckRows(bi, "dependencies", "/risk/businessInterruption/dependencies", ids, issues);
            if (risk.TryGetProperty("locations", out var rows))
            {
                var index = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    locations.Add(row.GetProperty("id").GetGuid());
                    if (row.TryGetProperty("address", out var address) && address.TryGetProperty("postcode", out var postcode) &&
                        NormalizePostcode(postcode.GetString()!) is null)
                        issues.Add(new("location-postcode-invalid", $"/risk/locations/{index}/address/postcode"));
                    index++;
                }
            }
            if (risk.TryGetProperty("losses", out var losses))
            {
                var index = 0;
                foreach (var loss in losses.EnumerateArray())
                {
                    if (loss.TryGetProperty("riskItemId", out var location) && !locations.Contains(location.GetGuid()))
                        issues.Add(new("loss-location-not-owned", $"/risk/losses/{index}/riskItemId"));
                    index++;
                }
            }
        }
        CheckResponses(root, "", issues);
        return issues.Count > 0 ? new(null, issues.Take(QuoteCaptureShape.MaximumIssues).ToArray()) : new(canonical, []);
    }

    private static void CheckRows(JsonElement owner, string name, string path, HashSet<Guid> ids, List<QuoteFieldIssue> issues)
    {
        if (!owner.TryGetProperty(name, out var rows)) return;
        var index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var id = row.GetProperty("id").GetGuid();
            if (id == Guid.Empty) issues.Add(new("risk-id-empty", $"{path}/{index}/id"));
            if (!ids.Add(id)) issues.Add(new("duplicate-risk-id", $"{path}/{index}/id"));
            index++;
        }
    }

    private static void CheckResponses(JsonElement node, string path, List<QuoteFieldIssue> issues)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("answers", out var answers))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal); var index = 0;
                foreach (var answer in answers.EnumerateArray())
                {
                    if (!seen.Add(answer.GetProperty("questionId").GetString()!))
                        issues.Add(new("duplicate-question-id", $"{path}/answers/{index}/questionId"));
                    index++;
                }
            }
            foreach (var property in node.EnumerateObject()) CheckResponses(property.Value, path + "/" + property.Name, issues);
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in node.EnumerateArray()) CheckResponses(child, path + "/" + index++, issues);
        }
    }

    public static (string Postcode, string District)? NormalizePostcode(string input)
    {
        var compact = string.Concat(input.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (!PostcodeSyntax().IsMatch(compact)) return null;
        return (compact[..^3] + " " + compact[^3..], compact[..^3]);
    }

    [GeneratedRegex(@"\A(?:GIR0AA|(?:[A-PR-UWYZ][0-9][0-9]?|[A-PR-UWYZ][A-HK-Y][0-9][0-9]?|[A-PR-UWYZ][0-9][A-HJKPSTUW]|[A-PR-UWYZ][A-HK-Y][0-9][ABEHMNPRVWXY])[0-9][ABD-HJLNP-UW-Z]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PostcodeSyntax();
}
