using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;
using BackOffice.Application.Agencies;
namespace BackOffice.Api;
public static class UserAdministrationEndpoints
{
    public sealed record TeamEdit(string Name,string Reason);
    public sealed record InvitationAcceptance(string Token,string Password);
    public static void MapUserAdministration(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/users",(HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>Results.Json(await s.ListAsync(LocalIdentityService.Actor(c.User),c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/users/invitations",(UserInvitation input,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.InviteAsync(LocalIdentityService.Actor(c.User),input,QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/teams",(TeamEdit input,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.TeamAsync(LocalIdentityService.Actor(c.User),null,input.Name,"",input.Reason,QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPut("/api/v1/admin/teams/{id:guid}",(Guid id,TeamEdit input,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.TeamAsync(LocalIdentityService.Actor(c.User),id,input.Name,c.Request.Headers.IfMatch.ToString(),input.Reason,QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/users/requests",(UserChange input,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.ProposeAsync(LocalIdentityService.Actor(c.User),input,QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/users/requests/{id:guid}/decision",(Guid id,AuthorityAdministrationEndpoints.AuthorityDecision input,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.DecideAsync(LocalIdentityService.Actor(c.User),id,c.Request.Headers.IfMatch.ToString(),input.Approve,input.Reason,QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("platform-admin");
        foreach(var action in new[]{"suspend","resume","force-reset","revoke-sessions"})
        {
            var operation=action;app.MapPost("/api/v1/admin/users/{id:guid}/"+operation,(Guid id,AdministrationEndpoints.ReasonInput input,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>AdministrationEndpoints.Result(await s.RestrictAsync(LocalIdentityService.Actor(c.User),id,c.Request.Headers.IfMatch.ToString(),operation,input.Reason,QuoteHttpInput.Key(c.Request),c.RequestAborted)))).RequireAuthorization("platform-admin");
        }
        app.MapPost("/api/v1/admin/identity-deliveries/{id:guid}/demo-reveal",(Guid id,HttpContext c,UserAdministration s)=>AdministrationEndpoints.Work(c,async()=>Results.Json(new{token=await s.RevealAsync(LocalIdentityService.Actor(c.User),id,c.RequestAborted)}))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/auth/internal-invitation",async(InvitationAcceptance input,HttpContext c,UserAdministration s)=>
        {
            try{return await s.AcceptInvitationAsync(input.Token,input.Password,c.RequestAborted)?Results.Ok(new{state="accepted"}):IdentityEndpoints.Problem(c,400,"invitation-invalid","Invitation is expired or unavailable.");}
            catch(AgencyCommandException e){return IdentityEndpoints.Problem(c,e.Status,e.Code,"Use a password with 12 to 128 characters.");}
        }).AllowAnonymous().RequireRateLimiting("invitation-acceptance");
    }
}
