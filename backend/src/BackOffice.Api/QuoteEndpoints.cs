using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public static class QuoteEndpoints
{
    public static void MapQuotes(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes", Create).RequireAuthorization("quote-capture");
        app.MapPut("/api/v1/quotes/{quoteId:guid}/proposal", Save).RequireAuthorization("quote-capture");
        app.MapGet("/api/v1/quotes/{quoteId:guid}", Get).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/readiness", Readiness).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quote-products", Products).RequireAuthorization("quote-read");
    }

    private static async Task<IResult> Products(HttpContext context, QuoteProducts service)
    {
        try
        {
            var relationship = QuoteHttpInput.ProductRelationship(context.Request);
            var selections = await service.ListAsync(LocalIdentityService.Actor(context.User), relationship, context.RequestAborted);
            return Results.Ok(new { items = selections.Select(product =>
            {
                var item = new Dictionary<string, object?>
                {
                    ["productVersionId"] = product.ProductVersionId, ["productCode"] = product.ProductCode,
                    ["displayName"] = product.DisplayName, ["versionLabel"] = product.VersionLabel,
                    ["questionSetVersion"] = product.QuestionSetVersion, ["referenceDataVersion"] = product.ReferenceDataVersion,
                    ["captureEligible"] = product.CaptureEligible
                };
                if (product.UnavailableReason is not null) item["unavailableReason"] = product.UnavailableReason;
                return item;
            }).ToArray() });
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }

    private static async Task<IResult> Create(HttpContext context, QuoteService service)
    {
        try
        {
            var key = QuoteHttpInput.Key(context.Request);
            var input = await QuoteHttpInput.CreateAsync(context.Request, context.RequestAborted);
            var outcome = await service.CreateAsync(LocalIdentityService.Actor(context.User), input.RelationshipId,
                input.ProductVersionId, input.Proposal, key, Guid.NewGuid(), context.RequestAborted, input.MatchSubmissionId);
            return Outcome(context, outcome);
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }

    private static async Task<IResult> Save(Guid quoteId, HttpContext context, QuoteService service)
    {
        try
        {
            Id(quoteId);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            var input = await QuoteHttpInput.SaveAsync(context.Request, context.RequestAborted);
            return Outcome(context, await service.SaveAsync(LocalIdentityService.Actor(context.User), quoteId, version,
                input.Proposal, input.Reason, key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }

    private static Task<IResult> Get(Guid quoteId, HttpContext context, QuoteService service, TimeProvider time) => Read(quoteId, context, service, time, false);
    private static Task<IResult> Readiness(Guid quoteId, HttpContext context, QuoteService service, TimeProvider time) => Read(quoteId, context, service, time, true);
    private static async Task<IResult> Read(Guid quoteId, HttpContext context, QuoteService service, TimeProvider time, bool assessmentOnly)
    {
        try
        {
            Id(quoteId); QuoteHttpInput.NoQuery(context.Request);
            var stored = await service.GetAsync(LocalIdentityService.Actor(context.User), quoteId, context.RequestAborted);
            using var proposal = JsonDocument.Parse(stored.Revision.ProposalJson);
            var readiness = QuoteReadiness.Assess(quoteId, stored.Revision.Id, proposal.RootElement, stored.TermAssessment, stored.CaptureUnavailableCode,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime), stored.VehicleCaptureModes, stored.CurrentEvidence, stored.MatchingCode);
            context.Response.Headers.ETag = "\"" + Convert.ToBase64String(stored.Quote.RowVersion) + "\"";
            if (assessmentOnly) return Results.Ok(readiness);
            return Results.Ok(new
            {
                id = quoteId, stored.Quote.Reference, stored.Quote.RelationshipId, stored.Quote.ClientId, stored.Quote.AgencyId,
                stored.ClientName, stored.AgencyName, stored.ProductCode, stored.Quote.State, stored.Quote.BoundPolicyId,
                revisionId = stored.Revision.Id, revisionNumber = stored.Revision.Number, stored.Quote.UpdatedAt,
                stored.Revision.ProductVersionId, proposal = proposal.RootElement.Clone(),
                captureVersions = new { stored.VersionPins.SchemaVersion, stored.VersionPins.QuestionSetVersion, referenceDataVersion = stored.VersionPins.ReferenceVersion },
                captureClosed = stored.Quote.CaptureClosedAt is not null,
                stored.Quote.CaptureClosedAt, stored.Quote.CaptureClosedReason, stored.MatchReviewId,
                capabilities = new { stored.CanSave, stored.CanClone, stored.CanWithdraw,
                    canAttachEvidence = stored.CanSave && stored.ProductCode != CommercialCaptureRules.ProductCode }, readiness
            });
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }

    internal static IResult Outcome(HttpContext context, CommandOutcome outcome)
    {
        context.Response.Headers.ETag = outcome.Etag;
        if (outcome.Status == 201) context.Response.Headers.Location = $"/api/v1/quotes/{outcome.ResourceId:D}";
        return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status);
    }

    internal static void Id(Guid id) { if (id == Guid.Empty) throw new QuoteHttpException(400, "invalid-quote-id"); }
    internal static bool Known(Exception error) => error is QuoteHttpException or QuoteOperationException or QuoteInputException or
        QuoteValidationException or CommandKeyConflictException or CommandBusyException or DbUpdateConcurrencyException;
    internal static IResult Failure(HttpContext context, Exception error)
    {
        var (status, code) = error switch
        {
            QuoteHttpException e => (e.Status, e.Code), QuoteOperationException e => (e.Status, e.Code),
            QuoteInputException e => (e.Code == "quote-input-too-large" ? 413 : e.Code is "quote-capture-closed" or "quote-pinned-configuration-unavailable" ? 409 : 422, e.Code),
            QuoteValidationException => (422, "quote-input-invalid"), CommandKeyConflictException => (409, "idempotency-conflict"),
            CommandBusyException => (409, "command-busy"), DbUpdateConcurrencyException => (412, "stale-quote"),
            _ => throw new InvalidOperationException("Unexpected quote error.")
        };
        if (error is QuoteValidationException validation)
            return Results.Problem(type: "about:blank", statusCode: status, title: "Check the captured details.",
                extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier,
                    ["errors"] = validation.Issues.Take(100).Select(x => new { path = x.Path.Length is > 0 and <= 500 ? x.Path : "/", code = x.Code, message = "Complete or correct the captured details." }).ToArray() });
        return IdentityEndpoints.Problem(context, status, code, "The quote request could not be completed.");
    }
}
