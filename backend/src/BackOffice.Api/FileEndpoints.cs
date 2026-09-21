using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Operations;

namespace BackOffice.Api;

public static class FileEndpoints
{
    public static void MapFiles(this WebApplication app)
    {
        app.MapPost("/api/v1/records/{recordId:guid}/file-uploads", Upload).RequireAuthorization("document-upload");
        app.MapGet("/api/v1/file-uploads/{uploadId:guid}", Status).RequireAuthorization("document-read");
        app.MapGet("/api/v1/file-uploads/{uploadId:guid}/content", Download).RequireAuthorization("document-download");
    }

    // Raw bounded file stream is a staging resource, not a DocumentVersion.
    // The document upload operation composes this store in its owning plan.
    private static Task<IResult> Upload(Guid recordId, HttpContext context, FileService service) => Run(context, async () =>
    {
        QuoteEndpoints.Id(recordId);
        var names = context.Request.Query["name"];
        if (context.Request.Query.Count != 1 || names.Count != 1 || string.IsNullOrEmpty(names[0])) throw new QuoteHttpException(400, "file-name-required");
        var key = QuoteHttpInput.Key(context.Request);
        if (context.Request.ContentLength > FileRules.MaximumFileBytes) throw new QuoteHttpException(413, "file-size-invalid");
        var type = context.Request.ContentType ?? ""; FileRules.ValidateNameAndType(names[0]!, type);
        var outcome = await service.Upload(LocalIdentityService.Actor(context.User), recordId, names[0]!, type, context.Request.Body, key, context.RequestAborted);
        context.Response.Headers.Location = $"/api/v1/file-uploads/{outcome.ResourceId}";
        return QuoteEndpoints.Outcome(context, outcome);
    });

    private static Task<IResult> Status(Guid uploadId, HttpContext context, FileService service) => Run(context, async () =>
    {
        QuoteEndpoints.Id(uploadId); QuoteHttpInput.NoQuery(context.Request);
        return QuoteEndpoints.Outcome(context, await service.ReadUpload(LocalIdentityService.Actor(context.User), uploadId, context.RequestAborted));
    });

    private static Task<IResult> Download(Guid uploadId, HttpContext context, FileService service) => Run(context, async () =>
    {
        QuoteEndpoints.Id(uploadId); QuoteHttpInput.NoQuery(context.Request);
        var file = await service.DownloadUpload(LocalIdentityService.Actor(context.User), uploadId, context.RequestAborted);
        // ASP.NET formats/escapes the attachment filename and disposes the
        // verified stream after writing the response (including disconnects).
        return Results.Stream(file.Content, file.MediaType, file.Name, enableRangeProcessing: false);
    });

    private static async Task<IResult> Run(HttpContext context, Func<Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        try { return await action(); }
        catch (OperationalAccessException error) { return IdentityEndpoints.Problem(context, error.Status, error.Code, "The file is unavailable."); }
        catch (FileRuleException error) { return IdentityEndpoints.Problem(context, error.Code == "file-size-invalid" ? 413 : 422, error.Code, "Check the file name, type and size."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return IdentityEndpoints.Problem(context, 503, "file-storage-unavailable", "The file is unavailable. Try again later."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
