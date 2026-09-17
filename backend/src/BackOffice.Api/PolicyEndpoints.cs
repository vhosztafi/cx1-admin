using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static partial class PolicyEndpoints
{
    public static void MapPolicies(this WebApplication app)
    {
        MapTemporalPolicies(app);
        app.MapGet("/api/v1/policies", (HttpContext context, Microsoft.EntityFrameworkCore.IDbContextFactory<BackOffice.Infrastructure.Persistence.BackOfficeDbContext> factory, PartyPaging paging, PolicyDiscoveryService service, TimeProvider time) => List(context, factory, paging, service, time)).RequireAuthorization("policy-discovery-read");
        app.MapGet("/api/v1/policies/{policyId:guid}", Read).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/terms/{termId:guid}", Read).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/terms/{termId:guid}/versions/{versionId:guid}", Read).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/terms/{termId:guid}/transactions/{transactionId:guid}", Read).RequireAuthorization("policy-read");
        app.MapGet("/api/v1/policies/{policyId:guid}/terms/{termId:guid}/obligations/{obligationId:guid}", Read).RequireAuthorization("policy-read");
    }
    private static async Task<IResult> Read(Guid policyId, HttpContext context, PolicyReadService service)
    {
        try
        {
            QuoteEndpoints.Id(policyId);
            if (context.Request.Query.Count != 0) throw new QuoteHttpException(400, "invalid-query");
            Guid? Id(string field) => context.Request.RouteValues.TryGetValue(field, out var value) ? Guid.Parse((string)value!) : null;
            return Results.Json(await service.ReadAsync(LocalIdentityService.Actor(context.User), policyId, Id("termId"), Id("versionId"), Id("transactionId"), Id("obligationId"), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
