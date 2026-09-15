using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencySharingEndpoints
{
    public static void MapAgencySharing(this WebApplication app)
        => app.MapGet("/api/v1/agencies/{agencyId:guid}/sharing", Preview).RequireAuthorization("agency-read");

    private static async Task<IResult> Preview(Guid agencyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
    {
        if (context.Request.Query.Count != 0) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Use the current agency sharing reference.");
        try
        {
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
            var result = await AgencySharingService.PreviewContext(db, LocalIdentityService.Actor(context.User), agencyId, time, context.RequestAborted);
            return Results.Json(result, ClientEndpoints.Json);
        }
        catch (AgencyCommandException ex) { return IdentityEndpoints.Problem(context, ex.Status, ex.Code, "Agency sharing is unavailable for this identity or agency state."); }
    }
}
