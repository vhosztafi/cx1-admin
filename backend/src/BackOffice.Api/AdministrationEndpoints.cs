using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class AdministrationEndpoints
{
    public static void MapAdministration(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/catalogue", (HttpContext c, ProductAdministration s) =>
            Work(c, async () => Results.Json(await s.ListAsync(LocalIdentityService.Actor(c.User), c.RequestAborted))))
            .RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/product-versions/{id:guid}/clone", (Guid id, ProductVersionEdit input, HttpContext c, ProductAdministration s) =>
            Work(c, async () => Result(await s.CloneAsync(LocalIdentityService.Actor(c.User), id, c.Request.Headers.IfMatch.ToString(), input,
                QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPut("/api/v1/admin/product-versions/{id:guid}", (Guid id, ProductVersionEdit input, HttpContext c, ProductAdministration s) =>
            Work(c, async () => Result(await s.SaveAsync(LocalIdentityService.Actor(c.User), id, c.Request.Headers.IfMatch.ToString(), input,
                QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/product-versions/{id:guid}/publish", (Guid id, ReasonInput input, HttpContext c, ProductAdministration s) =>
            Work(c, async () => Result(await s.PublishAsync(LocalIdentityService.Actor(c.User), id, c.Request.Headers.IfMatch.ToString(), input.Reason,
                QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/providers", (ProviderEdit input, HttpContext c, ProductAdministration s) =>
            Work(c, async () => Result(await s.ProviderAsync(LocalIdentityService.Actor(c.User), null, "", input,
                QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPut("/api/v1/admin/providers/{id:guid}", (Guid id, ProviderEdit input, HttpContext c, ProductAdministration s) =>
            Work(c, async () => Result(await s.ProviderAsync(LocalIdentityService.Actor(c.User), id, c.Request.Headers.IfMatch.ToString(), input,
                QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
    }
    public sealed record ReasonInput(string Reason);
    internal static IResult Result(CommandOutcome result) => Results.Content(result.Body, "application/json", statusCode: result.Status);
    internal static async Task<IResult> Work(HttpContext context, Func<Task<IResult>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { return await work(); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
