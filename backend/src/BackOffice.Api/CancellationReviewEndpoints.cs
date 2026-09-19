using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Http.Features;

namespace BackOffice.Api;

public static class CancellationReviewEndpoints
{
    private static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web) { Converters = { new DecimalStrings() } };
    public static void MapCancellationReview(this WebApplication app)
    {
        app.MapGet("/api/v1/drafts/{draftId:guid}/cancellation-preview", Read).RequireAuthorization("policy-read");
        app.MapPost("/api/v1/drafts/{draftId:guid}/cancellation-preview", Prepare).RequireAuthorization("policy-draft-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/cancellation-approvals", Approve).RequireAuthorization("underwriting-decide-within-authority");
        app.MapGet("/api/v1/drafts/{draftId:guid}/cancellation-evidence", Evidence).RequireAuthorization("policy-read");
        app.MapPost("/api/v1/drafts/{draftId:guid}/cancellation-evidence/uploads", Upload).RequireAuthorization("underwriting-evidence-write");
        app.MapPost("/api/v1/drafts/{draftId:guid}/cancellation-evidence/{evidenceId:guid}/reviews", Review).RequireAuthorization("underwriting-evidence-review");
    }

    private static async Task<IResult> Read(Guid draftId, HttpContext context, CancellationReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(draftId); QuoteHttpInput.NoQuery(context.Request);
            var result = await service.ReadAsync(LocalIdentityService.Actor(context.User), draftId, context.RequestAborted);
            context.Response.Headers.ETag = result.DraftEtag; return Results.Json(result, Output);
        }
        catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
    }

    private static async Task<IResult> Evidence(Guid draftId, HttpContext context, CancellationReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            QuoteEndpoints.Id(draftId); QuoteHttpInput.NoQuery(context.Request);
            var result = await service.EvidenceAsync(LocalIdentityService.Actor(context.User), draftId, context.RequestAborted);
            context.Response.Headers.ETag = result.DraftEtag; return Results.Json(result);
        }
        catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
    }

    private static async Task<IResult> Prepare(Guid draftId, HttpContext context, CancellationReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var command = Command(draftId, context); using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 8192);
            QuoteHttpInput.Keys(doc.RootElement, "previewHash");
            return Outcome(context, await service.PrepareAsync(LocalIdentityService.Actor(context.User), draftId, command.Version, command.Lease,
                QuoteReferralEndpoints.Text(doc.RootElement, "previewHash", 64), command.Key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
    }

    private static async Task<IResult> Approve(Guid draftId, HttpContext context, CancellationReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var command = Command(draftId, context); using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 8192);
            QuoteHttpInput.Keys(doc.RootElement, "previewId", "previewHash", "reason");
            return Outcome(context, await service.ApproveAsync(LocalIdentityService.Actor(context.User), draftId, command.Version, command.Lease,
                QuoteHttpInput.Id(doc.RootElement, "previewId"), QuoteReferralEndpoints.Text(doc.RootElement, "previewHash", 64),
                QuoteReferralEndpoints.Text(doc.RootElement, "reason", 2000), command.Key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
    }

    private static async Task<IResult> Review(Guid draftId, Guid evidenceId, HttpContext context, CancellationReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var command = Command(draftId, context); QuoteEndpoints.Id(evidenceId);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted, 8192);
            QuoteHttpInput.Keys(doc.RootElement, "outcome", "reason");
            return Outcome(context, await service.ReviewEvidenceAsync(LocalIdentityService.Actor(context.User), draftId, evidenceId, command.Version, command.Lease,
                QuoteReferralEndpoints.Text(doc.RootElement, "outcome", 20), QuoteReferralEndpoints.Text(doc.RootElement, "reason", 2000), command.Key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
    }

    private static async Task<IResult> Upload(Guid draftId, HttpContext context, CancellationReviewService service)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var command = Command(draftId, context); var token = context.RequestAborted;
            if (!context.Request.HasFormContentType || !context.Request.ContentType!.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                throw new QuoteHttpException(415, "multipart-required");
            const int maximum = QuoteEvidenceRules.MaximumFileBytes + 16384;
            if (context.Request.ContentLength > maximum) throw new QuoteHttpException(413, "evidence-file-size");
            using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, token)) > 0)
            { if (body.Length + count > maximum) throw new QuoteHttpException(413, "evidence-file-size"); body.Write(buffer, 0, count); }
            body.Position = 0; context.Request.Body = body;
            var form = await context.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maximum, ValueLengthLimit = 256, ValueCountLimit = 8, MultipartHeadersLengthLimit = 1024, MultipartHeadersCountLimit = 8 }, token);
            if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Count is < 3 or > 4 ||
                form.Keys.Any(x => x is not ("fileName" or "contentType" or "purpose" or "noticeDeliveredAt")) ||
                new[] { "fileName", "contentType", "purpose" }.Any(x => form[x].Count != 1) || form.ContainsKey("noticeDeliveredAt") && form["noticeDeliveredAt"].Count != 1)
                throw new QuoteHttpException(422, "evidence-upload-fields");
            DateTimeOffset? delivered = null;
            if (form.ContainsKey("noticeDeliveredAt"))
            {
                if (!DateTimeOffset.TryParseExact(form["noticeDeliveredAt"], "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.Offset != TimeSpan.Zero)
                    throw new QuoteHttpException(422, "cancellation-notice-time-invalid");
                delivered = parsed;
            }
            var upload = form.Files[0];
            if (upload.Length is 0 or > QuoteEvidenceRules.MaximumFileBytes) throw new QuoteHttpException(413, "evidence-file-size");
            if (upload.FileName != form["fileName"].ToString() || upload.ContentType != form["contentType"].ToString()) throw new QuoteHttpException(422, "evidence-upload-metadata");
            using var file = new MemoryStream(); await upload.CopyToAsync(file, token);
            return Outcome(context, await service.UploadAsync(LocalIdentityService.Actor(context.User), draftId, command.Version, command.Lease,
                form["purpose"].ToString(), delivered, upload.FileName, upload.ContentType, file.ToArray(), command.Key, Guid.NewGuid(), token));
        }
        catch (InvalidDataException) { return IdentityEndpoints.Problem(context, 422, "invalid-evidence-upload", "Check the upload format and size."); }
        catch (Exception e) when (QuoteEndpoints.Known(e)) { return QuoteEndpoints.Failure(context, e); }
    }

    private static (string Key, byte[] Version, Guid Lease) Command(Guid id, HttpContext context)
    { QuoteEndpoints.Id(id); QuoteHttpInput.NoQuery(context.Request); return (QuoteHttpInput.Key(context.Request), QuoteHttpInput.Version(context.Request), ServicingEndpoints.Fence(context.Request)); }
    private static IResult Outcome(HttpContext context, CommandOutcome result)
    { context.Response.Headers.ETag = result.Etag; return Results.Content(result.Body, "application/json", statusCode: result.Status); }
    private sealed class DecimalStrings : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("0.00", CultureInfo.InvariantCulture));
    }
}
