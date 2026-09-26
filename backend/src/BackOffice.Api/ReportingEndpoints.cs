using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Reporting;

namespace BackOffice.Api;

public static class ReportingEndpoints
{
    public static void MapReporting(this WebApplication app)
    {
        app.MapGet("/api/v1/search/options", (HttpContext c, SearchService service) => AdministrationEndpoints.Work(c, async () => Results.Json(await service.OptionsAsync(LocalIdentityService.Actor(c.User), c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/search", ([AsParameters] SearchFilters filters, HttpContext c, SearchService service) =>
            AdministrationEndpoints.Work(c, async () => Results.Json(await service.SearchAsync(LocalIdentityService.Actor(c.User), filters, c.RequestAborted))))
            .RequireAuthorization();
    }
}
