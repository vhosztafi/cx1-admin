using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class QuoteUnderwritingReadModel
{
    public async Task<string> HistoryVersionAsync(ActorContext actor, Guid quoteId, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await AuthorizeHistory(db, actor, quoteId, token);
        var version = await QuoteDiscovery.VersionAsync(db, token);
        await transaction.CommitAsync(token); return version;
    }

    public async Task<(object[] Items, bool More)> HistoryAsync(ActorContext actor, Guid quoteId, string expectedVersion, int offset, int size, CancellationToken token)
    {
        if (offset < 0 || size is < 1 or > 100) throw new QuoteOperationException(400, "invalid-query");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await AuthorizeHistory(db, actor, quoteId, token);
        if (await QuoteDiscovery.VersionAsync(db, token) != expectedVersion) throw new QuoteOperationException(409, "quote-history-changed");
        var rows = await (from rating in db.Set<QuoteRatingResult>().AsNoTracking()
                          join cycle in db.Set<UnderwritingCycle>().AsNoTracking() on rating.CycleId equals cycle.Id
                          join revision in db.Set<QuoteRevision>().AsNoTracking() on cycle.QuoteRevisionId equals revision.Id
                          where rating.QuoteId == quoteId && cycle.QuoteId == quoteId && revision.QuoteId == quoteId
                          orderby rating.CompletedAt descending, rating.Id
                          select new { rating.Id, rating.CycleId, revisionId = revision.Id, revisionNumber = revision.Number,
                              rating.CompletedAt, rating.ExpiresAt, rating.Outcome, rating.GrossPayable }).Skip(offset).Take(size + 1).ToArrayAsync(token);
        var items = rows.Take(size).Select(x => (object)new { x.Id, x.CycleId, x.revisionId, x.revisionNumber, x.CompletedAt, x.ExpiresAt, x.Outcome, grossPayable = Money(x.GrossPayable) }).ToArray();
        await transaction.CommitAsync(token); return (items, rows.Length > size);
    }

    private static async Task AuthorizeHistory(BackOfficeDbContext db, ActorContext actor, Guid quoteId, CancellationToken token)
    {
        var owned = await QuoteScope.ForQuoteAsync(db, actor, quoteId, QuoteAccess.Read, token);
        if (!owned.Scope.Actor.HasCapability("underwriting-read")) throw new QuoteOperationException(403, "underwriting-access-denied");
    }
}
