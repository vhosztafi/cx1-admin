using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Reporting;

namespace BackOffice.Api;

public static class ReportingEndpoints
{
    public static void MapReporting(this WebApplication app)
    {
        app.MapGet("/api/v1/dashboard", (string? scope,string? queue,int? offset,HttpContext c,DashboardService service) => AdministrationEndpoints.Work(c,async()=>Results.Json(await service.ReadAsync(LocalIdentityService.Actor(c.User),scope??"mine",queue,offset??0,c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/notifications", (HttpContext c,DashboardService service) => AdministrationEndpoints.Work(c,async()=>Results.Json(await service.NoticesAsync(LocalIdentityService.Actor(c.User),c.RequestAborted)))).RequireAuthorization();
        app.MapPost("/api/v1/notifications/{id:guid}/read", (Guid id,HttpContext c,DashboardService service) => AdministrationEndpoints.Work(c,async()=>{await service.MarkReadAsync(LocalIdentityService.Actor(c.User),id,c.RequestAborted);return Results.NoContent();})).RequireAuthorization();
        app.MapGet("/api/v1/search/options", (HttpContext c, SearchService service) => AdministrationEndpoints.Work(c, async () => Results.Json(await service.OptionsAsync(LocalIdentityService.Actor(c.User), c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/search", ([AsParameters] SearchFilters filters, HttpContext c, SearchService service) =>
            AdministrationEndpoints.Work(c, async () => Results.Json(await service.SearchAsync(LocalIdentityService.Actor(c.User), filters, c.RequestAborted))))
            .RequireAuthorization();
    }
}
