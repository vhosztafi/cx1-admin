using System.Globalization;
using System.Text.RegularExpressions;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class CommercialExposureEndpoints
{
    public static void MapCommercialExposure(this WebApplication app)
    {
        foreach (var kind in new[] { "quotes", "policies", "drafts" })
        {
            var subjectKind = kind;
            app.MapGet($"/api/v1/{kind}/{{subjectId:guid}}/commercial-exposure",
                (Guid subjectId, HttpContext context, CommercialExposureReadModel model) => Read(subjectKind, subjectId, context, model))
                .RequireAuthorization("commercial-exposure-read");
        }
    }

    private static async Task<IResult> Read(string kind, Guid subjectId, HttpContext context, CommercialExposureReadModel model)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(subjectId);
            var query = context.Request.Query;
            if (query.Count != 0 && (query.Count != 2 || !query.ContainsKey("effectiveAt") || !query.ContainsKey("knownAt")))
                throw new QuoteHttpException(400, "invalid-query");
            DateTimeOffset? Cutoff(string name)
            {
                if (query.Count == 0) return null;
                var raw = query[name];
                if (raw.Count != 1 || raw[0] is not { Length: <= 40 } value ||
                    !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) ||
                    !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw new QuoteHttpException(400, "invalid-query");
                return parsed;
            }
            return Results.Json(await model.ReadAsync(LocalIdentityService.Actor(context.User), kind, subjectId,
                Cutoff("effectiveAt"), Cutoff("knownAt"), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
