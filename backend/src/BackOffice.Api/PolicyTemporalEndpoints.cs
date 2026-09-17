using System.Globalization;
using System.Text.RegularExpressions;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static partial class PolicyEndpoints
{
    private static void MapTemporalPolicies(WebApplication app)
    {
        app.MapGet("/api/v1/policies/{policyId:guid}/as-at", ReadPolicyAt).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/terms/{termId:guid}/as-at", ReadTermAt).RequireAuthorization("policy-read");
    }

    private static Task<IResult> ReadPolicyAt(Guid policyId, HttpContext context, PolicyReadService service)
        => TemporalRead(policyId, false, context, service);
    private static Task<IResult> ReadTermAt(Guid termId, HttpContext context, PolicyReadService service)
        => TemporalRead(termId, true, context, service);

    private static async Task<IResult> TemporalRead(Guid id, bool byTerm, HttpContext context, PolicyReadService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(id);
            var query = context.Request.Query;
            if (query.Count != 2 || !query.ContainsKey("effectiveAt") || !query.ContainsKey("knownAt"))
                throw new QuoteHttpException(400, "invalid-query");
            DateTimeOffset Cutoff(string name)
            {
                var raw = query[name];
                if (raw.Count != 1 || raw[0] is not { Length: <= 40 } value ||
                    !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant) ||
                    !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw new QuoteHttpException(400, "invalid-query");
                return parsed;
            }
            var effective = Cutoff("effectiveAt"); var known = Cutoff("knownAt");
            var actor = LocalIdentityService.Actor(context.User);
            return Results.Json(byTerm ? await service.ReadTermAtAsync(actor, id, effective, known, context.RequestAborted)
                : await service.ReadAtAsync(actor, id, effective, known, token: context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
