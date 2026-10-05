using System.Data;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Reporting;

public sealed partial class ReportService
{
    public async Task<object> AgencySummaryAsync(ActorContext actor, Guid agencyId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        actor = await ReportingScope.Current(db, actor, token);
        var definition = ReportCatalogue.Authorize(actor, ReportCatalogue.All.Single(x => x.Code == "underwriting").Id);
        if (!await db.Set<Agency>().AnyAsync(x => x.Id == agencyId, token)) throw new QuoteOperationException(404, "agency-not-found");
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Europe/London").DateTime);
        var currentFilters = new ReportFilters(today.AddDays(-89), today, AgencyId: agencyId);
        var previousFilters = new ReportFilters(today.AddDays(-179), today.AddDays(-90), AgencyId: agencyId);
        var current = Result(definition, currentFilters, now, await Load(db, actor, definition, currentFilters, now, token), false);
        var previous = Result(definition, previousFilters, now, await Load(db, actor, definition, previousFilters, now, token), false);
        await tx.CommitAsync(token);
        return new { agencyId, asOf = now, from = currentFilters.From, to = currentFilters.To, current = current.Measures, previous = previous.Measures,
            definition = "Quote-created cohorts over consecutive 90 London calendar days, using current saved outcomes and pinned first-issue premium. No report run or favourite is created." };
    }
}
