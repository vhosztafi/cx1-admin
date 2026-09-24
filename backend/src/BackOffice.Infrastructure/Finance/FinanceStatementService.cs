using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Finance;

public sealed record FinanceStatementSource(string SourceKey, string SourceKind, Guid? PolicyId, Guid? TransactionId,
    Guid? AgencyTermsVersionId, DateOnly PostingDate, DateTimeOffset PostedAt, DateTimeOffset EffectiveAt,
    string Delta, string? DueDate, int? AgeDaysAtEnd, bool PastDueAtEnd);
public sealed record FinanceStatementView(Guid Id, Guid AgencyId, int Version, DateOnly From, DateOnly To,
    DateTimeOffset SourceCutoff, string Opening, string Debits, string Credits, string Closing,
    IReadOnlyList<StatementLine> Rows, IReadOnlyList<FinanceStatementSource> Sources,
    string SourceHash, string ContentHash, DateTimeOffset CreatedAt);
public sealed record FinanceStatementListItem(Guid Id, int Version, DateOnly From, DateOnly To,
    DateTimeOffset SourceCutoff, string Opening, string Debits, string Credits, string Closing, DateTimeOffset CreatedAt);
public sealed record FinanceStatementPage(int Page, int PageSize, int Total, IReadOnlyList<FinanceStatementListItem> Items);
public sealed record FinanceStatementDownload(byte[] Bytes, string ContentHash, string FileName);

public sealed class FinanceStatementService(IDbContextFactory<BackOfficeDbContext> factory,
    SqlCommandBoundary commands, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<CommandOutcome> GenerateAsync(ActorContext actor, Guid agencyId, DateOnly from, DateOnly to,
        string key, Guid correlationId, CancellationToken token = default)
    {
        if (agencyId == Guid.Empty || from == default || to == default || from >= to || to.DayNumber - from.DayNumber > 3660)
            throw new QuoteOperationException(400, "statement-window-invalid");
        return commands.ExecuteAuthorizedAsync(new CommandIdentity(actor.UserId,
                $"/api/v1/finance/agencies/{agencyId:N}/statements", key, correlationId),
            new { agencyId, from, to }, "finance.statement.generated",
            async (db, ct) =>
            {
                await FinanceLedgerService.Authorize(db, actor, agencyId, null, ct, forWrite: true);
                if (!actor.HasCapability("statement-generate"))
                    throw new QuoteOperationException(403, "statement-scope-denied");
            },
            async (db, ct) =>
            {
                // Serializable range reads and the held agency lock fix one source cutoff,
                // including all prior movements that determine opening.
                var now = time.GetUtcNow();
                var entries = await FinanceLedgerService.Load(db, agencyId, null, null, ct, now);
                var sources = entries.Where(x => x.View.DebtorKind == "agency" && x.View.PostingDate < to)
                    .OrderBy(x => x.View.PostingDate).ThenBy(x => x.View.PostedAt)
                    .ThenBy(x => x.View.SourceKey, StringComparer.Ordinal)
                    .Select(x =>
                    {
                        var due = x.View.DueDate is null ? (DateOnly?)null : DateOnly.ParseExact(x.View.DueDate, "yyyy-MM-dd");
                        var age = due is null ? (int?)null : to.AddDays(-1).DayNumber - due.Value.DayNumber;
                        return new FinanceStatementSource(x.View.SourceKey, x.View.SourceKind, x.View.PolicyId,
                            x.View.TransactionId, x.View.AgencyTermsVersionId, x.View.PostingDate, x.View.PostedAt,
                            x.View.EffectiveAt, x.View.DebtorDelta, x.View.DueDate, age,
                            age is > 0 && x.Debtor > 0);
                    }).ToArray();
                var reconciled = FinanceStatementMath.Reconcile(sources.Select(x => new StatementMovement(
                    x.SourceKey, x.PostingDate, FinanceLedgerMath.Pence(decimal.Parse(x.Delta,
                        System.Globalization.CultureInfo.InvariantCulture)),
                    x.DueDate is null ? null : DateOnly.ParseExact(x.DueDate, "yyyy-MM-dd"))), from, to);
                var id = Guid.NewGuid();
                var version = 1 + (await db.Set<FinanceStatementVersion>()
                    .Where(x => x.AgencyId == agencyId && x.From == from && x.To == to)
                    .MaxAsync(x => (int?)x.Version, ct) ?? 0);
                var sourceBytes = JsonSerializer.SerializeToUtf8Bytes(sources, Json);
                var sourceHash = SHA256.HashData(sourceBytes);
                var termsIds = sources.Select(x => x.AgencyTermsVersionId).Where(x => x.HasValue).Distinct().ToArray();
                var view = new FinanceStatementView(id, agencyId, version, from, to, now,
                    reconciled.Opening, reconciled.Debits, reconciled.Credits, reconciled.Closing,
                    reconciled.Rows, sources, Convert.ToHexString(sourceHash), "", now);
                var content = Csv(view);
                var contentHash = SHA256.HashData(content);
                view = view with { ContentHash = Convert.ToHexString(contentHash) };
                db.Add(new FinanceStatementVersion
                {
                    Id = id, AgencyId = agencyId, Version = version, From = from, To = to,
                    SourceCutoff = now, AgencyTermsVersionId = termsIds.Length == 1 ? termsIds[0] : null,
                    SourceIdsJson = JsonSerializer.Serialize(sources.Select(x => x.SourceKey), Json),
                    SourceHash = sourceHash, SnapshotJson = JsonSerializer.Serialize(view, Json),
                    Opening = view.Opening, Debits = view.Debits, Credits = view.Credits, Closing = view.Closing,
                    ContentBytes = content, ContentHash = contentHash, CreatedAt = now, CreatedBy = actor.UserId
                });
                await db.SaveChangesAsync(ct);
                // Durable command receipts contain only safe identifiers, never
                // snapshot rows or file bytes.
                return new CommandOutcome(id, 201, JsonSerializer.Serialize(new { id, version }, Json));
            }, token, IsolationLevel.Serializable);
    }

    public async Task<FinanceStatementPage> ListAsync(ActorContext actor, Guid agencyId,
        int page = 1, int pageSize = 50, CancellationToken token = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new QuoteOperationException(400, "statement-query-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        await AuthorizeRead(db, actor, agencyId, token);
        var query = db.Set<FinanceStatementVersion>().AsNoTracking().Where(x => x.AgencyId == agencyId);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Version).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FinanceStatementListItem(x.Id, x.Version, x.From, x.To, x.SourceCutoff,
                x.Opening, x.Debits, x.Credits, x.Closing, x.CreatedAt)).ToArrayAsync(token);
        await tx.CommitAsync(token);
        return new FinanceStatementPage(page, pageSize, total, items);
    }

    public async Task<FinanceStatementView> DetailAsync(ActorContext actor, Guid id, CancellationToken token = default)
    {
        var row = await ReadAuthorized(actor, id, token);
        return JsonSerializer.Deserialize<FinanceStatementView>(row.SnapshotJson, Json)
            ?? throw new InvalidOperationException("Statement snapshot cannot be read.");
    }

    public async Task<FinanceStatementDownload> DownloadAsync(ActorContext actor, Guid id, CancellationToken token = default)
    {
        var row = await ReadAuthorized(actor, id, token);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(row.ContentBytes), row.ContentHash))
            throw new InvalidOperationException("Statement content hash mismatch.");
        return new FinanceStatementDownload(row.ContentBytes, Convert.ToHexString(row.ContentHash),
            $"agency-statement-{row.AgencyId:N}-{row.From:yyyyMMdd}-{row.To:yyyyMMdd}-v{row.Version}.csv");
    }

    private async Task<FinanceStatementVersion> ReadAuthorized(ActorContext actor, Guid id, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        Guid agencyId;
        if (actor.AgencyId is Guid ownAgency)
        {
            await AuthorizeRead(db, actor, ownAgency, token);
            agencyId = ownAgency;
        }
        else
        {
            agencyId = await db.Set<FinanceStatementVersion>().AsNoTracking().Where(x => x.Id == id)
                .Select(x => (Guid?)x.AgencyId).SingleOrDefaultAsync(token)
                ?? throw new QuoteOperationException(404, "statement-not-found");
            await AuthorizeRead(db, actor, agencyId, token);
        }
        var row = await db.Set<FinanceStatementVersion>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.AgencyId == agencyId, token)
            ?? throw new QuoteOperationException(404, "statement-not-found");
        await tx.CommitAsync(token);
        return row;
    }

    private static byte[] Csv(FinanceStatementView view)
    {
        var text = new StringBuilder();
        text.AppendLine("statement_id,agency_id,version,from,to,source_cutoff,source_hash,opening,debits,credits,closing");
        text.Append(view.Id).Append(',').Append(view.AgencyId).Append(',').Append(view.Version).Append(',')
            .Append(view.From.ToString("yyyy-MM-dd")).Append(',').Append(view.To.ToString("yyyy-MM-dd")).Append(',')
            .Append(view.SourceCutoff.ToString("O")).Append(',').Append(view.SourceHash).Append(',')
            .Append(view.Opening).Append(',').Append(view.Debits).Append(',').Append(view.Credits).Append(',')
            .AppendLine(view.Closing);
        text.AppendLine("source_key,posting_date,due_date,debit,credit,running_balance,overdue");
        foreach (var row in view.Rows)
            text.Append(row.SourceKey).Append(',').Append(row.PostingDate.ToString("yyyy-MM-dd")).Append(',')
                .Append(row.DueDate).Append(',').Append(row.Debit).Append(',').Append(row.Credit).Append(',')
                .Append(row.RunningBalance).Append(',').AppendLine(row.Overdue ? "true" : "false");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private async Task AuthorizeRead(BackOfficeDbContext db, ActorContext actor, Guid agencyId, CancellationToken token)
    {
        if (actor.AgencyId is null)
        {
            await FinanceLedgerService.Authorize(db, actor, agencyId, null, token);
            return;
        }
        try { await AgencyScope.Resolve(db, actor, agencyId, "agency-sharing-read", token); }
        catch (AgencyCommandException) { throw new QuoteOperationException(403, "statement-scope-denied"); }
        var now = time.GetUtcNow();
        var grant = await db.Set<AgencyPermissionGrant>()
            .FromSqlInterpolated($"SELECT * FROM AgencyPermissionGrant WITH(HOLDLOCK) WHERE AgencyId={agencyId} AND Permission={AgencyPermissionRules.StatementDownload} AND GrantedAt<={now} AND RevokedAt IS NULL")
            .AsNoTracking().AnyAsync(token);
        if (!grant) throw new QuoteOperationException(403, "statement-scope-denied");
    }
}
