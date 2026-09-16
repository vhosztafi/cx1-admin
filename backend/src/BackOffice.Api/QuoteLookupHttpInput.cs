using System.Text.Json;
using BackOffice.Application.Quotes;

namespace BackOffice.Api;

public sealed record QuoteLookupRequestInput(Guid RevisionId, QuoteLookupTarget Target, string Scenario);
public sealed record QuoteLookupSelectionInput(Guid LookupId, Guid RevisionId, string Fingerprint, Guid? CandidateId, string? ManualReason);

public static class QuoteLookupHttpInput
{
    public static async Task<QuoteLookupRequestInput> RequestAsync(HttpRequest request, CancellationToken token = default)
    {
        using var document = await QuoteHttpInput.Read(request, token); var root = document.RootElement;
        QuoteHttpInput.Keys(root, "revisionId", "kind", "scope", "riskItemId", "scenario");
        var revision = QuoteHttpInput.Id(root, "revisionId");
        var kind = Text(root, "kind", 20); var scope = Text(root, "scope", 20);
        Guid? item = root.TryGetProperty("riskItemId", out _) ? QuoteHttpInput.Id(root, "riskItemId") : null;
        var scenario = QuoteLookupRules.Scenario(Text(root, "scenario", 40));
        return new(revision, new(kind, scope, item), scenario);
    }

    public static async Task<QuoteLookupSelectionInput> SelectionAsync(HttpRequest request, CancellationToken token = default)
    {
        using var document = await QuoteHttpInput.Read(request, token); var root = document.RootElement;
        QuoteHttpInput.Keys(root, "lookupId", "revisionId", "inputFingerprint", "candidateId", "manualReason");
        var fingerprint = Text(root, "inputFingerprint", 64);
        if (fingerprint.Length != 64 || fingerprint.Any(c => !char.IsAsciiHexDigitLower(c)))
            throw new QuoteHttpException(422, "invalid-lookup-fingerprint");
        Guid? candidate = root.TryGetProperty("candidateId", out _) ? QuoteHttpInput.Id(root, "candidateId") : null;
        var reason = root.TryGetProperty("manualReason", out _) ? QuoteLookupRules.ManualReason(Text(root, "manualReason", 1000)) : null;
        if ((candidate is null) == (reason is null)) throw new QuoteHttpException(422, "lookup-selection-required");
        return new(QuoteHttpInput.Id(root, "lookupId"), QuoteHttpInput.Id(root, "revisionId"), fingerprint, candidate, reason);
    }

    private static string Text(JsonElement root, string name, int maximum)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: > 0 } text || text.Length > maximum || string.IsNullOrWhiteSpace(text))
            throw new QuoteHttpException(422, "invalid-lookup-input");
        return text;
    }
}
