using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.AspNetCore.Http.Features;

namespace BackOffice.Api;

public static class UnderwritingEvidenceEndpoints
{
    public static void MapUnderwritingEvidence(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes/{quoteId:guid}/underwriting/evidence-files", Upload).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/underwriting/evidence", Attach).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/underwriting/evidence/{associationId:guid}/reviews", Review).RequireAuthorization("underwriting-evidence-review");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/underwriting/evidence/{associationId:guid}/withdraw", Withdraw).RequireAuthorization("underwriting-evidence-write");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/underwriting/evidence", List).RequireAuthorization("underwriting-read");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/underwriting/evidence/{associationId:guid}/events", Events).RequireAuthorization("underwriting-read");
    }
    private static async Task<IResult> Upload(Guid quoteId, HttpContext context, UnderwritingEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteHttpInput.NoQuery(context.Request); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            if (!context.Request.HasFormContentType || !context.Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) throw new QuoteHttpException(415, "multipart-required");
            const int maximum = QuoteEvidenceRules.MaximumFileBytes + 16384;
            if (context.Request.ContentLength > maximum) throw new QuoteHttpException(413, "evidence-file-size");
            using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            { if (body.Length + count > maximum) throw new QuoteHttpException(413, "evidence-file-size"); body.Write(buffer, 0, count); }
            body.Position = 0; context.Request.Body = body;
            var form = await context.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maximum, ValueLengthLimit = 256, ValueCountLimit = 8, MultipartHeadersLengthLimit = 1024, MultipartHeadersCountLimit = 8 }, context.RequestAborted);
            if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Count != 2 || form.Keys.Any(x => x is not ("fileName" or "contentType")) || form["fileName"].Count != 1 || form["contentType"].Count != 1) throw new QuoteHttpException(422, "evidence-upload-fields");
            var upload = form.Files[0]; if (upload.Length is 0 or > QuoteEvidenceRules.MaximumFileBytes) throw new QuoteHttpException(413, "evidence-file-size");
            if (upload.FileName != form["fileName"].ToString() || upload.ContentType != form["contentType"].ToString()) throw new QuoteHttpException(422, "evidence-upload-metadata");
            using var file = new MemoryStream(); await upload.CopyToAsync(file, context.RequestAborted);
            var result = await service.UploadAsync(LocalIdentityService.Actor(context.User), quoteId, version, upload.FileName, upload.ContentType, file.ToArray(), key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/quotes/{quoteId:D}/evidence-files/{result.ResourceId:D}/content";
            return QuoteEndpoints.Outcome(context, result);
        }
        catch (InvalidDataException) { return IdentityEndpoints.Problem(context, 422, "invalid-evidence-upload", "Check the upload format and size."); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Attach(Guid quoteId, HttpContext context, UnderwritingEvidenceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "fileId", "requirementCode", "riskItemId", "conditionId", "termsVersionId", "inputFingerprint", "reason");
            Guid? Optional(string field) => root.TryGetProperty(field, out _) ? QuoteHttpInput.Id(root, field) : null;
            return QuoteEndpoints.Outcome(context, await service.AttachAsync(LocalIdentityService.Actor(context.User), quoteId, QuoteHttpInput.Id(root, "cycleId"), version,
                QuoteHttpInput.Id(root, "fileId"), QuoteReferralEndpoints.Text(root, "requirementCode", 60), Optional("riskItemId"), Optional("conditionId"), Optional("termsVersionId"),
                QuoteReferralEndpoints.Text(root, "inputFingerprint", 64), QuoteReferralEndpoints.Text(root, "reason", 2000), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static Task<IResult> Review(Guid quoteId, Guid associationId, HttpContext context, UnderwritingEvidenceService service) => Event(quoteId, associationId, context, service, true);
    private static Task<IResult> Withdraw(Guid quoteId, Guid associationId, HttpContext context, UnderwritingEvidenceService service) => Event(quoteId, associationId, context, service, false);
    private static async Task<IResult> Event(Guid quoteId, Guid associationId, HttpContext context, UnderwritingEvidenceService service, bool review)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(associationId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, review ? ["cycleId", "associationEtag", "outcome", "expectedFingerprint", "reason"] : ["cycleId", "associationEtag", "reason"]);
            var actor = LocalIdentityService.Actor(context.User); var cycle = QuoteHttpInput.Id(root, "cycleId"); var child = QuoteReferralEndpoints.Version(root, "associationEtag"); var reason = QuoteReferralEndpoints.Text(root, "reason", 2000);
            var result = review ? await service.ReviewAsync(actor, quoteId, cycle, associationId, version, child, QuoteReferralEndpoints.Text(root, "outcome", 20), QuoteReferralEndpoints.Text(root, "expectedFingerprint", 64), reason, key, Guid.NewGuid(), context.RequestAborted)
                : await service.WithdrawAsync(actor, quoteId, cycle, associationId, version, child, reason, key, Guid.NewGuid(), context.RequestAborted);
            return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> List(Guid quoteId, HttpContext context, QuoteReferralReadModel reads, PartyPaging paging)
    {
        try { QuoteEndpoints.Id(quoteId); return await QuoteReferralEndpoints.Page(context, reads, paging, quoteId, "evidence", null); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Events(Guid quoteId, Guid associationId, HttpContext context, QuoteReferralReadModel reads, PartyPaging paging)
    {
        try { QuoteEndpoints.Id(quoteId); QuoteEndpoints.Id(associationId); return await QuoteReferralEndpoints.Page(context, reads, paging, quoteId, "events", associationId); }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
