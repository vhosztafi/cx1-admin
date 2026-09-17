using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record QuoteTermsRecipient(Guid Id, string Name, string Email);

public sealed partial class QuoteTermsService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public const string WorkKind = "quote-delivery";
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> PrepareAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid ratingId,
        Guid templateVersionId, byte[] version, string key, Guid correlationId, CancellationToken token = default)
    {
        UnderwritingDecisionContext? held = null; TemplateVersion? template = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/terms/prepare", key, correlationId),
            new { quoteId, cycleId, ratingId, templateVersionId, version = Convert.ToBase64String(version) }, "quote.terms-prepared",
            async (db, ct) => {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "quote-terms", time.GetUtcNow(), false, ct);
                template = await Template(db, held, templateVersionId, time.GetUtcNow(), ct);
            },
            async (db, ct) => {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                if (held.Rating!.Id != ratingId) throw new QuoteOperationException(412, "stale-quote-rating");
                await Ready(db, held, now, signing: false, ct);
                var payload = await Payload(db, held, template!, ct);
                var hash = UnderwritingHashes.Terms(cycleId, ratingId, QuoteService.Pins(held.Revision), payload);
                var cycle = await TrackedCycle(db, held, ct);
                var current = cycle.CurrentTermsVersionId is Guid id ? await db.Set<QuoteTermsVersion>().SingleAsync(x => x.Id == id, ct) : null;
                // Documentary review and signature changes are deliberately absent
                // from the contractual payload, so preparing after signing reuses it.
                if (current is not null && current.TermsHash == hash && current.TemplateVersionId == templateVersionId)
                    return await held.Receipt(db, current.Id, 201, "quote.terms-reused", now, ct);
                var row = new QuoteTermsVersion { QuoteId = quoteId, CycleId = cycleId, RatingId = ratingId, TemplateVersionId = templateVersionId,
                    Number = checked((await db.Set<QuoteTermsVersion>().Where(x => x.CycleId == cycleId).MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1),
                    TermsHash = hash, TermsJson = payload.GetRawText(), AssuranceHashAtPreparation = await UnderwritingEvidenceService.Assurance(db, held.Cycle, held.Revision, ct),
                    PreparedAt = now, PreparedBy = actor.UserId, CreatedAt = now, CreatedBy = actor.UserId };
                db.Add(row); await db.SaveChangesAsync(ct);
                cycle.CurrentTermsVersionId = row.Id; cycle.CurrentDeliveryId = null; cycle.CurrentAcceptanceId = null;
                SetState(db, held, "approved");
                return await held.Receipt(db, row.Id, 201, "quote.terms-prepared", now, ct);
            }, token);
    }

    public Task<CommandOutcome> SendAsync(ActorContext actor, Guid quoteId, Guid termsId, IReadOnlyList<Guid> recipients,
        byte[] version, string key, Guid correlationId, CancellationToken token = default)
    {
        if (recipients.Count is < 1 or > 20 || recipients.Contains(Guid.Empty) || recipients.Distinct().Count() != recipients.Count) throw new QuoteOperationException(422, "quote-recipients-invalid");
        var ids = recipients.Order().ToArray(); UnderwritingDecisionContext? held = null; QuoteTermsRecipient[]? snapshots = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/terms", key, correlationId),
            new { quoteId, termsId, ids, version = Convert.ToBase64String(version) }, "quote.terms-queued",
            async (db, ct) => {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, null, "quote-terms", time.GetUtcNow(), false, ct);
                snapshots = await Recipients(db, held, ids, ct);
            },
            async (db, ct) => {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                var terms = await CurrentTerms(db, held, termsId, now, ct); await Ready(db, held, now, signing: true, ct);
                var scenario = await db.Set<SettingVersion>().FromSqlRaw("SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope=N'quote-delivery'").AsNoTracking()
                    .Where(x => x.EffectiveFrom <= now).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
                if (scenario is null || QuoteTermsSeed.Scenario(scenario) is null) throw new QuoteOperationException(503, "quote-delivery-configuration-unavailable");
                var delivery = new QuoteTermsDelivery { QuoteId = quoteId, CycleId = held.Cycle.Id, TermsVersionId = terms.Id,
                    ScenarioVersionId = scenario.Id, SentBy = actor.UserId, RecipientSnapshotJson = JsonSerializer.Serialize(snapshots, QuoteRatingService.Json),
                    AssuranceHashAtSend = await UnderwritingEvidenceService.Assurance(db, held.Cycle, held.Revision, ct), CreatedAt = now, UpdatedAt = now, CreatedBy = actor.UserId };
                delivery.PayloadJson = JsonSerializer.Serialize(new { format = "quote-delivery-1", deliveryId = delivery.Id, quoteId, termsVersionId = terms.Id, termsHash = terms.TermsHash,
                    recipients = snapshots, document = JsonSerializer.Deserialize<JsonElement>(terms.TermsJson) }, QuoteRatingService.Json);
                delivery.PayloadHash = QuoteCanonicalJson.Create(delivery.PayloadJson, QuoteService.Pins(held.Revision)).ContentHash;
                var work = new OutboxWork { Kind = WorkKind, OperationKey = $"quote-delivery/{delivery.Id:N}", SubjectRecordId = delivery.Id, ScenarioVersionId = scenario.Id,
                    Payload = JsonSerializer.Serialize(new { deliveryId = delivery.Id, quoteId }), CreatedAt = now, NextAttemptAt = now, CorrelationId = correlationId };
                db.Add(work); await db.SaveChangesAsync(ct); delivery.WorkId = work.Id; db.Add(delivery); await db.SaveChangesAsync(ct);
                var cycle = await TrackedCycle(db, held, ct); cycle.CurrentDeliveryId = delivery.Id; cycle.CurrentAcceptanceId = null;
                SetState(db, held, "approved");
                var receipt = await held.Receipt(db, delivery.Id, 202, "quote.terms-queued", now, ct);
                return receipt with { Body = JsonSerializer.Serialize(new { id = delivery.Id, quoteId, quoteEtag = receipt.Etag, jobId = work.Id, state = "queued" }) };
            }, token);
    }

    internal static async Task<UnderwritingCycle> TrackedCycle(BackOfficeDbContext db, UnderwritingDecisionContext held, CancellationToken token) =>
        await db.Set<UnderwritingCycle>().SingleAsync(x => x.Id == held.Cycle.Id && x.QuoteId == held.Cycle.QuoteId, token);

    internal static void SetState(BackOfficeDbContext db, UnderwritingDecisionContext held, string state)
    {
        var quote = held.Owned.Quote;
        if (db.Entry(quote).State == EntityState.Detached) db.Attach(quote);
        quote.State = state;
    }

    internal static async Task<TemplateVersion> Template(BackOfficeDbContext db, UnderwritingDecisionContext held, Guid id, DateTimeOffset now, CancellationToken token)
    {
        var template = await db.Set<TemplateVersion>().FromSqlInterpolated($"SELECT * FROM TemplateVersion WITH(HOLDLOCK) WHERE Id={id} AND ProductId={held.Cycle.ProductId}").AsNoTracking().SingleOrDefaultAsync(token);
        if (template is null || template.State != "published" || template.Kind != "quote-terms" || template.EffectiveFrom > now || now >= template.EffectiveTo) throw new QuoteOperationException(409, "quote-template-unavailable");
        try
        {
            using var doc = JsonDocument.Parse(template.ContentJson); var root = doc.RootElement;
            if (root.EnumerateObject().Count() != 3 || root.GetProperty("format").GetString() != "quote-template-1" ||
                root.GetProperty("title").ValueKind != JsonValueKind.String || root.GetProperty("notice").ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()) || root.GetProperty("title").GetString()!.Length > 300 ||
                string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()) || root.GetProperty("notice").GetString()!.Length > 8000)
                throw new QuoteOperationException(503, "quote-template-invalid");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new QuoteOperationException(503, "quote-template-invalid"); }
        return template;
    }

    internal static async Task<QuoteTermsVersion> CurrentTerms(BackOfficeDbContext db, UnderwritingDecisionContext held, Guid id, DateTimeOffset now, CancellationToken token)
    {
        var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CycleId == held.Cycle.Id && x.QuoteId == held.Cycle.QuoteId, token)
            ?? throw new QuoteOperationException(404, "quote-terms-not-found");
        if (held.Cycle.CurrentTermsVersionId != terms.Id || held.Rating?.Id != terms.RatingId) throw new QuoteOperationException(409, "quote-terms-stale");
        var template = await Template(db, held, terms.TemplateVersionId, now, token);
        var hash = UnderwritingHashes.Terms(held.Cycle.Id, terms.RatingId, QuoteService.Pins(held.Revision), await Payload(db, held, template, token));
        if (hash != terms.TermsHash) throw new QuoteOperationException(409, "quote-terms-stale");
        return terms;
    }

    internal static async Task<QuoteTermsRecipient[]> Recipients(BackOfficeDbContext db, UnderwritingDecisionContext held, IReadOnlyList<Guid> ids, CancellationToken token)
    {
        var contacts = await db.Set<Contact>().FromSqlInterpolated($"SELECT * FROM Contact WITH(HOLDLOCK) WHERE ClientId={held.Cycle.ClientId} AND RelationshipId={held.Cycle.RelationshipId}").AsNoTracking().ToArrayAsync(token);
        var result = new List<QuoteTermsRecipient>();
        foreach (var id in ids.Order())
        {
            var contact = contacts.SingleOrDefault(x => x.Id == id && x.EndedAt is null);
            if (contact is null || string.IsNullOrWhiteSpace(contact.Email) || !System.Net.Mail.MailAddress.TryCreate(contact.Email, out var address) || address.Address != contact.Email || contact.Email.Any(char.IsControl))
                throw new QuoteOperationException(409, "quote-recipient-unavailable");
            result.Add(new(contact.Id, contact.DeclaredFullName, contact.Email));
        }
        return result.ToArray();
    }
}
