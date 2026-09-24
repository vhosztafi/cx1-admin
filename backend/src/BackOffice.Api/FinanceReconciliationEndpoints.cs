using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class FinanceReconciliationEndpoints
{
    public static void MapFinanceReconciliations(this WebApplication app)
    {
        app.MapPost("/api/v1/finance/agencies/{agencyId:guid}/bank-lines", Import).RequireAuthorization("finance-reconcile");
        app.MapGet("/api/v1/finance/agencies/{agencyId:guid}/bank-lines", List).RequireAuthorization("finance-read");
        app.MapGet("/api/v1/finance/bank-lines/{id:guid}", BankLine).RequireAuthorization("finance-read");
        app.MapPost("/api/v1/finance/agencies/{agencyId:guid}/reconciliations", Create).RequireAuthorization("finance-reconcile");
        app.MapGet("/api/v1/finance/reconciliations/{id:guid}", Detail).RequireAuthorization("finance-read");
        app.MapPost("/api/v1/finance/reconciliations/{id:guid}/matches", Match).RequireAuthorization("finance-reconcile");
        app.MapPost("/api/v1/finance/reconciliation-matches/{id:guid}/reversals", Reverse).RequireAuthorization("finance-reconcile");
        app.MapPost("/api/v1/finance/reconciliations/{id:guid}/exclusions", Exclude).RequireAuthorization("finance-reconcile");
        app.MapPost("/api/v1/finance/reconciliations/{id:guid}/variances", Explain).RequireAuthorization("finance-reconcile");
        app.MapPost("/api/v1/finance/reconciliations/{id:guid}/target-variances", ExplainTarget).RequireAuthorization("finance-reconcile");
        app.MapPost("/api/v1/finance/reconciliations/{id:guid}/complete", Complete).RequireAuthorization("finance-reconcile");
    }

    private static async Task<IResult> Import(Guid agencyId, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["importKey", "valueDate", "reference", "signedAmount", "currency", "raw"]);
            var root = body.RootElement;
            Exact(root, 6);
            var result = await service.ImportAsync(LocalIdentityService.Actor(context.User), agencyId,
                Text(root, "importKey"), Date(root, "valueDate"), Text(root, "reference"),
                Text(root, "signedAmount"), Text(root, "currency"), Raw(root, "raw"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/bank-lines/{result.ResourceId:D}";
            return result;
        });

    private static async Task<IResult> Create(Guid agencyId, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["from", "to"]);
            Exact(body.RootElement, 2);
            var result = await service.CreateAsync(LocalIdentityService.Actor(context.User), agencyId,
                Date(body.RootElement, "from"), Date(body.RootElement, "to"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/finance/reconciliations/{result.ResourceId:D}";
            return result;
        });

    private static async Task<IResult> Match(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["bankLineId", "financePostingId", "signedAmount", "reason"]);
            Exact(body.RootElement, 4);
            return await service.MatchAsync(LocalIdentityService.Actor(context.User), id,
                Id(body.RootElement, "bankLineId"), Id(body.RootElement, "financePostingId"),
                Text(body.RootElement, "signedAmount"), Text(body.RootElement, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
        });

    private static async Task<IResult> Reverse(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["reason"]);
            Exact(body.RootElement, 1);
            return await service.ReverseAsync(LocalIdentityService.Actor(context.User), id,
                Text(body.RootElement, "reason"), QuoteHttpInput.Key(context.Request),
                Guid.NewGuid(), context.RequestAborted);
        });

    private static async Task<IResult> Exclude(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["bankLineId", "duplicateOfBankLineId", "evidenceReference", "reason"]);
            Exact(body.RootElement, 4);
            return await service.ExcludeAsync(LocalIdentityService.Actor(context.User), id,
                Id(body.RootElement, "bankLineId"), Id(body.RootElement, "duplicateOfBankLineId"),
                Text(body.RootElement, "evidenceReference"), Text(body.RootElement, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
        });

    private static async Task<IResult> Explain(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["bankLineId", "reason"]);
            Exact(body.RootElement, 2);
            return await service.ExplainAsync(LocalIdentityService.Actor(context.User), id,
                Id(body.RootElement, "bankLineId"), Text(body.RootElement, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
        });

    private static async Task<IResult> Complete(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, []);
            Exact(body.RootElement, 0);
            return await service.CompleteAsync(LocalIdentityService.Actor(context.User), id,
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
        });

    private static async Task<IResult> ExplainTarget(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Command(context, async () =>
        {
            using var body = await Body(context.Request, ["financePostingId", "reason"]);
            Exact(body.RootElement, 2);
            return await service.ExplainTargetAsync(LocalIdentityService.Actor(context.User), id,
                Id(body.RootElement, "financePostingId"), Text(body.RootElement, "reason"),
                QuoteHttpInput.Key(context.Request), Guid.NewGuid(), context.RequestAborted);
        });

    private static async Task<IResult> List(Guid agencyId, HttpContext context, FinanceReconciliationService service)
        => await Read(context, async () =>
        {
            if (context.Request.Query.Keys.Except(["page", "pageSize"], StringComparer.Ordinal).Any())
                throw new QuoteOperationException(400, "bank-query-invalid");
            int Number(string name, int fallback) => !context.Request.Query.ContainsKey(name) ? fallback :
                int.TryParse(context.Request.Query[name], out var parsed) ? parsed :
                throw new QuoteOperationException(400, "bank-query-invalid");
            return Results.Json(await service.BankLinesAsync(LocalIdentityService.Actor(context.User), agencyId,
                Number("page", 1), Number("pageSize", 50), context.RequestAborted));
        });

    private static async Task<IResult> BankLine(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Read(context, async () =>
        {
            NoQuery(context.Request);
            return Results.Json(await service.BankLineAsync(LocalIdentityService.Actor(context.User), id, context.RequestAborted));
        });

    private static async Task<IResult> Detail(Guid id, HttpContext context, FinanceReconciliationService service)
        => await Read(context, async () =>
        {
            NoQuery(context.Request);
            return Results.Json(await service.DetailAsync(LocalIdentityService.Actor(context.User), id, context.RequestAborted));
        });

    private static async Task<IResult> Command(HttpContext context, Func<Task<CommandOutcome>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { var result = await work(); return Results.Content(result.Body, "application/json", statusCode: result.Status); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Read(HttpContext context, Func<Task<IResult>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { return await work(); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static void NoQuery(HttpRequest request)
    {
        if (request.Query.Count != 0) throw new QuoteOperationException(400, "bank-query-invalid");
    }
    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
            value.GetString() is { Length: <= 5000 } text ? text :
            throw new QuoteOperationException(400, "bank-input-invalid");
    private static Guid Id(JsonElement root, string name)
        => Guid.TryParse(Text(root, name), out var id) && id != Guid.Empty ? id :
            throw new QuoteOperationException(400, "bank-input-invalid");
    private static DateOnly Date(JsonElement root, string name)
        => DateOnly.TryParseExact(Text(root, name), "yyyy-MM-dd", out var date) ? date :
            throw new QuoteOperationException(400, "bank-input-invalid");
    private static string Raw(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ?
            value.GetRawText() : throw new QuoteOperationException(400, "bank-input-invalid");
    private static void Exact(JsonElement root, int count)
    {
        if (root.EnumerateObject().Count() != count) throw new QuoteOperationException(400, "bank-input-invalid");
    }
    private static async Task<JsonDocument> Body(HttpRequest request, string[] allowed)
    {
        if (request.Query.Count != 0 || !request.HasJsonContentType() || request.ContentLength is > 32768)
            throw new QuoteOperationException(400, "bank-input-invalid");
        using var stream = new MemoryStream(); var buffer = new byte[1024]; int read;
        while ((read = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
        {
            if (stream.Length + read > 32768) throw new QuoteOperationException(400, "bank-input-invalid");
            stream.Write(buffer, 0, read);
        }
        try
        {
            var document = JsonDocument.Parse(stream.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal)) ||
                root.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
            { document.Dispose(); throw new QuoteOperationException(400, "bank-input-invalid"); }
            return document;
        }
        catch (JsonException) { throw new QuoteOperationException(400, "bank-input-invalid"); }
    }
}
