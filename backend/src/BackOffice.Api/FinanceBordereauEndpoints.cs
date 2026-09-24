using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceBordereauEndpoints
{
    public static void MapFinanceBordereaux(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/providers/{providerId:guid}/periods/{periodId:guid}/bordereaux", Generate).RequireAuthorization("finance-bordereau");
        app.MapGet("/api/v1/finance/providers/{providerId:guid}/bordereaux", List).RequireAuthorization("finance-bordereau");
        app.MapGet("/api/v1/finance/bordereaux/{id:guid}", Detail).RequireAuthorization("finance-bordereau");
        app.MapPost("/api/v1/finance/bordereaux/{id:guid}/members/{sourceJournalId:guid}/corrections", Correct).RequireAuthorization("finance-bordereau");
        app.MapPost("/api/v1/finance/bordereaux/{id:guid}/members/{sourceJournalId:guid}/exclusions", Exclude).RequireAuthorization("finance-bordereau");
        app.MapPost("/api/v1/finance/bordereaux/{id:guid}/validations", Validate).RequireAuthorization("finance-bordereau");
        app.MapGet("/api/v1/finance/bordereaux/{id:guid}/versions/{versionId:guid}/download", Download).RequireAuthorization("finance-bordereau");
    }

    private static async Task<IResult> Generate(Guid providerId, Guid periodId, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            Empty(context.Request);
            var result = await service.GenerateAsync(LocalIdentityService.Actor(context.User), providerId, periodId,
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/bordereaux/{result.ResourceId:D}";
            context.Response.Headers.ETag = result.Etag;
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> List(Guid providerId, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Keys.Except(["page", "pageSize"], StringComparer.Ordinal).Any())
                throw new QuoteOperationException(400, "bordereau-query-invalid");
            int Number(string name, int fallback) => !context.Request.Query.ContainsKey(name) ? fallback :
                int.TryParse(context.Request.Query[name], out var value) ? value :
                throw new QuoteOperationException(400, "bordereau-query-invalid");
            return Results.Json(await service.ListAsync(LocalIdentityService.Actor(context.User), providerId,
                Number("page", 1), Number("pageSize", 50), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Detail(Guid id, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Keys.Except(["versionId"], StringComparer.Ordinal).Any())
                throw new QuoteOperationException(400, "bordereau-query-invalid");
            Guid? versionId = null;
            if (context.Request.Query.TryGetValue("versionId", out var raw))
                versionId = Guid.TryParse(raw, out var parsed) ? parsed : throw new QuoteOperationException(400, "bordereau-query-invalid");
            var result = await service.DetailAsync(LocalIdentityService.Actor(context.User), id, versionId, context.RequestAborted);
            context.Response.Headers.ETag = '"' + result.Id.ToString("N") + '"';
            return Results.Json(result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Correct(Guid id, Guid sourceJournalId, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var body = await Body(context.Request, ["policyReference", "providerProductCode", "agencyReference", "reason"]);
            var root = body.RootElement;
            var policy = Optional(root, "policyReference");
            var product = Optional(root, "providerProductCode");
            var agency = Optional(root, "agencyReference");
            if (policy is null && product is null && agency is null) throw new QuoteOperationException(400, "bordereau-correction-invalid");
            var result = await service.CorrectAsync(LocalIdentityService.Actor(context.User), id, Version(context.Request),
                sourceJournalId, policy, product, agency, Required(root, "reason"), QuoteHttpInput.Key(context.Request),
                Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Exclude(Guid id, Guid sourceJournalId, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            using var body = await Body(context.Request, ["reason"]);
            var result = await service.ExcludeAsync(LocalIdentityService.Actor(context.User), id, Version(context.Request),
                sourceJournalId, Required(body.RootElement, "reason"), QuoteHttpInput.Key(context.Request),
                Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Validate(Guid id, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            Empty(context.Request);
            var result = await service.ValidateAsync(LocalIdentityService.Actor(context.User), id, Version(context.Request),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.ETag = result.Etag;
            return Results.Content(result.Body, "application/json", statusCode: result.Status);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Download(Guid id, Guid versionId, HttpContext context, FinanceBordereauService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            if (context.Request.Query.Count != 0) throw new QuoteOperationException(400, "bordereau-query-invalid");
            var file = await service.DownloadAsync(LocalIdentityService.Actor(context.User), id, versionId, context.RequestAborted);
            context.Response.Headers.ETag = '"' + file.Hash + '"';
            return Results.File(file.Bytes, "text/csv; charset=utf-8", file.FileName);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static void Empty(HttpRequest request)
    {
        if (request.Query.Count != 0 || request.ContentLength is not 0)
            throw new QuoteOperationException(400, "bordereau-input-invalid");
    }

    private static Guid Version(HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString();
        return value.Length == 34 && value[0] == '"' && value[^1] == '"' &&
            Guid.TryParseExact(value[1..^1], "N", out var parsed) ? parsed :
            throw new QuoteOperationException(428, "bordereau-version-required");
    }

    private static async Task<JsonDocument> Body(HttpRequest request, string[] allowed)
    {
        if (request.Query.Count != 0 || !request.HasJsonContentType() || request.ContentLength is > 8192)
            throw new QuoteOperationException(400, "bordereau-input-invalid");
        using var stream = new MemoryStream();
        var buffer = new byte[1024]; int read;
        while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
        {
            if (stream.Length + read > 8192) throw new QuoteOperationException(400, "bordereau-input-invalid");
            stream.Write(buffer, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 3 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal)) ||
                document.RootElement.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() !=
                    document.RootElement.EnumerateObject().Count())
            {
                document.Dispose();
                throw new QuoteOperationException(400, "bordereau-input-invalid");
            }
            return document;
        }
        catch (JsonException) { throw new QuoteOperationException(400, "bordereau-input-invalid"); }
    }

    private static string? Optional(JsonElement root, string name)
        => !root.TryGetProperty(name, out var field) ? null : field.ValueKind == JsonValueKind.String && field.GetString() is { Length: <= 100 } value
            ? value : throw new QuoteOperationException(400, "bordereau-input-invalid");
    private static string Required(JsonElement root, string name)
        => root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String && field.GetString() is { Length: >= 10 and <= 1000 } value
            ? value : throw new QuoteOperationException(400, "bordereau-input-invalid");
}
