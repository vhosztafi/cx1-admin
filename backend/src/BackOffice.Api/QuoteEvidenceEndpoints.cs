using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.AspNetCore.Http.Features;

namespace BackOffice.Api;

public static class QuoteEvidenceEndpoints
{
    public static void MapQuoteEvidence(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes/{quoteId:guid}/evidence-files", Upload).RequireAuthorization("quote-capture");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/evidence-files", Files).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/evidence-files/{fileId:guid}/content", Download).RequireAuthorization("quote-read");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/evidence", Attach).RequireAuthorization("quote-capture");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/evidence", Read).RequireAuthorization("quote-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/evidence/{evidenceId:guid}", Item).RequireAuthorization("quote-read");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/evidence/{evidenceId:guid}/withdraw", Withdraw).RequireAuthorization("quote-capture");
    }
    private static async Task<IResult> Upload(Guid quoteId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteHttpInput.NoQuery(context.Request);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            if (!context.Request.HasFormContentType || !context.Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                throw new QuoteHttpException(415, "multipart-required");
            const int maximum = QuoteEvidenceRules.MaximumFileBytes + 16384;
            if (context.Request.ContentLength > maximum) throw new QuoteHttpException(413, "evidence-file-size");
            using var body = new MemoryStream(); var buffer = new byte[8192];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                if (body.Length + read > maximum) throw new QuoteHttpException(413, "evidence-file-size");
                body.Write(buffer, 0, read);
            }
            body.Position = 0; context.Request.Body = body;
            var form = await context.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maximum,
                ValueLengthLimit = 256, ValueCountLimit = 8, MultipartHeadersLengthLimit = 1024, MultipartHeadersCountLimit = 8 }, context.RequestAborted);
            if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Count != 2 || form.Keys.Any(x => x is not ("fileName" or "contentType")) ||
                form["fileName"].Count != 1 || form["contentType"].Count != 1) throw new QuoteHttpException(422, "evidence-upload-fields");
            var upload = form.Files[0];
            if (upload.Length > QuoteEvidenceRules.MaximumFileBytes) throw new QuoteHttpException(413, "evidence-file-size");
            if (upload.FileName != form["fileName"].ToString() || upload.ContentType != form["contentType"].ToString()) throw new QuoteHttpException(422, "evidence-upload-metadata");
            using var file = new MemoryStream(); await upload.CopyToAsync(file, context.RequestAborted);
            var result = await service.UploadAsync(LocalIdentityService.Actor(context.User), quoteId, version, upload.FileName,
                upload.ContentType, file.ToArray(), key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/quotes/{quoteId:D}/evidence-files/{result.ResourceId:D}/content";
            return Response(context, result);
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static async Task<IResult> Attach(Guid quoteId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            var input = await QuoteEvidenceHttpInput.AttachAsync(context.Request, context.RequestAborted);
            var result = await service.AttachAsync(LocalIdentityService.Actor(context.User), quoteId, version, input.RevisionId,
                input.RequirementCode, input.RiskItemId, input.FileId, input.InputFingerprint, input.Reason, key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/quotes/{quoteId:D}/evidence/{result.ResourceId:D}";
            return Response(context, result);
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static async Task<IResult> Withdraw(Guid quoteId, Guid evidenceId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(evidenceId);
            var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            var reason = await QuoteEvidenceHttpInput.WithdrawAsync(context.Request, context.RequestAborted);
            return Response(context, await service.WithdrawAsync(LocalIdentityService.Actor(context.User), quoteId, evidenceId, version,
                reason, key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static async Task<IResult> Read(Guid quoteId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var revision = QuoteEvidenceHttpInput.Revision(context.Request);
            return Results.Ok(await service.ReadAsync(LocalIdentityService.Actor(context.User), quoteId, revision, context.RequestAborted));
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static async Task<IResult> Item(Guid quoteId, Guid evidenceId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(evidenceId); QuoteHttpInput.NoQuery(context.Request);
            var result = await service.ReadAsync(LocalIdentityService.Actor(context.User), quoteId, token: context.RequestAborted);
            var item = result.Items.SingleOrDefault(x => x.Id == evidenceId) ?? throw new QuoteHttpException(404, "evidence-not-found");
            context.Response.Headers.ETag = item.Etag; return Results.Ok(item);
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static async Task<IResult> Files(Guid quoteId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteHttpInput.NoQuery(context.Request);
            return Results.Ok(new { items = await service.FilesAsync(LocalIdentityService.Actor(context.User), quoteId, context.RequestAborted) });
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static async Task<IResult> Download(Guid quoteId, Guid fileId, HttpContext context, QuoteEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(fileId); QuoteHttpInput.NoQuery(context.Request);
            var file = await service.DownloadAsync(LocalIdentityService.Actor(context.User), quoteId, fileId, context.RequestAborted);
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
        }
        catch (Exception error) when (Known(error)) { return Failure(context, error); }
    }
    private static IResult Response(HttpContext context, CommandOutcome outcome)
    { context.Response.Headers.ETag = outcome.Etag; return Results.Content(outcome.Body, "application/json", statusCode: outcome.Status); }
    private static bool Known(Exception error) => QuoteEndpoints.Known(error) || error is InvalidDataException;
    private static IResult Failure(HttpContext context, Exception error) => error switch
    {
        InvalidDataException => IdentityEndpoints.Problem(context, 422, "invalid-evidence-upload", "Check the upload format and size."),
        QuoteInputException { Code: "evidence-file-size" } => IdentityEndpoints.Problem(context, 413, "evidence-file-size", "The file must contain at most 10 MiB."),
        _ => QuoteEndpoints.Failure(context, error)
    };
}
