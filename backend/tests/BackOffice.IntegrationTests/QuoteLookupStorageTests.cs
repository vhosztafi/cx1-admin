using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class QuoteStorageTests
{
    [Fact]
    public async Task RealSqlQuoteLookupStoragePreservesOwnershipAndImmutableSelections()
    {
        await WithDatabase(async (db, _) =>
        {
            var fixture = await CreateFixture(db); var quote = await InsertQuote(db, fixture);
            var source = Revision(quote, fixture); source.ProposalJson = "{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\",\"insured\":{\"address\":{\"postcode\":\"AB1 2CD\"}}}";
            var next = Revision(quote, fixture, 2); db.AddRange(source, next); await db.SaveChangesAsync();
            var setting = new SettingVersion { Scope = "quote-lookup/success", Version = 1, Values = "{}", EffectiveFrom = DateTimeOffset.UtcNow };
            db.Add(setting); await db.SaveChangesAsync();
            async Task<QuoteLookup> NewLookup()
            {
                var lookup = new QuoteLookup { QuoteId = quote.Id, RevisionId = source.Id, Kind = "address", TargetScope = "insured", Query = "AB12CD",
                    InputFingerprint = new string('a', 64), RequestHash = RandomNumberGenerator.GetBytes(32), ReferenceVersion = "test-1",
                    ScenarioVersionId = setting.Id, Scenario = "success", CreatedBy = fixture.Actor };
                var work = new OutboxWork { Kind = "quote-lookup", SubjectRecordId = lookup.Id, OperationKey = Guid.NewGuid().ToString(),
                    ScenarioVersionId = setting.Id, NextAttemptAt = DateTimeOffset.UtcNow };
                db.Add(work); await db.SaveChangesAsync(); lookup.WorkId = work.Id; return lookup;
            }
            var lookup = await NewLookup(); db.Add(lookup); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteLookup SET Query=N'CHANGED' WHERE Id={lookup.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM QuoteLookup WHERE Id={lookup.Id}"));
            var invalid = await NewLookup(); invalid.RiskItemId = Guid.NewGuid(); invalid.TargetScope = "driver";
            db.Add(invalid); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var foreign = await InsertQuote(db, fixture); var foreignRevision = Revision(foreign, fixture); db.Add(foreignRevision); await db.SaveChangesAsync();
            QuoteLookupSelection Selection() => new() { LookupId = lookup.Id, QuoteId = quote.Id, SourceRevisionId = source.Id,
                InputFingerprint = lookup.InputFingerprint, NewRevisionId = next.Id, ManualReason = "Fictional manual recovery", ActorId = fixture.Actor, CreatedBy = fixture.Actor };
            var wrong = Selection(); wrong.NewRevisionId = foreignRevision.Id; db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            wrong = Selection(); wrong.InputFingerprint = new string('b', 64); db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            wrong = Selection(); wrong.CandidateId = Guid.NewGuid(); wrong.ManualReason = null; db.Add(wrong);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var selected = Selection(); db.Add(selected); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteLookupSelection SET ManualReason=N'Changed' WHERE Id={selected.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM QuoteLookupSelection WHERE Id={selected.Id}"));
            db.Add(Selection()); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            Assert.Equal(1, await db.Set<QuoteLookup>().CountAsync()); Assert.Equal(1, await db.Set<QuoteLookupSelection>().CountAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteLookup SET State=N'no-match',ResultJson=N'{{}}',CompletedAt={DateTimeOffset.UtcNow} WHERE Id={lookup.Id}");
            Assert.Equal("no-match", (await db.Set<QuoteLookup>().AsNoTracking().SingleAsync()).State);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteLookup SET State=N'failed' WHERE Id={lookup.Id}"));
        });
    }
}
