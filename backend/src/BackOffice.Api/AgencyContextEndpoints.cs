using System.Data;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencyContextEndpoints
{
    public static void MapAgencyContext(this WebApplication app)
    {
        app.MapGet("/api/v1/agency-context", (HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time) => Context(LocalIdentityService.Actor(context.User).AgencyId!.Value, context, factory, time)).RequireAuthorization("agency-context");
        app.MapGet("/api/v1/agency-context/clients", (HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(LocalIdentityService.Actor(context.User).AgencyId!.Value, null, "clients", context, factory, paging)).RequireAuthorization("agency-context");
        app.MapGet("/api/v1/agency-context/quotes", (HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(LocalIdentityService.Actor(context.User).AgencyId!.Value, null, "quotes", context, factory, paging)).RequireAuthorization("agency-context");
        app.MapGet("/api/v1/agency-context/policies", (HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(LocalIdentityService.Actor(context.User).AgencyId!.Value, null, "policies", context, factory, paging)).RequireAuthorization("agency-context");
        app.MapGet("/api/v1/agency-context/open-items", (HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(LocalIdentityService.Actor(context.User).AgencyId!.Value, null, "open-items", context, factory, paging)).RequireAuthorization("agency-context");
        app.MapGet("/api/v1/agency-context/policies/{policyId:guid}", (Guid policyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory) => PolicySummary(LocalIdentityService.Actor(context.User).AgencyId!.Value, policyId, context, factory, false)).RequireAuthorization("agency-context");
        foreach (var section in new[] { "contacts", "instructions" })
            app.MapGet($"/api/v1/agency-context/relationships/{{relationshipId:guid}}/{section}", (Guid relationshipId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(LocalIdentityService.Actor(context.User).AgencyId!.Value, relationshipId, section, context, factory, paging)).RequireAuthorization("agency-context");
    }

    internal static async Task<IResult> PolicySummary(Guid agencyId, Guid policyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, bool preview)
    {
        if (policyId == Guid.Empty || context.Request.Query.Count != 0) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Use the current shared policy reference.");
        try {
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted); var actor = LocalIdentityService.Actor(context.User);
            var rows = preview ? await AgencySharingService.PreviewPolicies(db, actor, agencyId, new(PolicyId: policyId), context.RequestAborted)
                : await AgencySharingService.Policies(db, actor, agencyId, new(PolicyId: policyId), context.RequestAborted);
            return rows.Items.Count == 1 ? Results.Json(rows.Items[0], ClientEndpoints.Json) : IdentityEndpoints.Problem(context, 404, "policy-not-found", "This shared policy is unavailable.");
        } catch (AgencyCommandException ex) { return IdentityEndpoints.Problem(context, ex.Status, ex.Code, "Refresh the current agency sharing reference."); }
    }

    private static async Task<IResult> Page(Guid agencyId, Guid? relationshipId, string section, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging)
    {
        try
        {
            var token = context.RequestAborted; var actor = LocalIdentityService.Actor(context.User);
            var query = new AgencySharingQuery(Search: section is "clients" or "quotes" or "policies" or "open-items" ? context.Request.Query["q"].ToString() : null, RelationshipId: relationshipId, At: context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow());
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await AgencySharingService.PageScope(db, actor, agencyId, section, query, token);
            var page = paging.ReadBound(context, actor, section, scope, section is "clients" or "quotes" or "policies" or "open-items" ? ["q"] : []);
            if (page == null) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Refresh the sharing list and use its current cursor.");
            query = query with { Offset = page.Offset, Size = page.Size };
            object response;
            if (section == "open-items")
            {
                var rows = await AgencySharingService.OpenItems(db, actor, agencyId, query, token);
                response = new { items = rows.Items, totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else if (section == "policies")
            {
                var rows = await AgencySharingService.Policies(db, actor, agencyId, query, token);
                response = new { items = rows.Items, totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else if (section == "quotes")
            {
                var rows = await AgencySharingService.Quotes(db, actor, agencyId, query, token);
                response = new { items = rows.Items, totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else if (section == "clients")
            {
                var rows = await AgencySharingService.Clients(db, actor, agencyId, query, token);
                response = new { items = rows.Items.Select(x => new { x.Id, x.RelationshipId, x.Reference, x.LegalName }).ToArray(), totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else if (section == "contacts")
            {
                var rows = await AgencySharingService.Contacts(db, actor, agencyId, query, token);
                response = new { items = rows.Items.Select(x => new { x.Id, x.PersonId, x.FullName, x.Role, x.Email, x.Telephone, x.IsPrimary }).ToArray(), totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else
            {
                var rows = await AgencySharingService.Instructions(db, actor, agencyId, query, token);
                response = new { items = rows.Items.Select(x => new { x.Id, x.PersonId, x.ContactName, x.Instruction, x.ReviewOn }).ToArray(), totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            await transaction.CommitAsync(token); return Results.Json(response, ClientEndpoints.Json);
        }
        catch (AgencyCommandException ex) { return IdentityEndpoints.Problem(context, ex.Status, ex.Code, "Refresh the current agency sharing reference."); }
    }

    private static async Task<IResult> Context(Guid agencyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
    {
        if (context.Request.Query.Count != 0) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Use the current agency sharing reference.");
        try
        {
            await using var db = await factory.CreateDbContextAsync(context.RequestAborted);
            var result = await AgencySharingService.Context(db, LocalIdentityService.Actor(context.User), agencyId, time, context.RequestAborted);
            context.Response.Headers.ETag = AgencyDraftService.Etag(result.AgencyVersion);
            return Results.Json(result, ClientEndpoints.Json);
        }
        catch (AgencyCommandException ex) { return IdentityEndpoints.Problem(context, ex.Status, ex.Code, "Agency sharing is unavailable for this identity or agency state."); }
    }
}
