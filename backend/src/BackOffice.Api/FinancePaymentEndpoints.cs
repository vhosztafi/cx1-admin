using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinancePaymentEndpoints
{
    public static void MapFinancePayments(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/refunds/{id:guid}/payments", Queue)
            .RequireAuthorization("finance-payment-execute");
        app.MapGet("/api/v1/finance/payments/{id:guid}", Detail)
            .RequireAuthorization("finance-read");
        app.MapPost("/api/v1/finance/payments/{id:guid}/resume", Resume)
            .RequireAuthorization("finance-payment-execute");
    }

    private static async Task<IResult> Queue(Guid id, HttpContext context, FinancePaymentService service)
        => await Work(context, async () =>
        {
            using var body = await Body(context.Request);
            var root = body.RootElement;
            var prior = root.TryGetProperty("priorPaymentId", out var priorValue)
                ? priorValue.ValueKind == JsonValueKind.String && Guid.TryParse(priorValue.GetString(), out var value)
                    ? value : throw new QuoteOperationException(400, "refund-payment-input-invalid")
                : (Guid?)null;
            var result = await service.QueueAsync(LocalIdentityService.Actor(context.User), id,
                Version(context.Request), prior, Text(root, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/payments/{result.ResourceId:D}";
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        });

    private static async Task<IResult> Detail(Guid id, HttpContext context, FinancePaymentService service)
        => await Work(context, async () =>
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "refund-payment-query-invalid");
            var result = await service.DetailAsync(LocalIdentityService.Actor(context.User), id,
                context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Json(result);
        });

    private static async Task<IResult> Resume(Guid id, HttpContext context, FinancePaymentService service)
        => await Work(context, async () =>
        {
            using var body = await Body(context.Request);
            var root = body.RootElement;
            if (root.EnumerateObject().Count() != 1 || !root.TryGetProperty("reason", out _))
                throw new QuoteOperationException(400, "refund-payment-resume-invalid");
            var result = await service.ResumeAsync(LocalIdentityService.Actor(context.User), id,
                Version(context.Request), Text(root, "reason"), QuoteHttpInput.Key(context.Request),
                Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/payments/{result.ResourceId:D}";
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        });

    private static byte[] Version(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString();
        if (value.Length is < 4 or > 30 || value[0] != '"' || value[^1] != '"')
            throw new QuoteOperationException(428, "refund-version-required");
        try
        {
            var bytes = Convert.FromBase64String(value[1..^1]);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        throw new QuoteOperationException(428, "refund-version-required");
    }
    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: <= 1000 } text ? text :
            throw new QuoteOperationException(400, "refund-payment-input-invalid");
    private static async Task<JsonDocument> Body(HttpRequest request)
    {
        if (request.Query.Count != 0 || !request.HasJsonContentType() || request.ContentLength is > 2048)
            throw new QuoteOperationException(400, "refund-payment-input-invalid");
        using var stream = new MemoryStream(); var buffer = new byte[512]; int read;
        while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
        {
            if (stream.Length + read > 2048) throw new QuoteOperationException(400, "refund-payment-input-invalid");
            stream.Write(buffer, 0, read);
        }
        try
        {
            var body = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            var root = body.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() is < 1 or > 2 ||
                root.EnumerateObject().Any(x => x.Name is not ("reason" or "priorPaymentId")) ||
                root.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() !=
                    root.EnumerateObject().Count())
            { body.Dispose(); throw new QuoteOperationException(400, "refund-payment-input-invalid"); }
            return body;
        }
        catch (JsonException) { throw new QuoteOperationException(400, "refund-payment-input-invalid"); }
    }
    private static async Task<IResult> Work(HttpContext context, Func<Task<IResult>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { return await work(); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
