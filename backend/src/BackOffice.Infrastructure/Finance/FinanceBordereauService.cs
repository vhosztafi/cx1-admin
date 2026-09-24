using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record BordereauMemberView(Guid SourceJournalId, Guid PolicyId, Guid TransactionId, Guid AgencyId,
    Guid ProductVersionId, Guid AgencyTermsVersionId, DateOnly PostingDate, DateTimeOffset PostedAt,
    string Premium, string Tax, string Fee, string Commission, string NetDue, string Currency,
    string PolicyReference, string ProviderProductCode, string AgencyReference,
    Guid? CorrectionActorId, string? CorrectionReason, DateTimeOffset? CorrectedAt,
    Guid? ExclusionActorId, string? ExclusionReason, DateTimeOffset? ExcludedAt);
public sealed record BordereauVersionView(Guid Id, Guid BatchId, Guid ProviderId, Guid AccountingPeriodId,
    int Number, Guid? ParentVersionId, DateTimeOffset SourceCutoff, string SourceHash, string MembersHash,
    string SchemaVersion, string State, IReadOnlyList<BordereauValidationIssue> Validation,
    string? ContentHash, IReadOnlyList<BordereauMemberView> Members, DateTimeOffset CreatedAt);
public sealed record BordereauBatchItem(Guid Id, Guid ProviderId, Guid AccountingPeriodId, Guid CurrentVersionId,
    int Version, string State, DateTimeOffset CreatedAt);
public sealed record BordereauPage(int Page, int PageSize, int Total, IReadOnlyList<BordereauBatchItem> Items);
public sealed record BordereauDownload(byte[] Bytes, string Hash, string FileName);

public sealed class FinanceBordereauService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> GenerateAsync(ActorContext actor, Guid providerId, Guid periodId,
        string key, Guid correlationId, CancellationToken token = default)
    {
        if (providerId == Guid.Empty || periodId == Guid.Empty) throw new QuoteOperationException(400, "bordereau-scope-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/providers/{providerId:N}/periods/{periodId:N}/bordereaux", key, correlationId),
            new { providerId, periodId }, "finance.bordereau.generated",
            (db, ct) => Authorize(db, actor, providerId, ct, true),
            async (db, ct) =>
            {
                var period = await db.Set<AccountingPeriod>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId, ct)
                    ?? throw new QuoteOperationException(404, "bordereau-period-not-found");
                var now = time.GetUtcNow();
                var sources = await (from j in db.Set<Journal>().AsNoTracking()
                    join o in db.Set<IssueFinancialObligation>().AsNoTracking() on j.ObligationId equals o.Id
                    join p in db.Set<Policy>().AsNoTracking() on o.PolicyId equals p.Id
                    join t in db.Set<PolicyTerm>().AsNoTracking() on o.TermId equals t.Id
                    join pv in db.Set<ProductVersion>().AsNoTracking() on t.ProductVersionId equals pv.Id
                    join product in db.Set<Product>().AsNoTracking() on pv.ProductId equals product.Id
                    join agency in db.Set<Agency>().AsNoTracking() on o.AgencyId equals agency.Id
                    where j.PostedAt != null && j.PostedAt <= now && o.ProviderId == providerId
                        && pv.ProviderId == providerId
                    select new { Journal = j, Obligation = o, Policy = p, Term = t,
                        ProductVersion = pv, Product = product, Agency = agency }).ToArrayAsync(ct);
                var selected = sources.Where(x => (x.Journal.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.Journal.PostedAt!.Value)) >= period.StartsOn
                    && (x.Journal.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.Journal.PostedAt!.Value)) < period.EndsOn)
                    .OrderBy(x => x.Journal.Id).ToArray();
                var batch = new FinanceBordereauBatch { ProviderId = providerId, AccountingPeriodId = periodId,
                    CreatedAt = now, UpdatedAt = now, CreatedBy = actor.UserId };
                var version = new FinanceBordereauVersion { BatchId = batch.Id, Number = 1, SourceCutoff = now,
                    SchemaVersion = "1", CreatedAt = now, CreatedBy = actor.UserId };
                var members = selected.Select(x => new FinanceBordereauMember
                {
                    VersionId = version.Id, SourceJournalId = x.Journal.Id,
                    PolicyId = x.Obligation.PolicyId, TransactionId = x.Obligation.TransactionId,
                    AgencyId = x.Obligation.AgencyId, ProductVersionId = x.ProductVersion.Id,
                    AgencyTermsVersionId = x.Obligation.AgencyTermsVersionId,
                    PostingDate = x.Journal.PostingDate ?? FinanceLedgerMath.LegacyPostingDate(x.Journal.PostedAt!.Value),
                    PostedAt = x.Journal.PostedAt!.Value, Currency = x.Obligation.Currency,
                    Premium = x.Obligation.Premium, Tax = x.Obligation.Tax, Fee = x.Obligation.Fee,
                    Commission = x.Obligation.Commission, NetDue = x.Obligation.NetDue,
                    PolicyReference = x.Policy.Reference, ProviderProductCode = x.Product.Code,
                    AgencyReference = x.Agency.Reference, CreatedAt = now, CreatedBy = actor.UserId
                }).ToArray();
                if (members.Select(x => x.SourceJournalId).Distinct().Count() != members.Length)
                    throw new InvalidOperationException("Posted source journal appears twice in bordereau.");
                version.SourceHash = Hash(members);
                version.MembersHash = Hash(members);
                db.Add(batch);
                await db.SaveChangesAsync(ct);
                db.Add(version); db.AddRange(members);
                await db.SaveChangesAsync(ct);
                batch.CurrentVersionId = version.Id;
                await db.SaveChangesAsync(ct);
                return Outcome(batch.Id, version);
            }, token, IsolationLevel.Serializable);
    }

    public Task<CommandOutcome> CorrectAsync(ActorContext actor, Guid batchId, Guid expectedVersionId,
        Guid sourceJournalId, string? policyReference, string? providerProductCode, string? agencyReference,
        string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (sourceJournalId == Guid.Empty || expectedVersionId == Guid.Empty || !Reason(reason))
            throw new QuoteOperationException(400, "bordereau-correction-invalid");
        return Revise(actor, batchId, expectedVersionId, sourceJournalId, "correct",
            new { policyReference, providerProductCode, agencyReference, reason }, key, correlationId,
            "finance.bordereau.corrected", (member, now) =>
            {
                if (policyReference is not null) member.PolicyReference = policyReference;
                if (providerProductCode is not null) member.ProviderProductCode = providerProductCode;
                if (agencyReference is not null) member.AgencyReference = agencyReference;
                member.CorrectionActorId = actor.UserId; member.CorrectionReason = reason; member.CorrectedAt = now;
            }, token);
    }

    public Task<CommandOutcome> ExcludeAsync(ActorContext actor, Guid batchId, Guid expectedVersionId,
        Guid sourceJournalId, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        if (sourceJournalId == Guid.Empty || expectedVersionId == Guid.Empty || !Reason(reason))
            throw new QuoteOperationException(400, "bordereau-exclusion-invalid");
        return Revise(actor, batchId, expectedVersionId, sourceJournalId, "exclude", new { reason }, key, correlationId,
            "finance.bordereau.excluded", (member, now) =>
            {
                member.ExclusionActorId = actor.UserId; member.ExclusionReason = reason; member.ExcludedAt = now;
            }, token);
    }

    public Task<CommandOutcome> ValidateAsync(ActorContext actor, Guid batchId, Guid expectedVersionId,
        string key, Guid correlationId, CancellationToken token = default)
        => Revise(actor, batchId, expectedVersionId, null, "validate", new { }, key, correlationId,
            "finance.bordereau.validated", null, token);

    private Task<CommandOutcome> Revise(ActorContext actor, Guid batchId, Guid expectedVersionId,
        Guid? sourceJournalId, string action, object input, string key, Guid correlationId, string eventType,
        Action<FinanceBordereauMember, DateTimeOffset>? edit, CancellationToken token)
    {
        if (batchId == Guid.Empty || expectedVersionId == Guid.Empty) throw new QuoteOperationException(400, "bordereau-version-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
            $"/api/v1/finance/bordereaux/{batchId:N}/{action}", key, correlationId),
            new { batchId, expectedVersionId, sourceJournalId, input }, eventType,
            async (db, ct) =>
            {
                var providerId = await db.Set<FinanceBordereauBatch>().AsNoTracking().Where(x => x.Id == batchId)
                    .Select(x => (Guid?)x.ProviderId).SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "bordereau-not-found");
                await Authorize(db, actor, providerId, ct, true);
            },
            async (db, ct) =>
            {
                var batch = await db.Set<FinanceBordereauBatch>()
                    .FromSqlInterpolated($"SELECT * FROM FinanceBordereauBatch WITH(UPDLOCK,HOLDLOCK) WHERE Id={batchId}")
                    .SingleOrDefaultAsync(ct) ?? throw new QuoteOperationException(404, "bordereau-not-found");
                if (batch.CurrentVersionId != expectedVersionId)
                    throw new QuoteOperationException(412, "bordereau-version-stale");
                var submission = await db.Set<FinanceBordereauSubmission>().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.BatchId == batchId, ct);
                if (submission is { State: not ("submitted" or "rejected") } &&
                    !await db.Set<DemoProviderOperation>().AsNoTracking().AnyAsync(x =>
                        x.Kind == FinanceSubmissionWorker.WorkKind && x.OperationKey == submission.OperationKey &&
                        x.ScenarioVersionId == submission.ScenarioVersionId &&
                        x.RequestHash == submission.RequestHash && x.Result != null, ct))
                    throw new QuoteOperationException(409, "batch-submission-pending");
                var prior = await db.Set<FinanceBordereauVersion>().AsNoTracking().SingleAsync(x => x.Id == expectedVersionId, ct);
                var originals = await db.Set<FinanceBordereauMember>().AsNoTracking()
                    .Where(x => x.VersionId == prior.Id).OrderBy(x => x.SourceJournalId).ToArrayAsync(ct);
                var now = time.GetUtcNow();
                var next = new FinanceBordereauVersion { BatchId = batchId, ParentVersionId = prior.Id,
                    Number = prior.Number + 1, SourceCutoff = prior.SourceCutoff, SourceHash = prior.SourceHash,
                    SchemaVersion = prior.SchemaVersion, CreatedAt = now, CreatedBy = actor.UserId };
                var members = originals.Select(x => Copy(x, next.Id, now, actor.UserId)).ToArray();
                if (sourceJournalId is Guid source)
                {
                    var target = members.SingleOrDefault(x => x.SourceJournalId == source)
                        ?? throw new QuoteOperationException(404, "bordereau-member-not-found");
                    edit!(target, now);
                }
                next.MembersHash = Hash(members);
                if (action == "validate")
                {
                    var included = members.Where(x => x.ExcludedAt is null).Select(ExportRow).ToArray();
                    var issues = FinanceBordereauValidation.Check(included);
                    next.ValidationJson = JsonSerializer.Serialize(issues, Json);
                    next.State = issues.Count == 0 ? "valid" : "invalid";
                    if (issues.Count == 0)
                    {
                        next.ContentBytes = BordereauCsv.Write(included);
                        next.ContentHash = SHA256.HashData(next.ContentBytes);
                    }
                }
                db.Add(next); db.AddRange(members);
                await db.SaveChangesAsync(ct);
                batch.CurrentVersionId = next.Id;
                await db.SaveChangesAsync(ct);
                return Outcome(batchId, next);
            }, token, IsolationLevel.Serializable);
    }

    public async Task<BordereauPage> ListAsync(ActorContext actor, Guid providerId, int page = 1,
        int pageSize = 50, CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new QuoteOperationException(400, "bordereau-query-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await Authorize(db, actor, providerId, token);
        var query = db.Set<FinanceBordereauBatch>().AsNoTracking().Where(x => x.ProviderId == providerId);
        var total = await query.CountAsync(token);
        var items = await (from batch in query
            join version in db.Set<FinanceBordereauVersion>().AsNoTracking() on batch.CurrentVersionId equals version.Id
            orderby batch.CreatedAt descending, batch.Id
            select new BordereauBatchItem(batch.Id, batch.ProviderId, batch.AccountingPeriodId,
                version.Id, version.Number, version.State, batch.CreatedAt))
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        await tx.CommitAsync(token);
        return new BordereauPage(page, pageSize, total, items);
    }

    public async Task<BordereauVersionView> DetailAsync(ActorContext actor, Guid batchId,
        Guid? versionId = null, CancellationToken token = default)
    {
        var (batch, version, members) = await Read(actor, batchId, versionId, token);
        return new BordereauVersionView(version.Id, batch.Id, batch.ProviderId, batch.AccountingPeriodId,
            version.Number, version.ParentVersionId, version.SourceCutoff, Convert.ToHexString(version.SourceHash),
            Convert.ToHexString(version.MembersHash), version.SchemaVersion, version.State,
            JsonSerializer.Deserialize<BordereauValidationIssue[]>(version.ValidationJson, Json) ?? [],
            version.ContentHash is null ? null : Convert.ToHexString(version.ContentHash),
            members.Select(View).ToArray(), version.CreatedAt);
    }

    public async Task<BordereauDownload> DownloadAsync(ActorContext actor, Guid batchId,
        Guid versionId, CancellationToken token = default)
    {
        var (batch, version, members) = await Read(actor, batchId, versionId, token);
        await using var db = await factory.CreateDbContextAsync(token);
        var submitted = await db.Set<FinanceBordereauSubmission>().AsNoTracking().AnyAsync(x => x.BatchId == batchId &&
            x.VersionId == versionId && x.State == "submitted" && x.ContentHash == version.ContentHash, token);
        if ((batch.CurrentVersionId != version.Id && !submitted) || version.State != "valid" || version.ContentBytes is null || version.ContentHash is null ||
            (JsonSerializer.Deserialize<BordereauValidationIssue[]>(version.ValidationJson, Json)?.Length ?? -1) != 0)
            throw new QuoteOperationException(409, "bordereau-not-valid");
        if (!CryptographicOperations.FixedTimeEquals(Hash(members), version.MembersHash) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(version.ContentBytes), version.ContentHash) ||
            !version.ContentBytes.AsSpan().SequenceEqual(BordereauCsv.Write(members.Where(x => x.ExcludedAt is null).Select(ExportRow).ToArray())))
            throw new InvalidOperationException("Bordereau saved evidence hash mismatch.");
        return new BordereauDownload(version.ContentBytes, Convert.ToHexString(version.ContentHash),
            $"bordereau-{batch.ProviderId:N}-{batch.AccountingPeriodId:N}-v{version.Number}.csv");
    }

    private async Task<(FinanceBordereauBatch Batch, FinanceBordereauVersion Version, FinanceBordereauMember[] Members)>
        Read(ActorContext actor, Guid batchId, Guid? versionId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        // No detail, hash, or byte read precedes the current permission check.
        var providerId = await db.Set<FinanceBordereauBatch>().AsNoTracking().Where(x => x.Id == batchId)
            .Select(x => (Guid?)x.ProviderId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "bordereau-not-found");
        await Authorize(db, actor, providerId, token);
        var batch = await db.Set<FinanceBordereauBatch>().AsNoTracking().SingleAsync(x => x.Id == batchId, token);
        var id = versionId ?? batch.CurrentVersionId;
        var version = await db.Set<FinanceBordereauVersion>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.BatchId == batchId, token)
            ?? throw new QuoteOperationException(404, "bordereau-version-not-found");
        var members = await db.Set<FinanceBordereauMember>().AsNoTracking().Where(x => x.VersionId == id)
            .OrderBy(x => x.SourceJournalId).ToArrayAsync(token);
        await tx.CommitAsync(token);
        return (batch, version, members);
    }

    internal static async Task Authorize(BackOfficeDbContext db, ActorContext actor, Guid providerId,
        CancellationToken token, bool write = false)
    {
        if (!actor.HasCapability("finance-bordereau")) throw new QuoteOperationException(403, "bordereau-scope-denied");
        var provider = write
            ? db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(UPDLOCK,HOLDLOCK) WHERE Id={providerId}")
            : db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={providerId}");
        if (!await provider.AnyAsync(token)) throw new QuoteOperationException(404, "bordereau-provider-not-found");
        var identity = await IdentitySnapshot.Lock(db, new IdentityReference(actor.UserId, null), token);
        if (identity is null || identity.User.AgencyId is not null || !actor.Roles.SetEquals(identity.Roles.Select(x => x.Code)) ||
            !new ActorContext(identity.User.Id, identity.User.TeamId, null,
                identity.Roles.Select(x => x.Code).ToHashSet(StringComparer.Ordinal)).HasCapability("finance-bordereau"))
            throw new QuoteOperationException(403, "bordereau-scope-denied");
    }

    private static FinanceBordereauMember Copy(FinanceBordereauMember x, Guid versionId, DateTimeOffset now, Guid actorId)
        => new() { VersionId = versionId, SourceJournalId = x.SourceJournalId, PolicyId = x.PolicyId,
            TransactionId = x.TransactionId, AgencyId = x.AgencyId, ProductVersionId = x.ProductVersionId,
            AgencyTermsVersionId = x.AgencyTermsVersionId, PostingDate = x.PostingDate, PostedAt = x.PostedAt,
            Currency = x.Currency, Premium = x.Premium, Tax = x.Tax, Fee = x.Fee,
            Commission = x.Commission, NetDue = x.NetDue, PolicyReference = x.PolicyReference,
            ProviderProductCode = x.ProviderProductCode, AgencyReference = x.AgencyReference,
            CorrectionActorId = x.CorrectionActorId, CorrectionReason = x.CorrectionReason, CorrectedAt = x.CorrectedAt,
            ExclusionActorId = x.ExclusionActorId, ExclusionReason = x.ExclusionReason, ExcludedAt = x.ExcludedAt,
            CreatedAt = now, CreatedBy = actorId };
    private static BordereauMemberView View(FinanceBordereauMember x)
        => new(x.SourceJournalId, x.PolicyId, x.TransactionId, x.AgencyId, x.ProductVersionId,
            x.AgencyTermsVersionId, x.PostingDate, x.PostedAt, FinanceLedgerMath.Money(x.Premium),
            FinanceLedgerMath.Money(x.Tax), FinanceLedgerMath.Money(x.Fee), FinanceLedgerMath.Money(x.Commission),
            FinanceLedgerMath.Money(x.NetDue), x.Currency, x.PolicyReference, x.ProviderProductCode,
            x.AgencyReference, x.CorrectionActorId, x.CorrectionReason, x.CorrectedAt,
            x.ExclusionActorId, x.ExclusionReason, x.ExcludedAt);
    private static BordereauExportRow ExportRow(FinanceBordereauMember x)
        => new(x.SourceJournalId, x.PolicyReference, x.ProviderProductCode, x.AgencyReference,
            FinanceLedgerMath.Money(x.Premium), FinanceLedgerMath.Money(x.Tax), FinanceLedgerMath.Money(x.Fee),
            FinanceLedgerMath.Money(x.Commission), FinanceLedgerMath.Money(x.NetDue));
    private static byte[] Hash(IEnumerable<FinanceBordereauMember> members)
        => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(members.OrderBy(x => x.SourceJournalId).Select(View), Json));
    private static bool Reason(string reason) => reason is { Length: >= 10 and <= 1000 } && !string.IsNullOrWhiteSpace(reason);
    private static CommandOutcome Outcome(Guid batchId, FinanceBordereauVersion version)
        => new(batchId, 201, JsonSerializer.Serialize(new { batchId, versionId = version.Id, version = version.Number,
            state = version.State }, Json), false, '"' + version.Id.ToString("N") + '"');
}
