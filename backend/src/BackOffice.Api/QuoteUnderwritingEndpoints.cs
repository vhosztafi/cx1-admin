using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public static class QuoteUnderwritingEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static void MapQuoteUnderwriting(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes/{quoteId:guid}/rate", Rate).RequireAuthorization("quote-rate");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/submit", Submit).RequireAuthorization("quote-submit");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/return-to-draft", Return).RequireAuthorization("quote-revise");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/underwriting/refresh", Refresh).RequireAuthorization("quote-revise");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/underwriting", Assessment).RequireAuthorization("underwriting-read");
        app.MapGet("/api/v1/ratings/{ratingId:guid}", Rating).RequireAuthorization("underwriting-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/ratings", History).RequireAuthorization("underwriting-read");
    }
    private static async Task<IResult> Rate(Guid quoteId, HttpContext context, QuoteRatingService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "revisionId", "reason");
            var result = await service.RateAsync(LocalIdentityService.Actor(context.User), quoteId, QuoteHttpInput.Id(root, "revisionId"), version, Reason(root, 1000), key, Guid.NewGuid(), context.RequestAborted);
            return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static Task<IResult> Submit(Guid quoteId, HttpContext context, QuoteUnderwritingLifecycle service) => Cycle(quoteId, context, service, false);
    private static Task<IResult> Return(Guid quoteId, HttpContext context, QuoteUnderwritingLifecycle service) => Cycle(quoteId, context, service, true);
    private static async Task<IResult> Cycle(Guid quoteId, HttpContext context, QuoteUnderwritingLifecycle service, bool revise)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "reason"); var id = QuoteHttpInput.Id(root, "cycleId"); var reason = Reason(root, 2000); var actor = LocalIdentityService.Actor(context.User);
            var result = revise ? await service.ReturnToDraftAsync(actor, quoteId, id, version, reason, key, Guid.NewGuid(), context.RequestAborted)
                : await service.SubmitAsync(actor, quoteId, id, version, reason, key, Guid.NewGuid(), context.RequestAborted);
            return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Refresh(Guid quoteId, HttpContext context, QuoteUnderwritingLifecycle service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "revisionId", "productVersionId", "confirmedTermsVersionId", "reason");
            return QuoteEndpoints.Outcome(context, await service.RefreshAsync(LocalIdentityService.Actor(context.User), quoteId, QuoteHttpInput.Id(root, "revisionId"),
                QuoteHttpInput.Id(root, "productVersionId"), QuoteHttpInput.Id(root, "confirmedTermsVersionId"), version, Reason(root, 1000), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Assessment(Guid quoteId, HttpContext context, QuoteUnderwritingReadModel service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteHttpInput.NoQuery(context.Request);
            var result = await service.AssessmentAsync(LocalIdentityService.Actor(context.User), quoteId, context.RequestAborted);
            context.Response.Headers.ETag = (string)result["quoteEtag"]; return Results.Json(result, Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Rating(Guid ratingId, HttpContext context, QuoteUnderwritingReadModel service)
    {
        try
        {
            QuoteEndpoints.Id(ratingId); QuoteHttpInput.NoQuery(context.Request);
            return Results.Json(await service.RatingAsync(LocalIdentityService.Actor(context.User), ratingId, context.RequestAborted), Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> History(Guid quoteId, HttpContext context, QuoteUnderwritingReadModel service, PartyPaging paging)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var actor = LocalIdentityService.Actor(context.User);
            var version = await service.HistoryVersionAsync(actor, quoteId, context.RequestAborted);
            var page = paging.ReadBound(context, actor, "quote-rating-desc", version) ?? throw new QuoteHttpException(400, "invalid-query");
            var rows = await service.HistoryAsync(actor, quoteId, version, page.Offset, page.Size, context.RequestAborted);
            var result = new Dictionary<string, object> { ["items"] = rows.Items };
            if (paging.Next(page, rows.More) is { } next) result["nextCursor"] = next;
            return Results.Json(result, Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static string Reason(JsonElement root, int maximum)
    {
        if (!root.TryGetProperty("reason", out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString()!.Length > maximum || value.GetString()!.Any(char.IsControl)) throw new QuoteHttpException(422, "underwriting-reason-required");
        return value.GetString()!.Trim();
    }
}
