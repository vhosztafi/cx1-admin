using System.Data;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyPermissionEndpoints
{
    public static void MapAgencyPermissions(this WebApplication app)
    {
        // Internal entry points only until the complete external identity gates are verified.
        app.MapGet("/api/v1/agencies/{agencyId:guid}/permission-requests", (Guid agencyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => List(agencyId, false, context, factory, paging)).RequireAuthorization("agency-admin");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/permission-grants", (Guid agencyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => List(agencyId, true, context, factory, paging)).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/permission-requests", Request).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/permission-requests/{requestId:guid}/decision", Decide).RequireAuthorization("agency-admin");
        app.MapPost("/api/v1/agencies/{agencyId:guid}/permission-grants/{grantId:guid}/revoke", Revoke).RequireAuthorization("agency-admin");
    }

    private static async Task<IResult> List(Guid agencyId, bool grants, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging)
    {
        try
        {
            var token = context.RequestAborted; var actor = LocalIdentityService.Actor(context.User);
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await AgencyPermissionService.ReadScope(db, actor, agencyId, token);
            var page = paging.ReadBound(context, actor, "createdAt-desc,id", scope);
            if (page == null) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Refresh the permission list and use its current cursor.");
            object result;
            if (grants)
            {
                var query = db.Set<AgencyPermissionGrant>().AsNoTracking().Where(x => x.AgencyId == agencyId && x.CreatedAt <= page.AsOf);
                var total = await query.CountAsync(token);
                var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(token);
                var labels = await Labels(db, rows.Select(x => x.GrantedBy).Concat(rows.Where(x => x.RevokedBy != null).Select(x => x.RevokedBy!.Value)), token);
                var items = rows.Select(x => new { x.Id, x.AgencyId, x.Permission, x.RequestId, x.GrantedBy, grantedByLabel = labels[x.GrantedBy], x.GrantedAt, x.RevokedBy, revokedByLabel = x.RevokedBy is Guid actorId ? labels[actorId] : null, x.RevokedAt, x.RevocationReason, etag = AgencyDraftService.Etag(x.RowVersion) });
                result = new { items = items.ToArray(), totalCount = total, nextCursor = paging.Next(page, page.Offset + rows.Count < total) };
            }
            else
            {
                var query = db.Set<AgencyPermissionRequest>().AsNoTracking().Where(x => x.AgencyId == agencyId && x.CreatedAt <= page.AsOf);
                var total = await query.CountAsync(token);
                var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(page.Offset).Take(page.Size).ToListAsync(token);
                var labels = await Labels(db, rows.Select(x => x.RequestedBy).Concat(rows.Where(x => x.DecisionBy != null).Select(x => x.DecisionBy!.Value)), token);
                var items = rows.Select(x => new { x.Id, x.AgencyId, x.Permission, x.RequestedBy, requestedByLabel = labels[x.RequestedBy], x.Reason, x.State, x.CreatedAt, x.DecisionBy, decisionByLabel = x.DecisionBy is Guid actorId ? labels[actorId] : null, x.DecidedAt, x.DecisionReason, etag = AgencyDraftService.Etag(x.RowVersion) });
                result = new { items = items.ToArray(), totalCount = total, nextCursor = paging.Next(page, page.Offset + rows.Count < total) };
            }
            await transaction.CommitAsync(token); return Results.Json(result, ClientEndpoints.Json);
        }
        catch (Exception ex) when (IsError(ex)) { return Error(context, ex); }
    }

    private static Task<Dictionary<Guid, string>> Labels(BackOfficeDbContext db, IEnumerable<Guid> actorIds, CancellationToken token)
    {
        // Resolve only actors in the already scoped page, while its transaction is held.
        var ids = actorIds.Distinct().ToArray();
        return db.Set<StaffUser>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, token);
    }

    private static async Task<IResult> Request(Guid agencyId, HttpContext context, AgencyPermissionService service)
    {
        try
        {
            var input = ClientEndpoints.Input<RequestInput>(await AgencyEndpoints.ReadBody(context));
            return Response(context, await service.Request(LocalIdentityService.Actor(context.User), agencyId, ClientEndpoints.Key(context), ClientEndpoints.Version(context), input.Permission, input.Reason, context.RequestAborted));
        }
        catch (Exception ex) when (IsError(ex)) { return Error(context, ex); }
    }
    private static async Task<IResult> Decide(Guid agencyId, Guid requestId, HttpContext context, AgencyPermissionService service)
    {
        try
        {
            var input = ClientEndpoints.Input<DecisionInput>(await AgencyEndpoints.ReadBody(context));
            return Response(context, await service.Decide(LocalIdentityService.Actor(context.User), agencyId, requestId, ClientEndpoints.Key(context), ClientEndpoints.Version(context), input.Outcome, input.Reason, context.RequestAborted));
        }
        catch (Exception ex) when (IsError(ex)) { return Error(context, ex); }
    }
    private static async Task<IResult> Revoke(Guid agencyId, Guid grantId, HttpContext context, AgencyPermissionService service)
    {
        try
        {
            var input = ClientEndpoints.Input<ReasonInput>(await AgencyEndpoints.ReadBody(context));
            return Response(context, await service.Revoke(LocalIdentityService.Actor(context.User), agencyId, grantId, ClientEndpoints.Key(context), ClientEndpoints.Version(context), input.Reason, context.RequestAborted));
        }
        catch (Exception ex) when (IsError(ex)) { return Error(context, ex); }
    }
    private static IResult Response(HttpContext context, CommandOutcome outcome)
    { context.Response.Headers.ETag = outcome.Etag; return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status); }
    private static bool IsError(Exception ex) => ex is AgencyCommandException || ClientEndpoints.IsCommandError(ex);
    private static IResult Error(HttpContext context, Exception ex) => ex is AgencyCommandException error
        ? IdentityEndpoints.Problem(context, error.Status, error.Code, "Check current agency permissions and refresh before retrying.")
        : ClientEndpoints.CommandError(context, ex, "agency-permission");
    private sealed record RequestInput([property: JsonRequired] string Permission, [property: JsonRequired] string Reason);
    private sealed record DecisionInput([property: JsonRequired] string Outcome, [property: JsonRequired] string Reason);
    private sealed record ReasonInput([property: JsonRequired] string Reason);
}
