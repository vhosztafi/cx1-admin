using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public static class QuoteReferralEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static void MapQuoteReferrals(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes/{quoteId:guid}/referral-decisions", Decide).RequireAuthorization("underwriting-decide-within-authority");
        app.MapPost("/api/v1/referrals/{referralId:guid}/decisions", Single).RequireAuthorization("underwriting-decide-within-authority");
        app.MapPost("/api/v1/referrals/{referralId:guid}/conditions/{conditionId:guid}/resolutions", Resolve).RequireAuthorization("underwriting-decide-within-authority");
        app.MapGet("/api/v1/referrals", List).RequireAuthorization("underwriting-read");
        app.MapGet("/api/v1/referrals/{referralId:guid}", Read).RequireAuthorization("underwriting-read");
        app.MapGet("/api/v1/referrals/{referralId:guid}/decisions", History).RequireAuthorization("underwriting-read");
    }
    private static async Task<IResult> Decide(Guid quoteId, HttpContext context, QuoteReferralService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement; QuoteHttpInput.Keys(root, "cycleId", "decisions");
            if (!root.TryGetProperty("decisions", out var decisions) || decisions.ValueKind != JsonValueKind.Array || decisions.GetArrayLength() is < 1 or > 50) throw new QuoteHttpException(422, "referral-decisions-required");
            return QuoteEndpoints.Outcome(context, await service.DecideAsync(LocalIdentityService.Actor(context.User), quoteId, QuoteHttpInput.Id(root, "cycleId"), version,
                decisions.EnumerateArray().Select(Decision).ToArray(), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Single(Guid referralId, HttpContext context, QuoteReferralService service, QuoteReferralReadModel reads)
    {
        try
        {
            QuoteEndpoints.Id(referralId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement; QuoteHttpInput.Keys(root, "cycleId", "decision");
            if (!root.TryGetProperty("decision", out var value)) throw new QuoteHttpException(422, "referral-decision-required");
            var decision = Decision(value); if (decision.ReferralId != referralId) throw new QuoteHttpException(422, "referral-route-mismatch");
            var actor = LocalIdentityService.Actor(context.User); var quoteId = await reads.QuoteForReferralAsync(actor, referralId, context.RequestAborted);
            return QuoteEndpoints.Outcome(context, await service.DecideAsync(actor, quoteId, QuoteHttpInput.Id(root, "cycleId"), version, [decision], key, Guid.NewGuid(), context.RequestAborted, referralId));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static ReferralDecisionInput Decision(JsonElement root)
    {
        var outcome = Text(root, "outcome", 30);
        string[] keys = outcome switch { "approve-with-conditions" => ["referralId", "etag", "outcome", "reason", "conditions"],
            "query" => ["referralId", "etag", "outcome", "reason", "conditions", "question"],
            "approve" or "decline" or "reopen" => ["referralId", "etag", "outcome", "reason"], _ => throw new QuoteHttpException(422, "referral-outcome-invalid") };
        QuoteHttpInput.Keys(root, keys); var conditions = Array.Empty<JsonElement>();
        if (keys.Contains("conditions"))
        {
            if (!root.TryGetProperty("conditions", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 20) throw new QuoteHttpException(422, "referral-conditions-required");
            conditions = items.EnumerateArray().Select(x => x.Clone()).ToArray();
        }
        return new(QuoteHttpInput.Id(root, "referralId"), Version(root, "etag"), outcome, Text(root, "reason", 2000), conditions, outcome == "query" ? Text(root, "question", 2000) : null);
    }
    private static async Task<IResult> Resolve(Guid referralId, Guid conditionId, HttpContext context, QuoteReferralService service, QuoteReferralReadModel reads)
    {
        try
        {
            QuoteEndpoints.Id(referralId); QuoteEndpoints.Id(conditionId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "conditionEtag", "evidenceAssociationId", "outcome", "reason"); var actor = LocalIdentityService.Actor(context.User);
            var quoteId = await reads.QuoteForReferralAsync(actor, referralId, context.RequestAborted);
            return QuoteEndpoints.Outcome(context, await service.ResolveAsync(actor, quoteId, referralId, QuoteHttpInput.Id(root, "cycleId"), conditionId,
                version, Version(root, "conditionEtag"), QuoteHttpInput.Id(root, "evidenceAssociationId"), Text(root, "outcome", 20), Text(root, "reason", 2000), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> List(HttpContext context, QuoteReferralReadModel reads, PartyPaging paging)
    {
        try
        {
            if (context.Request.Query["quoteId"].Count != 1 || !Guid.TryParseExact(context.Request.Query["quoteId"], "D", out var quoteId) || quoteId == Guid.Empty) throw new QuoteHttpException(400, "quote-scope-required");
            return await Page(context, reads, paging, quoteId, "referrals", null, "quoteId");
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Read(Guid referralId, HttpContext context, QuoteReferralReadModel reads)
    {
        try { QuoteEndpoints.Id(referralId); QuoteHttpInput.NoQuery(context.Request); var result = await reads.ReferralAsync(LocalIdentityService.Actor(context.User), referralId, context.RequestAborted); context.Response.Headers.ETag = (string)result["etag"]; return Results.Json(result, Json); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> History(Guid referralId, HttpContext context, QuoteReferralReadModel reads, PartyPaging paging)
    {
        try { QuoteEndpoints.Id(referralId); var quoteId = await reads.QuoteForReferralAsync(LocalIdentityService.Actor(context.User), referralId, context.RequestAborted); return await Page(context, reads, paging, quoteId, "decisions", referralId); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    internal static async Task<IResult> Page(HttpContext context, QuoteReferralReadModel reads, PartyPaging paging, Guid quoteId, string kind, Guid? childId, params string[] filters)
    {
        var actor = LocalIdentityService.Actor(context.User); var version = await reads.VersionAsync(actor, quoteId, context.RequestAborted);
        var page = paging.ReadBound(context, actor, "underwriting-" + kind, version, filters) ?? throw new QuoteHttpException(400, "invalid-query");
        var rows = await reads.PageAsync(actor, quoteId, kind, childId, version, page.Offset, page.Size, context.RequestAborted);
        var result = new Dictionary<string, object> { ["items"] = rows.Items }; if (paging.Next(page, rows.More) is { } next) result["nextCursor"] = next; return Results.Json(result, Json);
    }
    internal static string Text(JsonElement root, string field, int maximum)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > maximum || value.GetString()!.Any(char.IsControl)) throw new QuoteHttpException(422, "underwriting-field-invalid");
        return value.GetString()!.Trim();
    }
    internal static byte[] Version(JsonElement root, string field)
    {
        var text = Text(root, field, 100);
        if (text.Length == 14 && text[0] == '"' && text[^1] == '"')
            try { var bytes = Convert.FromBase64String(text[1..^1]); if (bytes.Length == 8 && "\"" + Convert.ToBase64String(bytes) + "\"" == text) return bytes; } catch (FormatException) { }
        throw new QuoteHttpException(400, "invalid-child-version");
    }
}
