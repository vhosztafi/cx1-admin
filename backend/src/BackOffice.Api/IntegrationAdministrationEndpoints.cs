using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;
namespace BackOffice.Api;
public static class IntegrationAdministrationEndpoints
{
    public static void MapIntegrationAdministration(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/integration-health",(HttpContext c,IntegrationAdministration s)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await s.HealthAsync(LocalIdentityService.Actor(c.User),c.RequestAborted)))).RequireAuthorization("integration-admin");
        app.MapPost("/api/v1/admin/integration-scenarios",(IntegrationScenarioEdit input,HttpContext c,IntegrationAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.ScenarioAsync(LocalIdentityService.Actor(c.User),input,c.Request.Headers.IfMatch.ToString(),QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("integration-admin");
        app.MapGet("/api/v1/admin/oversight/jobs",(string? kind,string? state,int? offset,DateTimeOffset? asOf,HttpContext c,IntegrationAdministration s)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await s.JobsAsync(LocalIdentityService.Actor(c.User),kind,state,offset??0,asOf,c.RequestAborted)))).RequireAuthorization("integration-admin");
        app.MapGet("/api/v1/admin/oversight/jobs/{id:guid}",(Guid id,HttpContext c,IntegrationAdministration s)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await s.DetailAsync(LocalIdentityService.Actor(c.User),id,c.RequestAborted)))).RequireAuthorization("integration-admin");
    }
}
