using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Underwriting;

namespace BackOffice.Api;

public static class QuoteTermsEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static void MapQuoteTerms(this WebApplication app)
    {
        app.MapPost("/api/v1/quotes/{quoteId:guid}/terms/prepare", Prepare).RequireAuthorization("quote-terms");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/terms", Send).RequireAuthorization("quote-terms");
        app.MapPost("/api/v1/quotes/{quoteId:guid}/acceptances", Accept).RequireAuthorization("quote-acceptance");
        app.MapGet("/api/v1/quotes/{quoteId:guid}/terms", Read).RequireAuthorization("quote-read");
    }
    private static async Task<IResult> Prepare(Guid quoteId, HttpContext context, QuoteTermsService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "ratingId", "templateVersionId");
            return QuoteEndpoints.Outcome(context, await service.PrepareAsync(LocalIdentityService.Actor(context.User), quoteId, QuoteHttpInput.Id(root, "cycleId"), QuoteHttpInput.Id(root, "ratingId"),
                QuoteHttpInput.Id(root, "templateVersionId"), version, key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Send(Guid quoteId, HttpContext context, QuoteTermsService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "termsVersionId", "recipientContactIds");
            if (!root.TryGetProperty("recipientContactIds", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 1 or > 20) throw new QuoteHttpException(422, "quote-recipients-invalid");
            var ids = list.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String && Guid.TryParseExact(x.GetString(), "D", out var id) && id != Guid.Empty ? id : throw new QuoteHttpException(422, "quote-recipients-invalid")).ToArray();
            var result = await service.SendAsync(LocalIdentityService.Actor(context.User), quoteId, QuoteHttpInput.Id(root, "termsVersionId"), ids, version, key, Guid.NewGuid(), context.RequestAborted);
            using var receipt = JsonDocument.Parse(result.Body); context.Response.Headers.Location = "/api/v1/jobs/" + receipt.RootElement.GetProperty("jobId").GetGuid();
            return QuoteEndpoints.Outcome(context, result);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Accept(Guid quoteId, HttpContext context, QuoteAcceptanceService service)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var key = QuoteHttpInput.Key(context.Request); var version = QuoteHttpInput.Version(context.Request);
            using var doc = await QuoteHttpInput.Read(context.Request, context.RequestAborted); var root = doc.RootElement;
            QuoteHttpInput.Keys(root, "cycleId", "ratingId", "termsVersionId", "termsHash", "assuranceHash", "accepterLabel", "acceptedAt", "channel", "evidenceAssociationId");
            var raw = QuoteReferralEndpoints.Text(root, "acceptedAt", 40); var offset = raw.EndsWith('Z') || raw.Length >= 6 && raw[^6] is '+' or '-';
            if (!offset || !DateTimeOffset.TryParseExact(raw, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var acceptedAt)) throw new QuoteHttpException(422, "quote-acceptance-time-invalid");
            var input = new QuoteAcceptanceInput(QuoteHttpInput.Id(root, "cycleId"), QuoteHttpInput.Id(root, "ratingId"), QuoteHttpInput.Id(root, "termsVersionId"), QuoteReferralEndpoints.Text(root, "termsHash", 64),
                QuoteReferralEndpoints.Text(root, "assuranceHash", 64), QuoteReferralEndpoints.Text(root, "accepterLabel", 200), acceptedAt, QuoteReferralEndpoints.Text(root, "channel", 20), QuoteHttpInput.Id(root, "evidenceAssociationId"));
            return QuoteEndpoints.Outcome(context, await service.RecordAsync(LocalIdentityService.Actor(context.User), quoteId, version, input, key, Guid.NewGuid(), context.RequestAborted));
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
    private static async Task<IResult> Read(Guid quoteId, HttpContext context, QuoteTermsReadModel service, PartyPaging paging)
    {
        try
        {
            QuoteEndpoints.Id(quoteId); var actor = LocalIdentityService.Actor(context.User);
            if (context.Request.Query.Keys.Except(["termsCursor", "deliveriesCursor", "acceptancesCursor", "pageSize"]).Any()) throw new QuoteHttpException(400, "invalid-query");
            var version = await service.VersionAsync(actor, quoteId, context.RequestAborted);
            PartyPaging.Page Page(string name)
            {
                var scoped = new DefaultHttpContext(); scoped.Request.Path = context.Request.Path;
                var query = new List<KeyValuePair<string, string?>>();
                foreach (var field in new[] { "pageSize", name + "Cursor" })
                    if (context.Request.Query.TryGetValue(field, out var value))
                    { if (value.Count != 1) throw new QuoteHttpException(400, "invalid-query"); query.Add(new(field == "pageSize" ? field : "cursor", value.ToString())); }
                scoped.Request.QueryString = QueryString.Create(query);
                return paging.ReadBound(scoped, actor, "quote-" + name, version) ?? throw new QuoteHttpException(400, "invalid-query");
            }
            var terms = Page("terms"); var deliveries = Page("deliveries"); var acceptances = Page("acceptances");
            var read = await service.ReadAsync(actor, quoteId, version, terms.Offset, deliveries.Offset, acceptances.Offset, terms.Size, context.RequestAborted);
            if (paging.Next(terms, read.MoreTerms) is { } nextTerms) read.View["nextTermsCursor"] = nextTerms;
            if (paging.Next(deliveries, read.MoreDeliveries) is { } nextDeliveries) read.View["nextDeliveriesCursor"] = nextDeliveries;
            if (paging.Next(acceptances, read.MoreAcceptances) is { } nextAcceptances) read.View["nextAcceptancesCursor"] = nextAcceptances;
            return Results.Json(read.View, Json);
        }
        catch (Exception error) when (QuoteEndpoints.Known(error)) { return QuoteEndpoints.Failure(context, error); }
    }
}
