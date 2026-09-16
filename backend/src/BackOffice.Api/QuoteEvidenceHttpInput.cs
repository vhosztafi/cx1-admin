using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Api;

public sealed record QuoteEvidenceAttachInput(Guid RevisionId, string RequirementCode, Guid? RiskItemId, Guid FileId, string InputFingerprint, string Reason);

public static class QuoteEvidenceHttpInput
{
    public static async Task<QuoteEvidenceAttachInput> AttachAsync(HttpRequest request, CancellationToken token = default)
    {
        using var document = await QuoteHttpInput.Read(request, token); var root = document.RootElement;
        QuoteHttpInput.Keys(root, "revisionId", "requirementCode", "riskItemId", "fileId", "inputFingerprint", "reason");
        var hash = Text(root, "inputFingerprint", 64);
        if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigitLower(c))) throw new QuoteHttpException(422, "invalid-evidence-fingerprint");
        return new(QuoteHttpInput.Id(root, "revisionId"), Text(root, "requirementCode", 100),
            root.TryGetProperty("riskItemId", out _) ? QuoteHttpInput.Id(root, "riskItemId") : null,
            QuoteHttpInput.Id(root, "fileId"), hash, QuoteEvidenceRules.Reason(Text(root, "reason", 1000)));
    }
    public static async Task<string> WithdrawAsync(HttpRequest request, CancellationToken token = default)
    {
        using var document = await QuoteHttpInput.Read(request, token); var root = document.RootElement;
        QuoteHttpInput.Keys(root, "reason"); return QuoteEvidenceRules.Reason(Text(root, "reason", 1000));
    }
    public static Guid? Revision(HttpRequest request)
    {
        if (request.Query.Count == 0) return null;
        if (request.Query.Count != 1 || request.Query.Keys.Single() != "revisionId") throw new QuoteHttpException(400, "invalid-query");
        var values = request.Query["revisionId"];
        if (values.Count != 1 || values[0] is not { Length: 36 } value || !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new QuoteHttpException(400, "invalid-query");
        return id;
    }
    private static string Text(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: > 0 } text || text.Length > maximum || string.IsNullOrWhiteSpace(text))
            throw new QuoteHttpException(422, "invalid-evidence-input");
        return text;
    }
}
