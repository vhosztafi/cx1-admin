using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCommercialExposureLimitPublicationsRejectMalformedDistrictsAndWrongScope()
    {
        await WithDatabase(async (db, password) =>
        {
            await DemoDatabase.SeedAsync(db, password, includeQuoteCapture: true, includeUnderwriting: true,
                includeCommercialCapture: true, includeCommercialUnderwriting: true);
            var original = await db.Set<CommercialExposureLimitVersion>().AsNoTracking().SingleAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            async Task Reject(string district, Guid? parent)
            {
                var row = new CommercialExposureLimitVersion { BookId = original.BookId, District = district, Version = 2,
                    Amount = 50_000_000m, EffectiveFrom = original.EffectiveFrom, EffectiveTo = original.EffectiveTo,
                    PublishedAt = original.PublishedAt, CreatedBy = original.CreatedBy, SupersedesLimitId = parent };
                CommercialExposureSeed.SetPublication(row, "Invalid test publication"); db.Add(row);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            }
            foreach (var district in new[] { "* ", "s9", "S9 ", "ZZ9", "*S9", "" }) await Reject(district, null);
            await Reject("S9", original.Id); // A district publication cannot replace a default scope.
            await Reject("*", Guid.NewGuid());
            var book = await db.Set<CommercialExposureBook>().AsNoTracking().SingleAsync();
            var motor = await db.Set<Product>().FirstAsync(x => x.Code != "commercial-combined");
            db.Add(new CommercialExposureBook { Code = "wrong-product", ProductId = motor.Id, ProviderId = book.ProviderId, CreatedBy = original.CreatedBy });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => CommercialExposureSeed.SeedAsync(db));
            await using (var held = await db.Database.BeginTransactionAsync())
            { await CommercialExposureSeed.SeedAsync(db); await held.CommitAsync(); }
            Assert.Equal(original.PublicationJson, (await db.Set<CommercialExposureLimitVersion>().AsNoTracking().SingleAsync()).PublicationJson);
        });
    }

    [Fact]
    public void CommercialExposureStorageSelectionMatchesPolicySelectorAcrossEffectiveAndKnownBoundaries()
    {
        var book = Guid.NewGuid(); var policy = Guid.NewGuid(); var term = Guid.NewGuid(); var renewalTerm = Guid.NewGuid();
        var start = DateTimeOffset.Parse("2027-01-01T00:00:00Z"); var known = start.AddDays(-10);
        CommercialExposureSlice Row(Guid termId, DateTimeOffset from, DateTimeOffset at, DateTimeOffset processed, int sequence, int ordinal, string kind, decimal amount) =>
            new(book, policy, termId, Guid.NewGuid(), from, from.AddYears(1), at, processed, sequence, ordinal, kind,
                kind == "cancellation" ? [] : [new(Guid.NewGuid(), "S9", amount)]);
        var rows = new[] {
            Row(term,start,start,known,1,1,"new-business",100),
            Row(term,start,start.AddMonths(3),known.AddDays(1),2,1,"adjustment",200),
            Row(term,start,start.AddMonths(3),known.AddDays(2),3,1,"adjustment",300),
            Row(term,start,start.AddMonths(3),known.AddDays(2),3,2,"adjustment",400),
            Row(term,start,start.AddMonths(6),known.AddDays(3),4,1,"cancellation",0),
            Row(renewalTerm,start.AddYears(1),start.AddYears(1),known.AddDays(4),1,1,"renewal",500)
        };
        var candidates = rows.Select(x => new PolicyTemporalCandidate(x.PolicyId,x.TermId,x.VersionId,x.TermStartsAt,x.TermEndsAt,
            x.EffectiveAt,x.ProcessedAt,x.TransactionSequence,x.SliceOrdinal,x.TransactionKind)).ToArray();
        var effectiveCuts = rows.SelectMany(x => new[] { x.TermStartsAt, x.TermEndsAt, x.EffectiveAt }).Distinct().SelectMany(x => new[] { x.AddTicks(-1), x, x.AddTicks(1) });
        var knownCuts = rows.Select(x => x.ProcessedAt).Distinct().SelectMany(x => new[] { x.AddTicks(-1), x, x.AddTicks(1) }).ToArray();
        foreach (var effective in effectiveCuts)
            foreach (var cutoff in knownCuts)
            {
                var selected = PolicyTemporalSelector.Select(candidates, policy, effective, cutoff);
                var expected = selected?.State == "active" ? rows.Single(x => x.VersionId == selected.Candidate.VersionId).Locations.Sum(x => x.SumInsured) : 0;
                Assert.Equal(expected, CommercialExposureRules.Snapshot(rows, book, effective, cutoff).Sum(x => x.PropertySum));
            }
    }

    [Fact]
    public Task RealSqlCommercialExposureStoragePinsSourceAndAtomicallyRetainsCompleteChildren() => CommercialTermsScenario(async (db, oldCycle, acceptanceId, actor, now) =>
    {
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == oldCycle.Id);
        var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteId);
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId);
        var proposal = JsonNode.Parse(revision.ProposalJson)!;
        var policy = new Policy { SourceQuoteId = quote.Id, AgencyId = quote.AgencyId, ClientId = quote.ClientId,
            RelationshipId = quote.RelationshipId, ProductId = quote.ProductId, CreatedBy = actor, CreatedAt = now };
        db.Add(policy); await db.SaveChangesAsync();
        var term = new PolicyTerm { PolicyId = policy.Id, ProductId = policy.ProductId, ProductVersionId = cycle.ProductVersionId,
            Number = 1, StartsAt = cycle.StartsAt, EndsAt = cycle.EndsAt, LocalTermIntentJson = proposal["termIntent"]!.ToJsonString(), CreatedBy = actor };
        db.Add(term); await db.SaveChangesAsync();
        var transaction = new PolicyTransaction { PolicyId = policy.Id, TermId = term.Id, SourceQuoteId = quote.Id,
            CycleId = cycle.Id, QuoteRevisionId = revision.Id, RatingId = cycle.CurrentRatingId, AcceptanceId = acceptanceId,
            Sequence = 1, EffectiveAt = term.StartsAt, ProcessedAt = now, CreatedAt = now, CreatedBy = actor,
            Reason = "Fictional commercial exposure storage fixture", OperationKey = "exposure-fixture/" + policy.Id.ToString("N") };
        db.Add(transaction); await db.SaveChangesAsync();
        proposal.AsObject().Remove("termIntent"); proposal["snapshotFormat"] = "issued-commercial-1";
        proposal["productVersionId"] = cycle.ProductVersionId.ToString();
        proposal["insured"]!["clientId"] = quote.ClientId.ToString(); proposal["insured"]!["clientAgencyRelationshipId"] = quote.RelationshipId.ToString();
        proposal["term"] = JsonSerializer.SerializeToNode(new { kind = "annual", startsAt = term.StartsAt, endsAt = term.EndsAt, timeZone = "Europe/London" });
        var json = proposal.ToJsonString();
        var version = new PolicyVersion { PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id,
            Sequence = 1, SliceOrdinal = 1, SnapshotJson = json, ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes(json)),
            EffectiveAt = term.StartsAt, ProcessedAt = now, CreatedAt = now, CreatedBy = actor };
        db.Add(version); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using (var held = await db.Database.BeginTransactionAsync())
        { await CommercialExposureSeed.SeedAsync(db); await held.CommitAsync(); }
        var mapping = await db.Set<CommercialExposureBinder>().AsNoTracking().SingleAsync(x => x.BinderVersionId == cycle.BinderVersionId);
        var limit = await db.Set<CommercialExposureLimitVersion>().AsNoTracking().SingleAsync(x => x.BookId == mapping.BookId);
        Assert.Equal(40_000_000m, limit.Amount); Assert.False(db.Database.HasPendingModelChanges());
        CommercialExposureVersion Header() => new() { BookId = mapping.BookId, BinderVersionId = mapping.BinderVersionId,
            PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id, VersionId = version.Id,
            SourceHash = version.ContentHash, TermStartsAt = term.StartsAt, TermEndsAt = term.EndsAt,
            EffectiveAt = version.EffectiveAt, ProcessedAt = version.ProcessedAt, TransactionKind = transaction.Kind,
            TransactionSequence = transaction.Sequence, SliceOrdinal = version.SliceOrdinal, CreatedBy = actor,
            LocationsJson = JsonSerializer.Serialize(CommercialExposureProjection.Locations(JsonDocument.Parse(json).RootElement), new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        async Task Reject(Action<CommercialExposureVersion> change)
        {
            var bad = Header(); change(bad); db.Add(bad);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            Assert.Empty(await db.Set<CommercialExposureVersion>().ToArrayAsync()); Assert.Empty(await db.Set<CommercialExposureLocationRecord>().ToArrayAsync());
        }
        await Reject(x => x.SourceHash = SHA256.HashData([1]));
        await Reject(x => x.VersionId = Guid.NewGuid());
        await Reject(x => x.PolicyId = Guid.NewGuid());
        await Reject(x => x.TermStartsAt = term.StartsAt.AddDays(-1));
        await Reject(x => x.TransactionSequence++);
        await Reject(x => x.SliceOrdinal++);
        await Reject(x => x.ProcessedAt = now.AddSeconds(-1));
        await Reject(x => { x.TransactionKind = "cancellation"; x.LocationsJson = "[]"; });
        await Reject(x => x.LocationsJson = "[]");
        await Reject(x => { var rows = JsonNode.Parse(x.LocationsJson)!.AsArray(); rows[0]!["sumInsured"] = 0; x.LocationsJson = rows.ToJsonString(); });
        await Reject(x => { var rows = JsonNode.Parse(x.LocationsJson)!.AsArray(); rows[0]!["district"] = "S1"; x.LocationsJson = rows.ToJsonString(); });
        await Reject(x => { var rows = JsonNode.Parse(x.LocationsJson)!.AsArray(); rows[0]!["riskItemId"] = Guid.NewGuid(); x.LocationsJson = rows.ToJsonString(); });
        await Reject(x => { var rows = JsonNode.Parse(x.LocationsJson)!.AsArray(); rows.Add(rows[0]!.DeepClone()); x.LocationsJson = rows.ToJsonString(); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => CommercialExposureProjection.AppendAsync(db, version.Id, mapping.BinderVersionId, actor));
        await using (var held = await db.Database.BeginTransactionAsync())
        { await CommercialExposureProjection.AppendAsync(db, version.Id, mapping.BinderVersionId, actor); await held.RollbackAsync(); }
        db.ChangeTracker.Clear(); Assert.Empty(await db.Set<CommercialExposureVersion>().ToArrayAsync());
        CommercialExposureVersion header;
        await using (var held = await db.Database.BeginTransactionAsync())
        { header = await CommercialExposureProjection.AppendAsync(db, version.Id, mapping.BinderVersionId, actor); await held.CommitAsync(); }
        db.ChangeTracker.Clear();
        var children = await db.Set<CommercialExposureLocationRecord>().AsNoTracking().ToArrayAsync();
        Assert.Equal(proposal["risk"]!["locations"]!.AsArray().Count, children.Length);
        Assert.Empty(await CommercialExposureProjection.ReadAsync(db, mapping.BookId, now.AddTicks(-1)));
        var slices = await CommercialExposureProjection.ReadAsync(db, mapping.BookId, now);
        Assert.Equal(children.Sum(x => x.SumInsured), CommercialExposureRules.Snapshot(slices, mapping.BookId, term.StartsAt, now).Sum(x => x.PropertySum));
        Assert.All(CommercialExposureRules.Snapshot(slices, mapping.BookId, term.StartsAt, now), x => Assert.Equal(1, x.PolicyCount));
        Assert.Empty(CommercialExposureRules.Snapshot(slices, mapping.BookId, term.EndsAt, now));
        db.Add(Header()); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.Add(new CommercialExposureLocationRecord { ExposureVersionId = header.Id, RiskItemId = children[0].RiskItemId, District = children[0].District, SumInsured = children[0].SumInsured });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.Add(new CommercialExposureLocationRecord { ExposureVersionId = Guid.NewGuid(), RiskItemId = Guid.NewGuid(), District = "S9", SumInsured = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.Add(new CommercialExposureLocationRecord { ExposureVersionId = header.Id, RiskItemId = Guid.NewGuid(), District = "S9", SumInsured = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CommercialExposureLocation SET SumInsured=0 WHERE ExposureVersionId={header.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE CommercialExposureLocation WHERE ExposureVersionId={header.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CommercialExposureVersion SET ProcessedAt={now.AddDays(1)} WHERE Id={header.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE CommercialExposureVersion WHERE Id={header.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CommercialExposureLimitVersion SET Amount=50000000 WHERE Id={limit.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CommercialExposureBinder SET BookId={Guid.NewGuid()} WHERE Id={mapping.Id}"));
        var firstBinder = await db.Set<BinderVersion>().AsNoTracking().SingleAsync(x => x.Id == mapping.BinderVersionId);
        var definition = JsonNode.Parse(firstBinder.DefinitionJson)!; definition["version"] = "commercial-demo-binder-2";
        definition["effectiveFrom"] = firstBinder.EffectiveTo; definition["effectiveTo"] = firstBinder.EffectiveTo.AddYears(1);
        var secondBinder = new BinderVersion { ProductId = firstBinder.ProductId, ProviderId = firstBinder.ProviderId, Version = "commercial-demo-binder-2",
            EffectiveFrom = firstBinder.EffectiveTo, EffectiveTo = firstBinder.EffectiveTo.AddYears(1), DefinitionJson = definition.ToJsonString(), CreatedBy = actor };
        db.Add(secondBinder); await db.SaveChangesAsync();
        await using (var held = await db.Database.BeginTransactionAsync())
        { await CommercialExposureSeed.SeedAsync(db); await CommercialExposureSeed.SeedAsync(db); await held.CommitAsync(); }
        var secondMapping = await db.Set<CommercialExposureBinder>().SingleAsync(x => x.BinderVersionId == secondBinder.Id);
        Assert.Equal(mapping.BookId, secondMapping.BookId);
        Assert.Equal(limit.ContentHash, (await db.Set<CommercialExposureLimitVersion>().AsNoTracking().SingleAsync()).ContentHash);
        Assert.Single(await CommercialExposureProjection.ReadAsync(db, secondMapping.BookId, now));
        Assert.Equal(version.SnapshotJson, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == version.Id)).SnapshotJson);
        var replacement = new CommercialExposureLimitVersion { BookId = mapping.BookId, Version = 2, Amount = 1m,
            EffectiveFrom = term.StartsAt.AddMonths(6), EffectiveTo = term.EndsAt, PublishedAt = now.AddDays(1),
            CreatedBy = actor, SupersedesLimitId = limit.Id };
        CommercialExposureSeed.SetPublication(replacement, "Fictional dated lower ceiling");
        var correctHash = replacement.ContentHash; replacement.ContentHash = SHA256.HashData([9]);
        db.Add(replacement); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        replacement.ContentHash = correctHash; replacement.Amount = 100_000_000m;
        db.Add(replacement); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        replacement.Amount = 1m; db.Add(replacement); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var oldLimits = await CommercialExposureProjection.LimitsAsync(db, mapping.BookId, now);
        Assert.Single(oldLimits);
        var newLimits = await CommercialExposureProjection.LimitsAsync(db, mapping.BookId, now.AddDays(1));
        Assert.Equal(2, newLimits.Count);
        var historic = CommercialExposureRules.Assess([], slices, oldLimits, mapping.BookId, policy.Id, term.StartsAt, term.EndsAt, now);
        Assert.True(historic.Allowed);
        var assessed = CommercialExposureRules.Assess([], slices, newLimits, mapping.BookId, policy.Id, term.StartsAt, term.EndsAt, now.AddDays(1));
        Assert.False(assessed.Allowed);
        Assert.Contains(assessed.Intervals, x => x.LimitVersionId == replacement.Id && x.Blocker == "commercial-district-capacity-exceeded");
        await using (var held = await db.Database.BeginTransactionAsync())
        { await CommercialExposureSeed.SeedAsync(db); await held.CommitAsync(); }
        Assert.Equal(2, await db.Set<CommercialExposureLimitVersion>().CountAsync());
    });
}
