using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteLookupView(Guid Id, Guid RevisionId, string Kind, string Scope, Guid? RiskItemId,
    string InputFingerprint, string Scenario, string State, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt,
    int Attempts, string WorkState, string? ErrorCode, DateTimeOffset? NextAttemptAt,
    string? Source, DateTimeOffset? AsOf, QuoteLookupCandidate[] Candidates, Guid? SelectedRevisionId);

public sealed class QuoteLookupService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    public const string WorkKind = "quote-lookup";

    public async Task<QuoteLookupView[]> ReadAsync(ActorContext actor, Guid quoteId, Guid? lookupId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var rows = await (from lookup in db.Set<QuoteLookup>().AsNoTracking()
            join work in db.Set<OutboxWork>().AsNoTracking() on lookup.WorkId equals work.Id
            where lookup.QuoteId == quoteId && (lookupId == null || lookup.Id == lookupId)
            orderby lookup.CreatedAt descending, lookup.Id descending
            select new { Lookup = lookup, work.Attempts, WorkState = work.State, work.ErrorCode, work.NextAttemptAt,
                SelectedRevisionId = db.Set<QuoteLookupSelection>().Where(x => x.LookupId == lookup.Id).Select(x => (Guid?)x.NewRevisionId).FirstOrDefault() })
            .Take(100).ToArrayAsync(token);
        if (lookupId is not null && rows.Length == 0) throw new QuoteOperationException(404, "lookup-not-found");
        var views = rows.Select(row =>
        {
            var lookup = row.Lookup;
            var outcome = lookup.State is "succeeded" or "no-match" or "rejected"
                ? JsonSerializer.Deserialize<QuoteLookupOutcome>(lookup.ResultJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null;
            return new QuoteLookupView(lookup.Id, lookup.RevisionId, lookup.Kind, lookup.TargetScope, lookup.RiskItemId,
                lookup.InputFingerprint, lookup.Scenario, lookup.State, lookup.CreatedAt, lookup.CompletedAt,
                row.Attempts, row.WorkState, row.ErrorCode, row.WorkState == "pending" ? row.NextAttemptAt : null,
                outcome?.Source, outcome?.AsOf, outcome?.Candidates ?? [], row.SelectedRevisionId);
        }).ToArray();
        await transaction.CommitAsync(token); return views;
    }

    public Task<CommandOutcome> SelectAsync(ActorContext actor, Guid quoteId, Guid lookupId, byte[] expectedVersion,
        Guid revisionId, string fingerprint, Guid? candidateId, string? manualReason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (expectedVersion.Length != 8 || revisionId == Guid.Empty || lookupId == Guid.Empty)
            throw new QuoteOperationException(400, "invalid-lookup-version");
        if ((candidateId is null) == (manualReason is null) || candidateId == Guid.Empty)
            throw new QuoteOperationException(422, "lookup-selection-required");
        if (manualReason is not null) manualReason = QuoteLookupRules.ManualReason(manualReason);
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null; QuoteLookup? lookup = null; EligibleQuoteCapture? capture = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/lookup-selections", key, correlationId),
            new { quoteId, lookupId, revisionId, fingerprint, candidateId, manualReason, version = Convert.ToBase64String(expectedVersion) }, "quote.lookup-selected-command",
            async (db, ct) =>
            {
                owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Capture, ct);
                revision = await QuoteService.CurrentRevision(db, owned.Quote, ct);
                capture = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, revision.ProductVersionId, time.GetUtcNow(), revision.AgencyTermsVersionId, ct);
                QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), capture.Pins);
                lookup = await db.Set<QuoteLookup>().FromSqlInterpolated($"SELECT * FROM QuoteLookup WITH(HOLDLOCK) WHERE Id={lookupId} AND QuoteId={quoteId}")
                    .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new QuoteOperationException(404, "lookup-not-found");
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote; QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
                if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, expectedVersion) || revision!.Id != revisionId || lookup!.RevisionId != revisionId || lookup.InputFingerprint != fingerprint)
                    throw new QuoteOperationException(412, "stale-lookup-input");
                if (await db.Set<QuoteLookupSelection>().AnyAsync(x => x.LookupId == lookupId, ct)) throw new QuoteOperationException(409, "lookup-already-selected");
                using var document = JsonDocument.Parse(revision.ProposalJson);
                var target = new QuoteLookupTarget(lookup.Kind, lookup.TargetScope, lookup.RiskItemId);
                QuoteLookupRules.EnsureCurrent(document.RootElement, target, capture!.Pins, fingerprint);
                var proposal = JsonNode.Parse(revision.ProposalJson)!;
                if (candidateId is not null)
                {
                    if (lookup.State != "succeeded" || lookup.ResultJson is null) throw new QuoteOperationException(409, "lookup-result-unavailable");
                    var outcome = JsonSerializer.Deserialize<QuoteLookupOutcome>(lookup.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                    var candidates = outcome.Candidates.Where(x => x.Id == candidateId).ToArray();
                    if (candidates.Length != 1) throw new QuoteOperationException(422, "lookup-candidate-not-found");
                    ApplyCandidate(proposal, target, candidates[0].Patch);
                }
                var prepared = QuoteRules.Prepare(proposal.ToJsonString(), capture.Product.Code, capture.Pins);
                db.Attach(quote); var now = time.GetUtcNow();
                await QuoteService.Append(db, quote, capture, prepared, checked(revision.Number + 1), manualReason, actor.UserId, now, ct);
                db.Add(new QuoteLookupSelection { LookupId = lookup.Id, QuoteId = quoteId, SourceRevisionId = revisionId,
                    InputFingerprint = fingerprint, NewRevisionId = quote.CurrentRevisionId!.Value, CandidateId = candidateId,
                    ManualReason = manualReason, ActorId = actor.UserId, CreatedBy = actor.UserId, CreatedAt = now });
                await db.SaveChangesAsync(ct);
                return new CommandOutcome(quoteId, 200, JsonSerializer.Serialize(new { id = quoteId }), Etag: "\"" + Convert.ToBase64String(quote.RowVersion) + "\"");
            }, token);
    }

    private static void ApplyCandidate(JsonNode proposal, QuoteLookupTarget target, JsonElement patch)
    {
        JsonNode subject = target.Scope == "insured" ? proposal["insured"]! : proposal["risk"]![target.Scope switch {
            "driver" => "drivers", "vehicle" => "vehicles", _ => "premises" }]!.AsArray().Single(row => Guid.Parse(row!["id"]!.GetValue<string>()) == target.RiskItemId)!;
        if (patch.ValueKind != JsonValueKind.Object) throw new QuoteOperationException(409, "lookup-result-invalid");
        foreach (var field in patch.EnumerateObject())
        {
            if (target.Kind == "address" && field.Name == "address" && field.Value.ValueKind == JsonValueKind.Object)
            {
                subject["address"] ??= new JsonObject();
                foreach (var part in field.Value.EnumerateObject())
                {
                    if (part.Name is not ("houseNumber" or "street" or "town" or "city" or "county" or "postcode") || part.Value.ValueKind != JsonValueKind.String)
                        throw new QuoteOperationException(409, "lookup-result-invalid");
                    subject["address"]![part.Name] = part.Value.GetString();
                }
            }
            else if (target.Kind == "vehicle" && field.Name is "make" or "model" && field.Value.ValueKind == JsonValueKind.String) subject[field.Name] = field.Value.GetString();
            else throw new QuoteOperationException(409, "lookup-result-invalid");
        }
    }

    public Task<CommandOutcome> RequestAsync(ActorContext actor, Guid quoteId, byte[] expectedVersion,
        Guid revisionId, QuoteLookupTarget target, string scenario, string key, Guid correlationId, CancellationToken token = default)
    {
        if (expectedVersion.Length != 8 || revisionId == Guid.Empty) throw new QuoteOperationException(400, "invalid-lookup-version");
        scenario = QuoteLookupRules.Scenario(scenario);
        OwnedQuoteScope? owned = null; QuoteRevision? revision = null; EligibleQuoteCapture? capture = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/quotes/{quoteId:D}/lookups", key, correlationId),
            new { quoteId, revisionId, target, scenario, version = Convert.ToBase64String(expectedVersion) }, "quote.lookup-requested-command",
            async (db, ct) =>
            {
                owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Capture, ct);
                revision = await QuoteService.CurrentRevision(db, owned.Quote, ct);
                capture = await QuoteCaptureEligibility.ResolveAsync(db, owned.Scope, revision.ProductVersionId,
                    time.GetUtcNow(), revision.AgencyTermsVersionId, ct);
                QuoteRules.EnsureRetainedPins(QuoteService.Pins(revision), capture.Pins);
            },
            async (db, ct) =>
            {
                var quote = owned!.Quote;
                QuoteRules.EnsureEditable(quote.State, quote.CaptureClosedAt);
                if (!CryptographicOperations.FixedTimeEquals(quote.RowVersion, expectedVersion) || revision!.Id != revisionId)
                    throw new QuoteOperationException(412, "stale-quote");
                using var proposal = JsonDocument.Parse(revision.ProposalJson);
                var input = QuoteLookupRules.Prepare(proposal.RootElement, target, capture!.Pins);
                var scope = WorkKind + "/" + scenario; var now = time.GetUtcNow();
                var setting = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Scope={scope} AND EffectiveFrom<={now}")
                    .AsNoTracking().OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(503, "lookup-demo-configuration-unavailable");
                using var configuration = JsonDocument.Parse(setting.Values); var root = configuration.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("demo", out var demo) || demo.ValueKind != JsonValueKind.True ||
                    !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != WorkKind ||
                    !root.TryGetProperty("scenario", out var configured) || configured.ValueKind != JsonValueKind.String || configured.GetString() != scenario)
                    throw new QuoteOperationException(503, "lookup-demo-configuration-unavailable");
                var hash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { quoteId, revisionId, target, input.InputFingerprint, setting.Id }));
                var existing = await db.Set<QuoteLookup>().AsNoTracking().SingleOrDefaultAsync(x => x.QuoteId == quoteId && x.RequestHash == hash, ct);
                if (existing is not null) return Receipt(existing.Id, quote.RowVersion);
                var lookup = new QuoteLookup { QuoteId = quoteId, RevisionId = revisionId, Kind = target.Kind, TargetScope = target.Scope,
                    RiskItemId = target.RiskItemId, Query = input.Query, InputFingerprint = input.InputFingerprint, RequestHash = hash,
                    ReferenceVersion = capture.Pins.ReferenceVersion, ScenarioVersionId = setting.Id, Scenario = scenario,
                    CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                var work = new OutboxWork { Kind = WorkKind, SubjectRecordId = lookup.Id, OperationKey = $"quote-lookup/{lookup.Id:N}",
                    ScenarioVersionId = setting.Id, Payload = JsonSerializer.Serialize(new { lookupId = lookup.Id }),
                    NextAttemptAt = now, CorrelationId = correlationId, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.Add(work); await db.SaveChangesAsync(ct); lookup.WorkId = work.Id; db.Add(lookup);
                db.Add(new QuoteActivity { QuoteId = quoteId, RevisionId = revisionId, ActorId = actor.UserId, CreatedBy = actor.UserId,
                    CreatedAt = now, OccurredAt = now, EventType = "quote.lookup-requested" });
                await db.SaveChangesAsync(ct);
                return Receipt(lookup.Id, quote.RowVersion);
            }, token);
    }

    public async Task<QuoteLookup> GetAsync(ActorContext actor, Guid quoteId, Guid lookupId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        var lookup = await db.Set<QuoteLookup>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == lookupId && x.QuoteId == quoteId, token)
            ?? throw new QuoteOperationException(404, "lookup-not-found");
        await transaction.CommitAsync(token); return lookup;
    }

    private static CommandOutcome Receipt(Guid id, byte[] quoteVersion) => new(id, 202, JsonSerializer.Serialize(new { id }),
        Etag: "\"" + Convert.ToBase64String(quoteVersion) + "\"");
}
