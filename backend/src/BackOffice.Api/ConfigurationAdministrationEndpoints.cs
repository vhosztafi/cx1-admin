using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;

namespace BackOffice.Api;

public static class ConfigurationAdministrationEndpoints
{
    public static void MapConfigurationAdministration(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/configuration", (HttpContext c, ConfigurationAdministration s) =>
            AdministrationEndpoints.Work(c, async () => Results.Json(await s.ListAsync(LocalIdentityService.Actor(c.User), c.RequestAborted))))
            .RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/configuration", (ConfigurationEdit input, HttpContext c, ConfigurationAdministration s) =>
            AdministrationEndpoints.Work(c, async () => AdministrationEndpoints.Result(await s.SaveAsync(LocalIdentityService.Actor(c.User), input,
                c.Request.Headers.IfMatch.ToString(), QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/templates/{id:guid}/successor", (Guid id, TemplateEdit input, HttpContext c, ConfigurationAdministration s) =>
            AdministrationEndpoints.Work(c, async () => AdministrationEndpoints.Result(await s.TemplateAsync(LocalIdentityService.Actor(c.User), id, input,
                c.Request.Headers.IfMatch.ToString(), QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/templates/{id:guid}/preview", (Guid id, TemplateEdit input, HttpContext c, ConfigurationAdministration s) =>
            AdministrationEndpoints.Work(c, async () => Results.File(await s.PreviewAsync(LocalIdentityService.Actor(c.User), id, input, c.RequestAborted),
                "application/pdf", "fictional-template-preview.pdf"))).RequireAuthorization("platform-admin");
        app.MapGet("/api/v1/communication/templates", (HttpContext c, ConfigurationAdministration s) =>
            AdministrationEndpoints.Work(c, async () => Results.Json(await s.MessageTemplatesAsync(LocalIdentityService.Actor(c.User), c.RequestAborted))))
            .RequireAuthorization("message-write");
    }
}
