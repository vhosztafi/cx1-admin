using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

public sealed record QuoteEvidenceInput(QuoteEvidenceRequirement Requirement, string InputFingerprint);
public sealed record QuoteEvidenceFileInput(string FileName, string ContentType, byte[] Content, string Sha256);

// Pure validation only. Services must hold quote authority and validate source
// revision, file ownership and ETags before accepting an attestation.
public static class QuoteEvidenceRules
{
    public const int MaximumFileBytes = 10 * 1024 * 1024;

    public static QuoteEvidenceInput Prepare(JsonElement proposal, string code, Guid? riskItemId, QuoteVersionPins pins)
    {
        if (riskItemId == Guid.Empty) throw new QuoteInputException("evidence-item-invalid");
        var matches = QuoteEvidenceRequirements.ForProposal(proposal).Where(x => x.Code == code && x.RiskItemId == riskItemId).ToArray();
        if (matches.Length != 1) throw new QuoteInputException("evidence-requirement-inapplicable");
        JsonElement subject;
        if (riskItemId is Guid driverId)
        {
            var drivers = Items(At(proposal, "risk.drivers")).Where(row => Guid.TryParse(Text(At(row, "id")), out var id) && id == driverId).ToArray();
            if (drivers.Length != 1) throw new QuoteInputException("evidence-item-invalid");
            subject = drivers[0];
        }
        else subject = At(proposal, code == "motor-trader-proof" ? "risk.business" : "risk.previousInsurance");
        var envelope = new JsonObject { ["format"] = "quote-evidence-input-1", ["requirementCode"] = code,
            ["riskItemId"] = riskItemId?.ToString("D") };
        var insured = At(proposal, "insured");
        if (insured.ValueKind != JsonValueKind.Undefined) envelope["insured"] = JsonNode.Parse(insured.GetRawText());
        if (subject.ValueKind != JsonValueKind.Undefined) envelope["subject"] = JsonNode.Parse(subject.GetRawText());
        // Pinned product/terms/schema/question/reference identity is included by
        // canonical hashing. Array position is deliberately not an item identity.
        return new(matches[0], QuoteCanonicalJson.Create(envelope.ToJsonString(), pins).ContentHash);
    }

    public static bool Matches(QuoteEvidenceInput current, string fingerprint) => fingerprint.Length == 64 &&
        fingerprint.All(Uri.IsHexDigit) && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(current.InputFingerprint), Convert.FromHexString(fingerprint));

    public static string Reason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000 || value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new QuoteInputException("evidence-reason-required");
        return value.Trim();
    }

    public static QuoteEvidenceFileInput File(string name, string contentType, byte[] content)
    {
        if (content.Length is 0 or > MaximumFileBytes) throw new QuoteInputException("evidence-file-size");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || name != name.Trim() || name.StartsWith('.') || name.EndsWith('.') ||
            name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Surrogate || "/\\:<>\"|?*".Contains(c)))
            throw new QuoteInputException("evidence-file-name");
        var extension = Path.GetExtension(name).ToLowerInvariant(); var bytes = content.AsSpan();
        var valid = contentType switch
        {
            "application/pdf" => extension == ".pdf" && bytes.StartsWith("%PDF-"u8),
            "image/png" => extension == ".png" && bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => extension is ".jpg" or ".jpeg" && bytes.Length >= 5 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217,
            "text/plain" => extension == ".txt" && PlainText(content),
            _ => false
        };
        if (!valid) throw new QuoteInputException("evidence-file-type");
        // Bounded deterministic demo screening, not a malware scan or a claim
        // that the document proves the declarations. Persist separate attestation.
        var retained = content.ToArray();
        return new(name, contentType, retained, Convert.ToHexStringLower(SHA256.HashData(retained)));
    }

    private static bool PlainText(byte[] bytes)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return !text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) && !text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith('<');
        }
        catch (DecoderFallbackException) { return false; }
    }
}
