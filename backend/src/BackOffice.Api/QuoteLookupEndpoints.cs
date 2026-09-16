using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Quotes;

namespace BackOffice.Api;

public static class QuoteLookupEndpoints
{
    public static void MapQuoteLookups(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes/{quoteId:guid}/lookups", Request).RequireAuthorization("quote-capture");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/lookups", List).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/lookups/{lookupId:guid}", Get).RequireAuthorization("quote-read");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/lookup-selections", Select).RequireAuthorization("quote-capture");
    }

    private static async Task<IResult> Request(Guid quoteId, HttpContext context, QuoteLookupService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            var input = await QuoteLookupHttpInput.RequestAsync(context.Request, context.RequestAborted);
            var outcome = await service.RequestAsync(LocalIdentityService.Actor(context.User), quoteId, version,
                input.RevisionId, input.Target, input.Scenario, key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/quotes/{quoteId:D}/lookups/{outcome.ResourceId:D}";
            return QuoteEndpoints.Outcome(context, outcome);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Select(Guid quoteId, HttpContext context, QuoteLookupService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            var input = await QuoteLookupHttpInput.SelectionAsync(context.Request, context.RequestAborted);
            return QuoteEndpoints.Outcome(context, await service.SelectAsync(LocalIdentityService.Actor(context.User), quoteId,
                input.LookupId, version, input.RevisionId, input.Fingerprint, input.CandidateId, input.ManualReason,
                key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> Get(Guid quoteId, Guid lookupId, HttpContext context, QuoteLookupService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(lookupId); QuoteHttpInput.NoQuery(context.Request);
            var items = await service.ReadAsync(LocalIdentityService.Actor(context.User), quoteId, lookupId, context.RequestAborted);
            return Results.Ok(items.Single());
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }

    private static async Task<IResult> List(Guid quoteId, HttpContext context, QuoteLookupService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteHttpInput.NoQuery(context.Request);
            return Results.Ok(new { items = await service.ReadAsync(LocalIdentityService.Actor(context.User), quoteId, null, context.RequestAborted) });
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
