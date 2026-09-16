using System.Data;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class AgencySharingEndpoints
{
    public static void MapAgencySharing(this WebApplication app)
    {
        app.MapGet("/api/v1/agencies/{agencyId:guid}/sharing", Preview).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/sharing/clients", (Guid agencyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(agencyId, null, "clients", context, factory, paging)).RequireAuthorization("agency-read");
        app.MapGet("/api/v1/agencies/{agencyId:guid}/sharing/quotes", (Guid agencyId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(agencyId, null, "quotes", context, factory, paging)).RequireAuthorization("agency-read");
        foreach (var section in new[] { "contacts", "instructions" })
            app.MapGet($"/api/v1/agencies/{{agencyId:guid}}/sharing/relationships/{{relationshipId:guid}}/{section}", (Guid agencyId, Guid relationshipId, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging) => Page(agencyId, relationshipId, section, context, factory, paging)).RequireAuthorization("agency-read");
    }

    private static async Task<IResult> Page(Guid agencyId, Guid? relationshipId, string section, HttpContext context, IDbContextFactory<BackOfficeDbContext> factory, PartyPaging paging)
    {
        try
        {
            var token = context.RequestAborted; var actor = LocalIdentityService.Actor(context.User);
            var query = new AgencySharingQuery(Search: section is "clients" or "quotes" ? context.Request.Query["q"].ToString() : null, RelationshipId: relationshipId);
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var scope = await AgencySharingService.PreviewPageScope(db, actor, agencyId, section, query, token);
            var page = paging.ReadBound(context, actor, section, scope, section is "clients" or "quotes" ? ["q"] : []);
            if (page == null) return IdentityEndpoints.Problem(context, 400, "invalid-query", "Refresh the sharing list and use its current cursor.");
            query = query with { Offset = page.Offset, Size = page.Size };
            object response;
            if (section == "quotes")
            {
                var rows = await AgencySharingService.PreviewQuotes(db, actor, agencyId, query, token);
                response = new { items = rows.Items, totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else if (section == "clients")
            {
                var rows = await AgencySharingService.PreviewClients(db, actor, agencyId, query, token);
                response = new { items = rows.Items.Select(x => new { x.Id, x.RelationshipId, x.Reference, x.LegalName }).ToArray(), totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else if (section == "contacts")
            {
                var rows = await AgencySharingService.PreviewContacts(db, actor, agencyId, query, token);
                response = new { items = rows.Items.Select(x => new { x.Id, x.PersonId, x.FullName, x.Role, x.Email, x.Telephone, x.IsPrimary }).ToArray(), totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            else
            {
                var rows = await AgencySharingService.PreviewInstructions(db, actor, agencyId, query, token);
                response = new { items = rows.Items.Select(x => new { x.Id, x.PersonId, x.ContactName, x.Instruction, x.ReviewOn }).ToArray(), totalCount = rows.Total, nextCursor = paging.Next(page, page.Offset + rows.Items.Count < rows.Total) };
            }
            await transaction.CommitAsync(token); return Results.Json(response, ClientEndpoints.Json);
        }
        catch (AgencyCommandException ex) { return IdentityEndpoints.Problem(context, ex.Status, ex.Code, "Refresh the current agency sharing reference."); }
    }

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
