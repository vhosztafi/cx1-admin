using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Identity;

namespace BackOffice.Api;

public static class AuthorityAdministrationEndpoints
{
    public sealed record AuthorityDecision(bool Approve, string Reason);
    public static void MapAuthorityAdministration(this WebApplication app)
    {
        app.MapGet("/api/v1/admin/authority", (HttpContext c, AuthorityAdministration s) =>
            AdministrationEndpoints.Work(c, async () => Results.Json(await s.ListAsync(LocalIdentityService.Actor(c.User), c.RequestAborted))))
            .RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/authority/requests", (AuthorityProposal input, HttpContext c, AuthorityAdministration s) =>
            AdministrationEndpoints.Work(c, async () => AdministrationEndpoints.Result(await s.ProposeAsync(LocalIdentityService.Actor(c.User), input,
                QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/authority/requests/{id:guid}/decision", (Guid id, AuthorityDecision input, HttpContext c, AuthorityAdministration s) =>
            AdministrationEndpoints.Work(c, async () => AdministrationEndpoints.Result(await s.DecideAsync(LocalIdentityService.Actor(c.User), id,
                c.Request.Headers.IfMatch.ToString(), input.Approve, input.Reason, QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
        app.MapPost("/api/v1/admin/authority/grants/{id:guid}/revoke", (Guid id, AdministrationEndpoints.ReasonInput input, HttpContext c, AuthorityAdministration s) =>
            AdministrationEndpoints.Work(c, async () => AdministrationEndpoints.Result(await s.RevokeAsync(LocalIdentityService.Actor(c.User), id,
                c.Request.Headers.IfMatch.ToString(), input.Reason, QuoteHttpInput.Key(c.Request), c.RequestAborted)))).RequireAuthorization("platform-admin");
    }
}
