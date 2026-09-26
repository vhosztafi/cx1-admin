using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Reporting;
namespace BackOffice.Api;
public static class ReportEndpoints
{
 public static void MapReports(this WebApplication app)
 {
        app.MapGet("/api/v1/reports/{id:guid}/options", (Guid id,HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.OptionsAsync(LocalIdentityService.Actor(c.User),id,c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/reports", (HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.CatalogueAsync(LocalIdentityService.Actor(c.User),c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/reports/{id:guid}", (Guid id,HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>{var all=await service.CatalogueAsync(LocalIdentityService.Actor(c.User),c.RequestAborted);return all.SingleOrDefault(x=>x.Id==id) is {} definition?Results.Json(definition):Results.NotFound();})).RequireAuthorization();
        app.MapPost("/api/v1/reports/{id:guid}/run", (Guid id,ReportFilters filters,HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.RunAsync(LocalIdentityService.Actor(c.User),id,filters,c.RequestAborted)))).RequireAuthorization();
 }
}
