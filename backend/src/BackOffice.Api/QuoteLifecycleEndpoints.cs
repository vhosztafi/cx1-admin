using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class QuoteLifecycleEndpoints
{
    public static void MapQuoteLifecycle(this WebApplication app)
    {
        app.MapGet("/api/v1/quotes/{quoteId:guid}/revisions", Revisions).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/revisions/{revisionId:guid}", Revision).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/compare", Compare).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/clone-terms", CloneTerms).RequireAuthorization("quote-capture");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/clone", Clone).RequireAuthorization("quote-capture");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/withdraw", Withdraw).RequireAuthorization("quote-capture");
    }
    private static async Task<IResult> Revisions(Guid quoteId, HttpContext context, QuoteLifecycleService service, QuoteService quotes, PartyPaging paging)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var actor = LocalIdentityService.Actor(context.User);
            var current = await quotes.GetAsync(actor, quoteId, context.RequestAborted);
            var page = paging.ReadBound(context, actor, "quote-revision-desc", current.Revision.Id.ToString("D")) ?? throw new QuoteHttpException(400, "invalid-query");
            var rows = await service.RevisionsAsync(actor, quoteId, page.Offset, page.Size, context.RequestAborted);
            if (rows.CurrentRevisionId != current.Revision.Id) throw new QuoteHttpException(409, "quote-history-changed");
            var result = new Dictionary<string, object?> { ["items"] = rows.Items, ["totalCount"] = rows.TotalCount };
            if (paging.Next(page, page.Offset + rows.Items.Length < rows.TotalCount) is { } next) result["nextCursor"] = next;
            return Results.Ok(result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Revision(Guid quoteId, Guid revisionId, HttpContext context, QuoteLifecycleService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(revisionId); QuoteHttpInput.NoQuery(context.Request);
            return Results.Ok(await service.RevisionAsync(LocalIdentityService.Actor(context.User), quoteId, revisionId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Compare(Guid quoteId, HttpContext context, QuoteLifecycleService service, PartyPaging paging)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var actor = LocalIdentityService.Actor(context.User);
            var left = QueryId(context.Request, "leftRevisionId"); var right = QueryId(context.Request, "rightRevisionId");
            var page = paging.Read(context, actor, "quote-revision-diff-v1", "leftRevisionId", "rightRevisionId") ?? throw new QuoteHttpException(400, "invalid-query");
            var all = await service.CompareAsync(actor, quoteId, left, right, context.RequestAborted);
            var rows = all.Skip(page.Offset).Take(page.Size).ToArray();
            var result = new Dictionary<string, object?> { ["quoteId"] = quoteId, ["leftRevisionId"] = left, ["rightRevisionId"] = right,
                ["changes"] = rows, ["totalChanges"] = all.Count };
            if (paging.Next(page, page.Offset + rows.Length < all.Count) is { } next) result["nextCursor"] = next;
            return Results.Ok(result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> CloneTerms(Guid quoteId, HttpContext context, QuoteLifecycleService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId);
            if (context.Request.Query.Count != 2 || context.Request.Query.Keys.Any(x => x is not ("sourceRevisionId" or "relationshipId"))) throw new QuoteHttpException(400, "invalid-query");
            return Results.Ok(await service.CloneTermsAsync(LocalIdentityService.Actor(context.User), quoteId, QueryId(context.Request, "sourceRevisionId"),
                QueryId(context.Request, "relationshipId"), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Clone(Guid quoteId, HttpContext context, QuoteLifecycleService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var document = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = document.RootElement;
            QuoteHttpInput.Keys(root, "sourceRevisionId", "relationshipId", "confirmedTermsId", "reason");
            var result = await service.CloneAsync(LocalIdentityService.Actor(context.User), quoteId, version, QuoteHttpInput.Id(root, "sourceRevisionId"),
                QuoteHttpInput.Id(root, "relationshipId"), root.TryGetProperty("confirmedTermsId", out _) ? QuoteHttpInput.Id(root, "confirmedTermsId") : null,
                Reason(root), key, Guid.NewGuid(), context.RequestAborted);
            return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Withdraw(Guid quoteId, HttpContext context, QuoteLifecycleService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var document = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = document.RootElement;
            QuoteHttpInput.Keys(root, "reason");
            return QuoteEndpoints.Outcome(context, await service.WithdrawAsync(LocalIdentityService.Actor(context.User), quoteId, version,
                Reason(root), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static string Reason(JsonElement root)
    {
        if (!root.TryGetProperty("reason", out var reason) || reason.ValueKind != JsonValueKind.String) throw new QuoteHttpException(422, "invalid-reason");
        return QuoteEvidenceRules.Reason(reason.GetString()!);
    }
    private static Guid QueryId(HttpRequest request, string name)
    {
        var values = request.Query[name];
        if (values.Count != 1 || values[0] is not { Length: 36 } value || !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)
            throw new QuoteHttpException(400, "invalid-query");
        return id;
    }
}
