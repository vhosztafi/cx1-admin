using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;
using System.Text.Json;

namespace BackOffice.Api;

public static class FinanceStatementEndpoints
{
    public static void MapFinanceStatements(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/agencies/{agencyId:guid}/statements", Generate).RequireAuthorization("statement-generate");
        app.MapGet("/api/v1/finance/agencies/{agencyId:guid}/statements", List).RequireAuthorization();
        app.MapGet("/api/v1/finance/statements/{id:guid}", Detail).RequireAuthorization();
        app.MapGet("/api/v1/finance/statements/{id:guid}/download", Download).RequireAuthorization();
    }

    private static async Task<IResult> Generate(Guid agencyId, HttpContext context, FinanceStatementService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0 || !context.Request.HasJsonContentType() || context.Request.ContentLength is > 4096)
                throw new QuoteOperationException(400, "statement-window-invalid");
            using var body = new MemoryStream();
            var buffer = new byte[1024]; int length;
            while ((length = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + length > 4096) throw new QuoteOperationException(400, "statement-window-invalid");
                body.Write(buffer, 0, length);
            }
            DateOnly from, to;
            try
            {
                using var json = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 3 });
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 ||
                    !root.TryGetProperty("from", out var fromValue) || !root.TryGetProperty("to", out var toValue) ||
                    fromValue.ValueKind != JsonValueKind.String || toValue.ValueKind != JsonValueKind.String ||
                    !DateOnly.TryParseExact(fromValue.GetString(), "yyyy-MM-dd", out from) ||
                    !DateOnly.TryParseExact(toValue.GetString(), "yyyy-MM-dd", out to))
                    throw new QuoteOperationException(400, "statement-window-invalid");
            }
            catch (JsonException) { throw new QuoteOperationException(400, "statement-window-invalid"); }
            var outcome = await service.GenerateAsync(LocalIdentityService.Actor(context.User), agencyId, from,
                to, QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/statements/{outcome.ResourceId:D}";
            return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> List(Guid agencyId, HttpContext context, FinanceStatementService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Keys.Except(["page", "pageSize"], StringComparer.Ordinal).Any())
                throw new QuoteOperationException(400, "statement-query-invalid");
            int Number(string name, int fallback)
                => !context.Request.Query.ContainsKey(name) ? fallback : int.TryParse(context.Request.Query[name], out var number)
                    ? number : throw new QuoteOperationException(400, "statement-query-invalid");
            return Results.Json(await service.ListAsync(LocalIdentityService.Actor(context.User), agencyId,
                Number("page", 1), Number("pageSize", 50), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Detail(Guid id, HttpContext context, FinanceStatementService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "statement-query-invalid");
            return Results.Json(await service.DetailAsync(LocalIdentityService.Actor(context.User), id, context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Download(Guid id, HttpContext context, FinanceStatementService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "statement-query-invalid");
            var file = await service.DownloadAsync(LocalIdentityService.Actor(context.User), id, context.RequestAborted);
            context.Response.Headers.ETag = '"' + file.ContentHash + '"';
            return Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
