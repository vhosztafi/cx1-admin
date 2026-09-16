using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlQuoteReferralBulkDecisionRollsBackAndQueryDeclineReopenRetainHistory()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password, proposal => {
                var tools = proposal["cover"]!["requestedSections"]![0]!;
                tools["selected"] = true; tools["limit"] = "10000.00"; tools["excess"] = "250.00";
            }); var service = new QuoteReferralService(f.Factory, f.Clock);
            var referral = await db.Set<QuoteReferral>().AsNoTracking().FirstAsync(x => x.CycleId == f.CycleId);
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            var condition = JsonSerializer.SerializeToElement(new { code = "provide-trading-history" });
            var query = new ReferralDecisionInput(referral.Id, referral.RowVersion, "query", "Clarify business experience", [condition], "Please supply your trading history");
            await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [query, query with { ReferralId = Guid.NewGuid() }], Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Empty(await db.Set<QuoteReferralDecision>().ToArrayAsync()); Assert.Empty(await db.Set<QuoteCondition>().ToArrayAsync());
            // The captured requested cover exceeds this actor/binder extent even
            // though the premium is low. Plain approval must not use GWP alone.
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(referral.Id, referral.RowVersion, "approve", "Low premium is not authority", [])], Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            var version = await Version(); var key = Guid.NewGuid().ToString();
            await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, version, [query], key, Guid.NewGuid());
            Assert.True((await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, version, [query], key, Guid.NewGuid())).Replayed);
            var first = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(); Assert.Equal(query.Question, first.Question);
            Assert.Equal("queried", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            foreach (var outcome in new[] { "decline", "reopen" })
            {
                referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id);
                await service.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(), [new(referral.Id, referral.RowVersion, outcome, "Recorded reason for " + outcome, [])], Guid.NewGuid().ToString(), Guid.NewGuid());
            }
            Assert.Equal(3, await db.Set<QuoteReferralDecision>().CountAsync()); Assert.Single(await db.Set<QuoteCondition>().ToArrayAsync());
            Assert.Equal("open", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            Assert.Equal("referred", (await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.QuoteId)).State);
            var original = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(x => x.Id == first.Id);
            Assert.Equal(first.ConditionsJson, original.ConditionsJson); Assert.Equal(first.Question, original.Question);
        });
    }
}
