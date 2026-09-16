using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlLookupRequestsDeduplicateRollbackAndReauthorizeReplay()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'active' WHERE Id={fixture.Agency}");
            await using (var transaction = await db.Database.BeginTransactionAsync())
            { await QuoteCaptureDemoSeed.SeedAsync(db); await transaction.CommitAsync(); }
            var clock = new QuoteTime();
            db.Add(new SettingVersion { Scope = "quote-lookup/success", Version = 1, EffectiveFrom = clock.GetUtcNow(),
                Values = "{\"demo\":true,\"kind\":\"quote-lookup\",\"scenario\":\"success\"}" });
            await db.SaveChangesAsync();
            var user = await db.Set<StaffUser>().Where(x => x.Email == "underwriter@cover.example").Select(x => x.Id).SingleAsync();
            var actor = new ActorContext(user, null, null, new HashSet<string> { "underwriter" });
            var options = new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), sql => sql.UseCompatibilityLevel(160)).Options;
            var factory = new QuoteFactory(options); var quotes = new QuoteService(factory, clock); var lookups = new QuoteLookupService(factory, clock);
            const string proposal = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"insured\":{\"address\":{\"postcode\":\"AB1 2CD\"}}}";
            var created = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "lookup-create", Guid.NewGuid());
            var saved = await quotes.GetAsync(actor, created.ResourceId); var target = new QuoteLookupTarget("address", "insured");
            var results = await Task.WhenAll(new[] { "lookup-a", "lookup-b" }.Select(key => lookups.RequestAsync(actor, created.ResourceId,
                saved.Quote.RowVersion, saved.Revision.Id, target, "success", key, Guid.NewGuid())));
            Assert.Equal(results[0].ResourceId, results[1].ResourceId);
            Assert.Equal(1, await db.Set<QuoteLookup>().CountAsync());
            Assert.Equal(1, await db.Set<OutboxWork>().CountAsync(x => x.Kind == QuoteLookupService.WorkKind));
            var lookup = await lookups.GetAsync(actor, created.ResourceId, results[0].ResourceId);
            Assert.Equal("pending", lookup.State); Assert.Null(lookup.ResultJson); Assert.Equal("AB12CD", lookup.Query);
            var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == lookup.WorkId);
            Assert.DoesNotContain("AB12CD", work.Payload); Assert.Contains(lookup.Id.ToString(), work.Payload);
            Assert.DoesNotContain("AB12CD", results[0].Body);
            await quotes.SaveAsync(actor, created.ResourceId, saved.Quote.RowVersion, proposal.Replace("AB1 2CD", "CD1 2EF"), null, "lookup-edit", Guid.NewGuid());
            var replay = await lookups.RequestAsync(actor, created.ResourceId, saved.Quote.RowVersion, saved.Revision.Id, target, "success", "lookup-a", Guid.NewGuid());
            Assert.True(replay.Replayed); Assert.Equal(lookup.Id, replay.ResourceId);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => lookups.RequestAsync(actor, created.ResourceId,
                saved.Quote.RowVersion, saved.Revision.Id, target, "success", "lookup-stale", Guid.NewGuid()))).Status);
            var other = await quotes.CreateAsync(actor, fixture.Relationship, fixture.ProductVersion, proposal, "lookup-other", Guid.NewGuid());
            var otherSaved = await quotes.GetAsync(actor, other.ResourceId);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => lookups.GetAsync(actor, other.ResourceId, lookup.Id))).Status);
            var receipts = await db.Set<IdempotencyRecord>().CountAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_LookupRequestTestFail ON QuoteActivity AFTER INSERT AS BEGIN SET NOCOUNT ON; THROW 51077, 'Injected lookup failure.', 1; END;");
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => lookups.RequestAsync(actor, other.ResourceId, otherSaved.Quote.RowVersion,
                    otherSaved.Revision.Id, target, "success", "lookup-rollback", Guid.NewGuid()));
                Assert.Equal(1, await db.Set<QuoteLookup>().CountAsync());
                Assert.Equal(1, await db.Set<OutboxWork>().CountAsync(x => x.Kind == QuoteLookupService.WorkKind));
                Assert.Equal(receipts, await db.Set<IdempotencyRecord>().CountAsync());
            }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_LookupRequestTestFail"); }
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency SET State=N'suspended' WHERE Id={fixture.Agency}");
            await Assert.ThrowsAsync<QuoteOperationException>(() => lookups.RequestAsync(actor, created.ResourceId, saved.Quote.RowVersion,
                saved.Revision.Id, target, "success", "lookup-a", Guid.NewGuid()));
        });
    }
}
