using System.Globalization;
using System.Text.Json;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public static class CapacityEndpoints
{
    public static void MapCapacity(this WebApplication app)
    {
        app.MapPost("/api/v1/referrals/{referralId:guid}/escalations", Create).RequireAuthorization("underwriting-escalate");
        app.MapGet("/api/v1/escalations/{escalationId:guid}", Read).RequireAuthorization("underwriting-read");
        app.MapGet("/api/v1/escalations/{escalationId:guid}/messages", Messages).RequireAuthorization("underwriting-read");
        app.MapPost("/api/v1/escalations/{escalationId:guid}/send", Send).RequireAuthorization("underwriting-escalate");
        app.MapPost("/api/v1/escalations/{escalationId:guid}/responses", Response).RequireAuthorization("underwriting-record-capacity");
        app.MapPost("/api/v1/escalations/{escalationId:guid}/actions", Action).RequireAuthorization("underwriting-escalate");
    }
    private static async Task<IResult> Action(Guid escalationId, HttpContext context, CapacityService service, CapacityReadModel reads)
    {
        try
        {
            QuoteEndpoints.Id(escalationId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            var action = QuoteReferralEndpoints.Text(root, "action", 20);
            QuoteHttpInput.Keys(root, action == "assign" ? ["cycleId", "escalationEtag", "action", "assignedUserId", "reason"] : ["cycleId", "escalationEtag", "action", "reason"]);
            var actor = LocalIdentityService.Actor(context.User); var quoteId = await reads.QuoteForEscalationAsync(actor, escalationId, context.RequestAborted);
            return QuoteEndpoints.Outcome(context, await service.ActionAsync(actor, quoteId, QuoteHttpInput.Id(root, "cycleId"), escalationId, version,
                QuoteReferralEndpoints.Version(root, "escalationEtag"), action, action == "assign" ? QuoteHttpInput.Id(root, "assignedUserId") : null,
                QuoteReferralEndpoints.Text(root, "reason", 2000), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Create(Guid referralId, HttpContext context, CapacityService service, QuoteReferralReadModel referrals)
    {
        try
        {
            QuoteEndpoints.Id(referralId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "referralEtag", "providerId", "reason"); var actor = LocalIdentityService.Actor(context.User);
            var quoteId = await referrals.QuoteForReferralAsync(actor, referralId, context.RequestAborted);
            var result = await service.CreateAsync(actor, quoteId, QuoteHttpInput.Id(root, "cycleId"), referralId, version, QuoteReferralEndpoints.Version(root, "referralEtag"),
                QuoteHttpInput.Id(root, "providerId"), QuoteReferralEndpoints.Text(root, "reason", 2000), key, Guid.NewGuid(), context.RequestAborted);
            context.Response.Headers.Location = $"/api/v1/escalations/{result.ResourceId:D}"; return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Read(Guid escalationId, HttpContext context, CapacityReadModel reads)
    {
        try
        {
            QuoteEndpoints.Id(escalationId); QuoteHttpInput.NoQuery(context.Request);
            var result = await reads.GetAsync(LocalIdentityService.Actor(context.User), escalationId, context.RequestAborted);
            context.Response.Headers.ETag = (string)result["etag"]; return Results.Json(result, QuoteReferralEndpoints.Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Messages(Guid escalationId, HttpContext context, CapacityReadModel reads, PartyPaging paging)
    {
        try
        {
            QuoteEndpoints.Id(escalationId); var actor = LocalIdentityService.Actor(context.User);
            var version = await reads.MessagesVersionAsync(actor, escalationId, context.RequestAborted);
            var page = paging.ReadBound(context, actor, "capacity-messages", version) ?? throw new QuoteHttpException(400, "invalid-query");
            var rows = await reads.MessagesAsync(actor, escalationId, version, page.Offset, page.Size, context.RequestAborted);
            var result = new Dictionary<string, object> { ["items"] = rows.Items }; if (paging.Next(page, rows.More) is { } next) result["nextCursor"] = next;
            return Results.Json(result, QuoteReferralEndpoints.Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Send(Guid escalationId, HttpContext context, CapacityService service, CapacityReadModel reads)
    {
        try
        {
            QuoteEndpoints.Id(escalationId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "escalationEtag", "body", "evidenceAssociationIds", "scenarioVersionId");
            var ids = Array(root, "evidenceAssociationIds", 0).Select(x => x.ValueKind == JsonValueKind.String && Guid.TryParseExact(x.GetString(), "D", out var id) && id != Guid.Empty ? id : throw new QuoteHttpException(422, "capacity-evidence-invalid")).ToArray();
            var actor = LocalIdentityService.Actor(context.User); var quoteId = await reads.QuoteForEscalationAsync(actor, escalationId, context.RequestAborted);
            return QuoteEndpoints.Outcome(context, await service.SendAsync(actor, quoteId, QuoteHttpInput.Id(root, "cycleId"), escalationId, version, QuoteReferralEndpoints.Version(root, "escalationEtag"),
                Body(root), ids, QuoteHttpInput.Id(root, "scenarioVersionId"), key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Response(Guid escalationId, HttpContext context, CapacityService service, CapacityReadModel reads)
    {
        try
        {
            QuoteEndpoints.Id(escalationId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            var outcome = QuoteReferralEndpoints.Text(root, "outcome", 30); var approving = outcome is "approve" or "approve-with-conditions";
            if (!approving && outcome is not ("query" or "decline")) throw new QuoteHttpException(422, "capacity-outcome-invalid");
            var fields = new List<string> { "cycleId", "escalationEtag", "submissionId", "submissionHash", "providerUnderwriter", "providerReference", "body", "receivedAt", "evidenceAssociationId", "outcome" };
            if (approving) fields.AddRange(["validFrom", "validTo", "authorisedLimits"]); if (outcome == "approve-with-conditions") fields.Add("conditions");
            QuoteHttpInput.Keys(root, fields.ToArray());
            var response = new CapacityResponseInput(QuoteHttpInput.Id(root, "submissionId"), QuoteReferralEndpoints.Text(root, "submissionHash", 64), outcome,
                QuoteReferralEndpoints.Text(root, "providerUnderwriter", 200), QuoteReferralEndpoints.Text(root, "providerReference", 100), Body(root), Instant(root, "receivedAt"),
                QuoteHttpInput.Id(root, "evidenceAssociationId"), approving ? Instant(root, "validFrom") : null, approving ? Instant(root, "validTo") : null,
                approving ? Array(root, "authorisedLimits", 1) : [], outcome == "approve-with-conditions" ? Array(root, "conditions", 1) : []);
            var actor = LocalIdentityService.Actor(context.User); var quoteId = await reads.QuoteForEscalationAsync(actor, escalationId, context.RequestAborted);
            return QuoteEndpoints.Outcome(context, await service.RecordResponseAsync(actor, quoteId, QuoteHttpInput.Id(root, "cycleId"), escalationId, version,
                QuoteReferralEndpoints.Version(root, "escalationEtag"), response, key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static JsonElement[] Array(JsonElement root, string field, int minimum)
    {
        if (!root.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < minimum || value.GetArrayLength() > 20) throw new QuoteHttpException(422, "capacity-array-invalid");
        return value.EnumerateArray().Select(x => x.Clone()).ToArray();
    }
    private static DateTimeOffset Instant(JsonElement root, string field)
    {
        var value = QuoteReferralEndpoints.Text(root, field, 40); var offset = value.EndsWith('Z') || value.Length >= 6 && value[^6] is '+' or '-';
        if (!offset || !DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new QuoteHttpException(422, "capacity-time-invalid");
        return parsed.ToUniversalTime();
    }
    private static string Body(JsonElement root)
    {
        if (!root.TryGetProperty("body", out var value) || value.ValueKind != JsonValueKind.String) throw new QuoteHttpException(422, "capacity-body-required");
        return CapacityService.Correspondence(value.GetString()!);
    }
}
