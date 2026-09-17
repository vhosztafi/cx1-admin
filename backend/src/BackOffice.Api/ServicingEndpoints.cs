using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class ServicingEndpoints
{
    public static void MapServicingDrafts(this WebApplication app)
    {
        app.MapGet("/api/v1/terms/{termId:guid}/drafts", (Guid termId, HttpContext c, ServicingDraftService s) => Read(termId, true, c, s)).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/drafts/{draftId:guid}", (Guid draftId, HttpContext c, ServicingDraftService s) => Read(draftId, false, c, s)).RequireAuthorization("policy-read");
        app.MapPost("/api/v1/terms/{termId:guid}/drafts", Create).RequireAuthorization("policy-draft-write");
        app.MapPut("/api/v1/drafts/{draftId:guid}/proposal", (Guid draftId, HttpContext c, ServicingDraftService s) => Write(draftId, "save", c, s)).RequireAuthorization("policy-draft-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/abandon", (Guid draftId, HttpContext c, ServicingDraftService s) => Write(draftId, "abandon", c, s)).RequireAuthorization("policy-draft-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/lease", (Guid draftId, HttpContext c, ServicingDraftService s) => Write(draftId, "acquire", c, s)).RequireAuthorization("policy-draft-write");
        app.MapPut("/api/v1/drafts/{draftId:guid}/lease", (Guid draftId, HttpContext c, ServicingDraftService s) => Write(draftId, "renew", c, s)).RequireAuthorization("policy-draft-write");
        app.MapDelete("/api/v1/drafts/{draftId:guid}/lease", (Guid draftId, HttpContext c, ServicingDraftService s) => Write(draftId, "release", c, s)).RequireAuthorization("policy-draft-write");
    }

    private static async Task<IResult> Read(Guid id, bool term, HttpContext context, ServicingDraftService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(id); QuoteHttpInput.NoQuery(context.Request);
            var actor = LocalIdentityService.Actor(context.User);
            var view = term ? await service.ListAsync(actor, id, context.RequestAborted) : await service.ReadAsync(actor, id, context.RequestAborted);
            context.Response.Headers.ETag = view.Etag; return Results.Content(view.Body, "application/json");
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Create(Guid termId, HttpContext context, ServicingDraftService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(termId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var document = await QuoteHttpInput.Read(context.Request, context.RequestAborted, ServicingProposalInput.MaximumBytes); var root = document.RootElement;
            QuoteHttpInput.Keys(root, "kind", "baseVersionId", "commonEffectiveIntent", "reason");
            if (!root.TryGetProperty("commonEffectiveIntent", out var intent) || intent.ValueKind != JsonValueKind.Object) throw new QuoteHttpException(422, "servicing-effective-intent-required");
            return Outcome(context, await service.CreateAsync(LocalIdentityService.Actor(context.User), termId, version,
                new(Text(root, "kind", 30), QuoteHttpInput.Id(root, "baseVersionId"), intent.Clone(), Text(root, "reason", 2000)), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Write(Guid draftId, string action, HttpContext context, ServicingDraftService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(draftId); QuoteHttpInput.NoQuery(context.Request);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request); var actor = LocalIdentityService.Actor(context.User);
            var token = context.RequestAborted;
            if (action is "renew" or "release")
            {
                // A body is not part of these commands; reject it even when
                // chunked instead of silently ignoring request-controlled data.
                if (await context.Request.Body.ReadAsync(new byte[1], token) != 0) throw new QuoteHttpException(400, "unexpected-body");
                return Outcome(context, await service.LeaseAsync(actor, draftId, version, action, Fence(context.Request), null, key, Guid.NewGuid(), token));
            }
            using var document = await QuoteHttpInput.Read(context.Request, token, ServicingProposalInput.MaximumBytes); var root = document.RootElement;
            if (action == "save") return Outcome(context, await service.SaveAsync(actor, draftId, version, Fence(context.Request), root.GetRawText(), key, Guid.NewGuid(), token));
            if (action == "abandon")
            {
                QuoteHttpInput.Keys(root, "reason");
                return Outcome(context, await service.AbandonAsync(actor, draftId, version, Fence(context.Request), Text(root, "reason", 2000), key, Guid.NewGuid(), token));
            }
            var mode = Text(root, "mode", 20);
            if (mode == "acquire") QuoteHttpInput.Keys(root, "mode");
            else if (mode == "takeover") QuoteHttpInput.Keys(root, "mode", "reason");
            else throw new QuoteHttpException(422, "servicing-invalid-lease-action");
            return Outcome(context, await service.LeaseAsync(actor, draftId, version, mode, null, mode == "takeover" ? Text(root, "reason", 2000) : null, key, Guid.NewGuid(), token));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static IResult Outcome(HttpContext context, BackOffice.Infrastructure.Platform.CommandOutcome outcome)
    {
        context.Response.Headers.ETag = outcome.Etag;
        if (outcome.Status == 201) context.Response.Headers.Location = $"/api/v1/drafts/{outcome.ResourceId:D}";
        return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
    }

    private static Guid Fence(HttpRequest request)
    {
        var values = request.Headers["X-Edit-Lease"];
        if (values.Count != 1 || values[0] is not { Length: 36 } value || !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new QuoteHttpException(400, "servicing-lease-required");
        return id;
    }
    private static string Text(JsonElement root, string key, int maximum)
        => root.TryGetProperty(key, out var node) && node.ValueKind == JsonValueKind.String && node.GetString() is { Length: > 0 } value && value.Length <= maximum
            ? value : throw new QuoteHttpException(422, "invalid-" + key);
}
