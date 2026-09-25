using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceRefundEndpoints
{
    public static void MapFinanceRefunds(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/credits/{id:guid}/refunds", Request).RequireAuthorization("finance-refund-request");
        app.MapGet("/api/v1/finance/agencies/{agencyId:guid}/refunds", List).RequireAuthorization("finance-read");
        app.MapGet("/api/v1/finance/refunds/{id:guid}", Detail).RequireAuthorization("finance-read");
        app.MapPost("/api/v1/finance/refunds/{id:guid}/decisions", Decide).RequireAuthorization("finance-refund-approve");
    }

    private static async Task<IResult> Request(Guid id, HttpContext context, FinanceRefundService service)
        => await Work(context, async () =>
        {
            using var body = await Body(context.Request, ["amount", "sources", "reason"]);
            var root = body.RootElement;
            Exact(root, 3);
            var sources = root.GetProperty("sources");
            if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() is < 1 or > 100)
                throw new QuoteOperationException(400, "refund-input-invalid");
            var inputs = sources.EnumerateArray().Select(x =>
            {
                if (x.ValueKind != JsonValueKind.Object || x.EnumerateObject().Count() != 2 ||
                    x.EnumerateObject().Any(p => p.Name is not ("allocationId" or "amount")))
                    throw new QuoteOperationException(400, "refund-input-invalid");
                return new RefundSourceInput(Id(x, "allocationId"), Text(x, "amount"));
            }).ToArray();
            var result = await service.RequestAsync(LocalIdentityService.Actor(context.User), id,
                Text(root, "amount"), inputs, Text(root, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/refunds/{result.ResourceId:D}";
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        });

    private static async Task<IResult> Decide(Guid id, HttpContext context, FinanceRefundService service)
        => await Work(context, async () =>
        {
            using var body = await Body(context.Request, ["kind", "reason"]);
            Exact(body.RootElement, 2);
            var result = await service.DecideAsync(LocalIdentityService.Actor(context.User), id,
                Version(context.Request), Text(body.RootElement, "kind"), Text(body.RootElement, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        });

    private static async Task<IResult> Detail(Guid id, HttpContext context, FinanceRefundService service)
        => await Work(context, async () =>
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "refund-query-invalid");
            var result = await service.DetailAsync(LocalIdentityService.Actor(context.User), id, context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Json(result);
        });

    private static async Task<IResult> List(Guid agencyId, HttpContext context, FinanceRefundService service)
        => await Work(context, async () =>
        {
            if (context.Request.Query.Keys.Except(["state", "page", "pageSize"], StringComparer.Ordinal).Any() ||
                context.Request.Query.Any(x => x.Value.Count != 1))
                throw new QuoteOperationException(400, "refund-query-invalid");
            int Number(string name, int fallback) => !context.Request.Query.ContainsKey(name) ? fallback :
                int.TryParse(context.Request.Query[name], out var parsed) ? parsed :
                throw new QuoteOperationException(400, "refund-query-invalid");
            return Results.Json(await service.ListAsync(LocalIdentityService.Actor(context.User), agencyId,
                context.Request.Query.TryGetValue("state", out var state) ? state.ToString() : null,
                Number("page", 1), Number("pageSize", 50), context.RequestAborted));
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
            throw new QuoteOperationException(400, "refund-input-invalid");
    private static Guid Id(JsonElement root, string name)
        => Guid.TryParse(Text(root, name), out var value) && value != Guid.Empty ? value :
            throw new QuoteOperationException(400, "refund-input-invalid");
    private static void Exact(JsonElement root, int count)
    {
        if (root.EnumerateObject().Count() != count) throw new QuoteOperationException(400, "refund-input-invalid");
    }
    private static async Task<JsonDocument> Body(HttpRequest request, string[] allowed)
    {
        if (request.Query.Count != 0 || !request.HasJsonContentType() || request.ContentLength is > 32768)
            throw new QuoteOperationException(400, "refund-input-invalid");
        using var stream = new MemoryStream(); var buffer = new byte[1024]; int read;
        while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
        {
            if (stream.Length + read > 32768) throw new QuoteOperationException(400, "refund-input-invalid");
            stream.Write(buffer, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal)) ||
                root.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
            { document.Dispose(); throw new QuoteOperationException(400, "refund-input-invalid"); }
            return document;
        }
        catch (JsonException) { throw new QuoteOperationException(400, "refund-input-invalid"); }
    }
    private static async Task<IResult> Work(HttpContext context, Func<Task<IResult>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { return await work(); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
