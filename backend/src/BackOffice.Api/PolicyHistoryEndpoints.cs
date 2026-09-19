using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static partial class PolicyEndpoints
{
    private static void MapPolicyHistory(WebApplication app)
    {
        app.MapGet("/api/v1/policies/{policyId:guid}/history", History).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/terms/{termId:guid}/versions", TermHistory).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/risk/{kind}/{itemId:guid}/history", RiskHistory).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/compare", CompareHistory).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/clone-terms", CloneTerms).RequireAuthorization("quote-capture");
        app.MapGet("/api/v1/policies/{policyId:guid}/reconstructions", Reconstructions).RequireAuthorization("policy-read");
        app.MapPost("/api/v1/policies/{policyId:guid}/clone", ClonePolicy).RequireAuthorization("quote-capture");
        app.MapPost("/api/v1/terms/{termId:guid}/as-at/export", ReconstructPolicy).RequireAuthorization("policy-draft-write");
    }

    private static async Task<IResult> RiskHistory(Guid policyId, string kind, Guid itemId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(policyId); QuoteEndpoints.Id(itemId); QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await service.RiskHistoryAsync(LocalIdentityService.Actor(context.User), policyId, kind, itemId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> TermHistory(Guid termId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(termId); QuoteHttpInput.NoQuery(context.Request);
            var view = await service.TermHistoryAsync(LocalIdentityService.Actor(context.User), termId, context.RequestAborted);
            context.Response.Headers.ETag = view.PolicyEtag; return Results.Json(view);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Reconstructions(Guid policyId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(policyId); QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await service.ReconstructionsAsync(LocalIdentityService.Actor(context.User), policyId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> History(Guid policyId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(policyId);
            var query = context.Request.Query;
            DateTimeOffset? effective = null, known = null;
            if (query.Count != 0)
            {
                HistoryQuery(context, "effectiveAt", "knownAt");
                effective = HistoryInstant(query["effectiveAt"][0]); known = HistoryInstant(query["knownAt"][0]);
            }
            var view = await service.ReadAsync(LocalIdentityService.Actor(context.User), policyId, effective, known, context.RequestAborted);
            context.Response.Headers.ETag = view.PolicyEtag;
            return Results.Json(view);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> CompareHistory(Guid policyId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(policyId); HistoryQuery(context, "beforeVersionId", "afterVersionId");
            return Results.Json(await service.CompareAsync(LocalIdentityService.Actor(context.User), policyId,
                HistoryQueryId(context, "beforeVersionId"), HistoryQueryId(context, "afterVersionId"), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> CloneTerms(Guid policyId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(policyId); HistoryQuery(context, "versionId", "relationshipId");
            var view = await service.CloneTermsAsync(LocalIdentityService.Actor(context.User), policyId,
                HistoryQueryId(context, "versionId"), HistoryQueryId(context, "relationshipId"), context.RequestAborted);
            context.Response.Headers.ETag = view.PolicyEtag; return Results.Json(view);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> ClonePolicy(Guid policyId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(policyId); var key = QuoteHttpInput.Key(context.Request); var expected = QuoteHttpInput.Version(context.Request);
            using var document = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 4096); var root = document.RootElement;
            QuoteHttpInput.Keys(root, "versionId", "relationshipId", "confirmedTermsId", "reason");
            var outcome = await service.CloneAsync(LocalIdentityService.Actor(context.User), policyId, expected,
                new(QuoteHttpInput.Id(root, "versionId"), QuoteHttpInput.Id(root, "relationshipId"), QuoteHttpInput.Id(root, "confirmedTermsId"), HistoryText(root, "reason")),
                key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.ETag = outcome.Etag;
            context.Response.Headers.Location = $"/api/v1/quotes/{outcome.ResourceId:D}";
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> ReconstructPolicy(Guid termId, HttpContext context, PolicyHistoryService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(termId); var key = QuoteHttpInput.Key(context.Request); var expected = QuoteHttpInput.Version(context.Request);
            using var document = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 4096); var root = document.RootElement;
            QuoteHttpInput.Keys(root, "effectiveAt", "knownAt", "versionId", "contentHash", "reason");
            var outcome = await service.ReconstructAsync(LocalIdentityService.Actor(context.User), termId, expected,
                new(HistoryInstant(HistoryText(root, "effectiveAt")), HistoryInstant(HistoryText(root, "knownAt")),
                    root.TryGetProperty("versionId", out _) ? QuoteHttpInput.Id(root, "versionId") : null,
                    root.TryGetProperty("contentHash", out _) ? HistoryText(root, "contentHash") : null, HistoryText(root, "reason")),
                key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.ETag = outcome.Etag;
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static void HistoryQuery(HttpContext context, params string[] keys)
    {
        var query = context.Request.Query;
        if (query.Count != keys.Length || keys.Any(key => !query.ContainsKey(key) || query[key].Count != 1))
            throw new QuoteHttpException(400, "invalid-query");
    }
    private static Guid HistoryQueryId(HttpContext context, string key)
        => context.Request.Query[key][0] is { Length: 36 } text && Guid.TryParseExact(text, "D", out var id) && id != Guid.Empty
            ? id : throw new QuoteHttpException(400, "invalid-query");
    private static string HistoryText(JsonElement root, string key)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 2000 } text
            ? text : throw new QuoteHttpException(422, "invalid-" + key);
    private static DateTimeOffset HistoryInstant(string? text)
        => text is { Length: <= 40 } && Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value.ToUniversalTime() : throw new QuoteHttpException(400, "invalid-cutoff");
}
