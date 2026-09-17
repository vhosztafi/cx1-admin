using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class ServicingRatingEndpoints
{
    public static void MapServicingRatings(this WebApplication app)
    {
        app.MapPost("/api/v1/drafts/{draftId:guid}/rate", Rate).RequireAuthorization("policy-draft-rate");
        app.MapGet("/api/v1/drafts/{draftId:guid}/ratings", History).RequireAuthorization("policy-read");
    }

    private static async Task<IResult> Rate(Guid draftId, HttpContext context, ServicingRatingService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(draftId); QuoteHttpInput.NoQuery(context.Request);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            var fence = ServicingEndpoints.Fence(context.Request);
            using var document = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 16384);
            var root = document.RootElement; QuoteHttpInput.Keys(root, "revisionId", "reason");
            var revision = QuoteHttpInput.Id(root, "revisionId");
            if (!root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(reason.GetString()) || reason.GetString()!.Length > 2000)
                throw new QuoteHttpException(422, "invalid-reason");
            var outcome = await service.RateAsync(LocalIdentityService.Actor(context.User), draftId, revision, version, fence,
                reason.GetString()!, key, Guid.NewGuid(), context.RequestAborted);
            using var receipt = JsonDocument.Parse(outcome.Body);
            context.Response.Headers.Location = $"/api/v1/jobs/{receipt.RootElement.GetProperty("jobId").GetGuid():D}";
            context.Response.Headers.ETag = outcome.Etag;
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> History(Guid draftId, HttpContext context, ServicingRatingReadModel service, PartyPaging paging)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(draftId); var actor = LocalIdentityService.Actor(context.User);
            var head = await service.ReadAsync(actor, draftId, pageSize: 1, token: context.RequestAborted);
            var page = paging.ReadBound(context, actor, "servicing-rating-sequence", head.DraftEtag);
            if (page is null || page.Size > 50) throw new QuoteHttpException(400, "invalid-query");
            var view = await service.ReadAsync(actor, draftId, page.Offset == 0 ? null : page.Offset, page.Size, context.RequestAborted);
            if (view.DraftEtag != head.DraftEtag) throw new QuoteHttpException(409, "stale-cursor");
            // DraftEtag is a command fence; result/expiry changes are not represented by it.
            return Results.Json(new { view.DraftId, view.RevisionId, view.DraftState, view.DraftEtag, view.AssessedAt,
                view.CurrentCycleId, view.Current, view.Items, nextCursor = paging.NextKeyset(page, view.NextBeforeSequence), view.Blockers });
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
