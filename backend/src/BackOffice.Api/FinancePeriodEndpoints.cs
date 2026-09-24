using System.Globalization;
using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinancePeriodEndpoints
{
    public static void MapFinancePeriods(this WebApplication app)
    {
        app.MapGet("/api/v1/finance/periods", List).RequireAuthorization("finance-read");
        app.MapGet("/api/v1/finance/periods/{periodId:guid}", Review).RequireAuthorization("finance-read");
        app.MapPost("/api/v1/finance/periods/{periodId:guid}/close", Close)
            .RequireAuthorization("finance-period-close");
        app.MapPost("/api/v1/finance/corrections", Correction)
            .RequireAuthorization("finance-correction-post");
        app.MapPost("/api/v1/finance/journals", Correction)
            .RequireAuthorization("finance-correction-post");
        app.MapGet("/api/v1/finance/corrections/{id:guid}", CorrectionDetail)
            .RequireAuthorization("finance-read");
    }

    private static async Task<IResult> List(HttpContext context, FinancePeriodService service)
        => await Work(context, async () =>
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "finance-period-query-invalid");
            return Results.Json(await service.ListAsync(LocalIdentityService.Actor(context.User),
                context.RequestAborted));
        });

    private static async Task<IResult> Review(Guid periodId, HttpContext context, FinancePeriodService service)
        => await Work(context, async () =>
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "finance-period-query-invalid");
            var view = await service.ReviewAsync(LocalIdentityService.Actor(context.User), periodId,
                context.RequestAborted);
            context.Response.Headers.ETag = view.Etag;
            return Results.Json(view);
        });

    private static async Task<IResult> Close(Guid periodId, HttpContext context, FinancePeriodService service)
        => await Work(context, async () =>
        {
            using var body = await Body(context.Request, ["reason"]);
            var result = await service.CloseAsync(LocalIdentityService.Actor(context.User), periodId,
                Version(context.Request), Text(body.RootElement, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        });

    private static async Task<IResult> Correction(HttpContext context, FinancePeriodService service)
        => await Work(context, async () =>
        {
            using var body = await Body(context.Request,
                ["originalSourceKind", "originalSourceId", "debtorDelta", "providerDelta", "cashDelta",
                    "internalDelta", "effectiveAt", "reason"]);
            var root = body.RootElement;
            if (!DateTimeOffset.TryParseExact(Text(root, "effectiveAt"), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var effectiveAt) || effectiveAt.Offset != TimeSpan.Zero)
                throw new QuoteOperationException(400, "finance-correction-input-invalid");
            var result = await service.PostCorrectionAsync(LocalIdentityService.Actor(context.User),
                Text(root, "originalSourceKind"), Id(root, "originalSourceId"),
                Text(root, "debtorDelta"), Text(root, "providerDelta"), Text(root, "cashDelta"),
                Text(root, "internalDelta"), effectiveAt, Text(root, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/corrections/{result.ResourceId:D}";
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        });

    private static async Task<IResult> CorrectionDetail(Guid id, HttpContext context, FinancePeriodService service)
        => await Work(context, async () =>
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "finance-period-query-invalid");
            return Results.Json(await service.CorrectionAsync(LocalIdentityService.Actor(context.User), id,
                context.RequestAborted));
        });

    private static byte[] Version(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString();
        if (value.Length is < 4 or > 30 || value[0] != '"' || value[^1] != '"')
            throw new QuoteOperationException(428, "finance-period-version-required");
        try { var bytes = Convert.FromBase64String(value[1..^1]); if (bytes.Length == 8) return bytes; }
        catch (FormatException) { }
        throw new QuoteOperationException(428, "finance-period-version-required");
    }

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: <= 1000 } text ? text :
            throw new QuoteOperationException(400, "finance-period-input-invalid");

    private static Guid Id(JsonElement root, string name)
        => Guid.TryParse(Text(root, name), out var id) && id != Guid.Empty ? id :
            throw new QuoteOperationException(400, "finance-period-input-invalid");

    private static async Task<JsonDocument> Body(HttpRequest request, string[] fields)
    {
        if (request.Query.Count != 0 || !request.HasJsonContentType() || request.ContentLength is > 4096)
            throw new QuoteOperationException(400, "finance-period-input-invalid");
        using var stream = new MemoryStream(); var buffer = new byte[512]; int read;
        while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
        {
            if (stream.Length + read > 4096) throw new QuoteOperationException(400, "finance-period-input-invalid");
            stream.Write(buffer, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 3 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != fields.Length ||
                root.EnumerateObject().Any(x => !fields.Contains(x.Name, StringComparer.Ordinal)) ||
                fields.Any(x => !root.TryGetProperty(x, out _)))
            { document.Dispose(); throw new QuoteOperationException(400, "finance-period-input-invalid"); }
            return document;
        }
        catch (JsonException) { throw new QuoteOperationException(400, "finance-period-input-invalid"); }
    }

    private static async Task<IResult> Work(HttpContext context, Func<Task<IResult>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { return await work(); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
