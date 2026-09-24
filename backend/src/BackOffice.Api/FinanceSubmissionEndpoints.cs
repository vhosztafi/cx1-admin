using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceSubmissionEndpoints
{
    public static void MapFinanceSubmissions(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/bordereaux/{batchId:guid}/versions/{versionId:guid}/submissions", Queue)
            .RequireAuthorization("finance-bordereau");
        app.MapGet("/api/v1/finance/bordereaux/{batchId:guid}/versions/{versionId:guid}/submission", Detail)
            .RequireAuthorization("finance-bordereau");
    }

    private static async Task<IResult> Queue(Guid batchId, Guid versionId, HttpContext context,
        FinanceSubmissionService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Headers.IfMatch.ToString() != '"' + versionId.ToString("N") + '"')
                throw new QuoteOperationException(428, "bordereau-version-required");
            if (context.Request.Query.Count != 0 || !context.Request.HasJsonContentType() ||
                context.Request.ContentLength is > 2048)
                throw new QuoteOperationException(400, "bordereau-submission-input-invalid");
            var bytes = new byte[2049];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(length), context.RequestAborted);
                if (read == 0) break;
                length += read;
            }
            if (length > 2048)
                throw new QuoteOperationException(400, "bordereau-submission-input-invalid");
            using var document = JsonDocument.Parse(bytes.AsMemory(0, length),
                new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("contentHash", out var hash) || hash.ValueKind != JsonValueKind.String ||
                hash.GetString() is not { Length: 64 } contentHash)
                throw new QuoteOperationException(400, "bordereau-submission-input-invalid");
            var result = await service.QueueAsync(LocalIdentityService.Actor(context.User), batchId, versionId,
                contentHash, QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/bordereaux/{batchId:D}/versions/{versionId:D}/submission";
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (JsonException) { return QuoteEndpoints.Failure(context,
            new QuoteOperationException(400, "bordereau-submission-input-invalid")); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Detail(Guid batchId, Guid versionId, HttpContext context,
        FinanceSubmissionService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0)
                throw new QuoteOperationException(400, "bordereau-submission-query-invalid");
            return Results.Json(await service.DetailAsync(LocalIdentityService.Actor(context.User),
                batchId, versionId, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
