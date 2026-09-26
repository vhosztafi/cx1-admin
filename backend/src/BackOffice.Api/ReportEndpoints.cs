using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Reporting;
namespace BackOffice.Api;
public static class ReportEndpoints
{
 public static void MapReports(this WebApplication app)
 {
        app.MapGet("/api/v1/reports/preferences",(HttpContext c,SavedReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.ReadAsync(LocalIdentityService.Actor(c.User),c.RequestAborted)))).RequireAuthorization();
        app.MapPost("/api/v1/reports/favourites",(SaveReportInput input,HttpContext c,SavedReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.SaveAsync(LocalIdentityService.Actor(c.User),null,input,c.RequestAborted)))).RequireAuthorization();
        app.MapPut("/api/v1/reports/favourites/{id:guid}",(Guid id,SaveReportInput input,HttpContext c,SavedReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.SaveAsync(LocalIdentityService.Actor(c.User),id,input,c.RequestAborted)))).RequireAuthorization();
        app.MapDelete("/api/v1/reports/favourites/{id:guid}",(Guid id,int version,HttpContext c,SavedReportService service)=>AdministrationEndpoints.Work(c,async()=>{await service.DeleteAsync(LocalIdentityService.Actor(c.User),id,version,c.RequestAborted);return Results.NoContent();})).RequireAuthorization();
        app.MapPost("/api/v1/reports/{id:guid}/export",(Guid id,ReportExportInput input,HttpContext c,ReportExportService service)=>AdministrationEndpoints.Work(c,async()=>{var csv=await service.ExportAsync(LocalIdentityService.Actor(c.User),id,input,c.RequestAborted);return Results.File(csv.Content,"text/csv; charset=utf-8",csv.FileName);})).RequireAuthorization();
        app.MapGet("/api/v1/reports/{id:guid}/options", (Guid id,HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.OptionsAsync(LocalIdentityService.Actor(c.User),id,c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/reports", (HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.CatalogueAsync(LocalIdentityService.Actor(c.User),c.RequestAborted)))).RequireAuthorization();
        app.MapGet("/api/v1/reports/{id:guid}", (Guid id,HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>{var all=await service.CatalogueAsync(LocalIdentityService.Actor(c.User),c.RequestAborted);return all.SingleOrDefault(x=>x.Id==id) is {} definition?Results.Json(definition):Results.NotFound();})).RequireAuthorization();
        app.MapPost("/api/v1/reports/{id:guid}/run", (Guid id,ReportFilters filters,HttpContext c,ReportService service)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await service.RunAsync(LocalIdentityService.Actor(c.User),id,filters,c.RequestAborted)))).RequireAuthorization();
 }
}
