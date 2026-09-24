using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceReceiptEndpoints
{
    public static void MapFinanceReceipts(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/agencies/{agencyId:guid}/receipts", Record).RequireAuthorization("finance-cash-write");
        app.MapGet("/api/v1/finance/agencies/{agencyId:guid}/receipts", List).RequireAuthorization("finance-read");
        app.MapGet("/api/v1/finance/receipts/{id:guid}", Detail).RequireAuthorization("finance-read");
        app.MapPost("/api/v1/finance/receipts/{id:guid}/payer", Assign).RequireAuthorization("finance-cash-write");
        app.MapPost("/api/v1/finance/receipts/{id:guid}/allocations", Allocate).RequireAuthorization("finance-cash-write");
        app.MapPost("/api/v1/finance/allocations/{id:guid}/reversals", Reverse).RequireAuthorization("finance-cash-write");
    }

    private static async Task<IResult> Record(Guid agencyId, HttpContext context, FinanceReceiptService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var body = await Body(context.Request, ["amount", "currency", "receivedOn", "bankReference",
                "originKind", "originId", "payerKind", "payerId"]);
            var root = body.RootElement;
            if (root.EnumerateObject().Count() != 8 || !DateOnly.TryParseExact(Required(root, "receivedOn"),
                "yyyy-MM-dd", out var receivedOn)) throw new QuoteOperationException(400, "receipt-input-invalid");
            var result = await service.RecordAsync(LocalIdentityService.Actor(context.User), agencyId,
                Required(root, "amount"), Required(root, "currency"), receivedOn,
                Required(root, "bankReference"), Required(root, "originKind"), Id(root, "originId"),
                Required(root, "payerKind"), OptionalId(root, "payerId"), QuoteHttpInput.Key(context.Request),
                Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/receipts/{result.ResourceId:D}";
            context.Response.Headers.ETag = result.Etag;
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> List(Guid agencyId, HttpContext context, FinanceReceiptService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Keys.Except(["page", "pageSize"], StringComparer.Ordinal).Any())
                throw new QuoteOperationException(400, "receipt-query-invalid");
            int Number(string name, int fallback) => !context.Request.Query.ContainsKey(name) ? fallback :
                int.TryParse(context.Request.Query[name], out var parsed) ? parsed :
                throw new QuoteOperationException(400, "receipt-query-invalid");
            return Results.Json(await service.ListAsync(LocalIdentityService.Actor(context.User), agencyId,
                Number("page", 1), Number("pageSize", 50), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Detail(Guid id, HttpContext context, FinanceReceiptService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "receipt-query-invalid");
            var receipt = await service.DetailAsync(LocalIdentityService.Actor(context.User), id, context.RequestAborted);
            context.Response.Headers.ETag = '"' + receipt.AssignmentId.ToString("N") + '"';
            return Results.Json(receipt);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Assign(Guid id, HttpContext context, FinanceReceiptService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var body = await Body(context.Request, ["payerKind", "payerId", "reason"]);
            var root = body.RootElement;
            if (root.EnumerateObject().Count() != 3) throw new QuoteOperationException(400, "payer-assignment-invalid");
            var result = await service.AssignAsync(LocalIdentityService.Actor(context.User), id, Version(context.Request),
                Required(root, "payerKind"), OptionalId(root, "payerId"), Required(root, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Allocate(Guid id, HttpContext context, FinanceReceiptService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var body = await Body(context.Request, ["items"]);
            var root = body.RootElement;
            if (root.EnumerateObject().Count() != 1 || !root.TryGetProperty("items", out var rows) ||
                rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() is < 1 or > 100)
                throw new QuoteOperationException(400, "allocation-input-invalid");
            var items = new List<ReceiptAllocationInput>();
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || row.EnumerateObject().Count() != 2 ||
                    row.EnumerateObject().Any(x => x.Name is not ("invoiceId" or "amount")))
                    throw new QuoteOperationException(400, "allocation-input-invalid");
                items.Add(new ReceiptAllocationInput(Id(row, "invoiceId"), Required(row, "amount")));
            }
            var result = await service.AllocateAsync(LocalIdentityService.Actor(context.User), id, Version(context.Request),
                items, QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Reverse(Guid id, HttpContext context, FinanceReceiptService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var body = await Body(context.Request, ["reason"]);
            if (body.RootElement.EnumerateObject().Count() != 1)
                throw new QuoteOperationException(400, "allocation-reversal-invalid");
            var result = await service.ReverseAsync(LocalIdentityService.Actor(context.User), id,
                Required(body.RootElement, "reason"), QuoteHttpInput.Key(context.Request),
                Guid.NewGuid(), context.RequestAborted);
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static Guid Version(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString();
        return value.Length == 34 && value[0] == '"' && value[^1] == '"' &&
            Guid.TryParseExact(value[1..^1], "N", out var parsed) ? parsed :
            throw new QuoteOperationException(428, "receipt-version-required");
    }
    private static string Required(JsonElement root, string name) => root.TryGetProperty(name, out var field) &&
        field.ValueKind == JsonValueKind.String && field.GetString() is { Length: <= 1000 } value ? value :
        throw new QuoteOperationException(400, "receipt-input-invalid");
    private static Guid Id(JsonElement root, string name) => Guid.TryParse(Required(root, name), out var value) && value != Guid.Empty
        ? value : throw new QuoteOperationException(400, "receipt-input-invalid");
    private static Guid? OptionalId(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var field)) throw new QuoteOperationException(400, "receipt-input-invalid");
        return field.ValueKind == JsonValueKind.Null ? null : Id(root, name);
    }
    private static async Task<JsonDocument> Body(HttpRequest request, string[] allowed)
    {
        if (request.Query.Count != 0 || !request.HasJsonContentType() || request.ContentLength is > 32768)
            throw new QuoteOperationException(400, "receipt-input-invalid");
        using var stream = new MemoryStream();
        var buffer = new byte[1024]; int read;
        while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
        {
            if (stream.Length + read > 32768) throw new QuoteOperationException(400, "receipt-input-invalid");
            stream.Write(buffer, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 5 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal)) ||
                root.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
            {
                document.Dispose();
                throw new QuoteOperationException(400, "receipt-input-invalid");
            }
            return document;
        }
        catch (JsonException) { throw new QuoteOperationException(400, "receipt-input-invalid"); }
    }
}
