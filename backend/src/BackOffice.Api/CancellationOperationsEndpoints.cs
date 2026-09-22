using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;

namespace BackOffice.Api;

public static class CancellationOperationsEndpoints
{
    public static void MapCancellationOperations(this WebApplication app)
    {
        app.MapGet("/api/v1/versions/{versionId:guid}/cancellation-consequences", async (Guid versionId, HttpContext context, CancellationOperationsService service) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            try
            {
                QuoteEndpoints.Id(versionId); QuoteHttpInput.NoQuery(context.Request);
                var rows = await service.List(LocalIdentityService.Actor(context.User), versionId, context.RequestAborted);
                return Results.Ok(new { items = rows });
            }
            catch (OperationalAccessException e) { return IdentityEndpoints.Problem(context, e.Status, e.Code, "Review cancellation at the selected policy version."); }
            catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
        }).RequireAuthorization("document-read");
    }
}
