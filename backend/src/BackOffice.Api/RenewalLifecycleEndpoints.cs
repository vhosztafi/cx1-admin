using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Policies;

namespace BackOffice.Api;

public static class RenewalLifecycleEndpoints
{
    public static void MapRenewalLifecycle(this WebApplication app)
    {
        app.MapGet("/api/v1/terms/{termId:guid}/renewal-lifecycle",Read).RequireAuthorization("policy-read");
        app.MapPost("/api/v1/terms/{termId:guid}/lapse",Lapse).RequireAuthorization("policy-draft-write");
    }
    private static async Task<IResult> Read(Guid termId,HttpContext context,RenewalLifecycleService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(termId);QuoteHttpInput.NoQuery(context.Request);
            var view=await service.ReadAsync(LocalIdentityService.Actor(context.User),termId,context.RequestAborted);
            context.Response.Headers.ETag=view.Etag;return Results.Json(view);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
    private static async Task<IResult> Lapse(Guid termId,HttpContext context,RenewalLifecycleService service)
    {
        context.Response.Headers.CacheControl="no-store";
        try
        {
            QuoteEndpoints.Id(termId);var key=QuoteHttpInput.Key(context.Request);var version=QuoteHttpInput.Version(context.Request);
            using var document=await QuoteHttpInput.Read(context.Request,context.RequestAborted,4096);QuoteHttpInput.Keys(document.RootElement,"reason");
            if(!document.RootElement.TryGetProperty("reason",out var value) || value.ValueKind!=JsonValueKind.String)throw new QuoteHttpException(422,"renewal-lapse-reason-required");
            var result=await service.LapseAsync(LocalIdentityService.Actor(context.User),termId,version,value.GetString()!,key,Guid.NewGuid(),context.RequestAborted);
            context.Response.Headers.ETag=result.Etag;context.Response.Headers.Location=$"/api/v1/terms/{termId:D}/renewal-lifecycle";
            return Results.Content(result.Body,"application/json",statusCode:result.Status);
        }
        catch(Exception error) when(QuoteEndpoints.Known(error)){return QuoteEndpoints.Failure(context,error);}
    }
}
