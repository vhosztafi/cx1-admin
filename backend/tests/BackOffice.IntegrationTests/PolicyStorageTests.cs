using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private sealed record PolicyFixture(DecisionFixture Source, Policy Policy, PolicyTerm Term, PolicyTransaction Transaction, PolicyVersion Version, UnderwritingCycle Cycle);
    private static async Task<PolicyFixture> StoredPolicy(BackOfficeDbContext db, string password)
    {
        var setup = await SignedTerms(db, password); var f = setup.Fixture;
        var sent = await new QuoteTermsService(f.Factory, f.Clock).SendAsync(f.Servicing, f.QuoteId, setup.TermsId, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
        var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
        var lease = (await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
        var worker = new QuoteDeliveryWorker(f.Factory, f.Clock); Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
        var proof = await TermsProof(db, f, (await new UnderwritingEvidenceService(f.Factory, f.Clock).RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "acceptance-proof"));
        var assessment = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId);
        var prepared = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == setup.TermsId);
        var accepted = await new QuoteAcceptanceService(f.Factory, f.Clock).RecordAsync(f.Servicing, f.QuoteId, await TermsVersion(db, f),
            new(f.CycleId, setup.RatingId, setup.TermsId, prepared.TermsHash, (string)assessment["assuranceHash"], "Fictional issue customer", f.Clock.Current, "written", proof), Guid.NewGuid().ToString(), Guid.NewGuid());
        db.ChangeTracker.Clear(); var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.QuoteId);
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId); var now = f.Clock.Current;
        var policy = new Policy { SourceQuoteId = quote.Id, AgencyId = quote.AgencyId, ClientId = quote.ClientId, RelationshipId = quote.RelationshipId, ProductId = quote.ProductId, CreatedBy = f.Underwriter.UserId, CreatedAt = now };
        db.Add(policy); await db.SaveChangesAsync();
        var revision = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId);
        var snapshot = JsonNode.Parse(revision.ProposalJson)!;
        var term = new PolicyTerm { PolicyId = policy.Id, Number = 1, ProductId = policy.ProductId, ProductVersionId = cycle.ProductVersionId, StartsAt = cycle.StartsAt, EndsAt = cycle.EndsAt,
            LocalTermIntentJson = snapshot["termIntent"]!.ToJsonString(), CreatedBy = f.Underwriter.UserId, CreatedAt = now };
        db.Add(term); await db.SaveChangesAsync();
        var transaction = new PolicyTransaction { PolicyId = policy.Id, TermId = term.Id, SourceQuoteId = quote.Id, CycleId = cycle.Id, QuoteRevisionId = revision.Id, RatingId = setup.RatingId,
            AcceptanceId = accepted.ResourceId, Sequence = 1, EffectiveAt = term.StartsAt, ProcessedAt = now, CreatedAt = now, CreatedBy = f.Underwriter.UserId, Reason = "Fictional policy storage fixture", OperationKey = "policy-issue/" + policy.Id.ToString("N") };
        db.Add(transaction); await db.SaveChangesAsync();
        snapshot.AsObject().Remove("termIntent"); snapshot["term"] = JsonSerializer.SerializeToNode(new { kind = "annual", startsAt = term.StartsAt, endsAt = term.EndsAt, timeZone = "Europe/London" });
        snapshot["productVersionId"] = cycle.ProductVersionId.ToString(); snapshot["insured"]!["clientId"] = quote.ClientId.ToString(); snapshot["insured"]!["clientAgencyRelationshipId"] = quote.RelationshipId.ToString();
        var json = snapshot.ToJsonString(); var version = new PolicyVersion { PolicyId = policy.Id, TermId = term.Id, TransactionId = transaction.Id, Sequence = 1, SliceOrdinal = 1,
            SnapshotJson = json, ContentHash = SHA256.HashData(Encoding.UTF8.GetBytes(json)), EffectiveAt = term.StartsAt, ProcessedAt = now, CreatedAt = now, CreatedBy = f.Underwriter.UserId };
        db.Add(version); await db.SaveChangesAsync(); term.CurrentVersionId = version.Id; policy.CurrentTermId = term.Id; await db.SaveChangesAsync();
        return new(f, policy, term, transaction, version, cycle);
    }

    [Fact]
    public async Task RealSqlPolicyStorageRejectsDuplicateSourceForeignPointersAndChangedSnapshot()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await StoredPolicy(db, password); Assert.False(db.Database.HasPendingModelChanges());
            db.Add(new Policy { SourceQuoteId = f.Policy.SourceQuoteId, AgencyId = f.Policy.AgencyId, ClientId = f.Policy.ClientId, RelationshipId = f.Policy.RelationshipId, ProductId = f.Policy.ProductId, CreatedBy = f.Source.Underwriter.UserId });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var foreign = await db.Set<Quote>().Where(x => x.Id != f.Policy.SourceQuoteId).Select(x => x.Id).FirstAsync();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=N'bound',BoundPolicyId={f.Policy.Id} WHERE Id={foreign}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicyTerm SET CurrentVersionId={Guid.NewGuid()} WHERE Id={f.Term.Id}"));
            // Even the right policy cannot bind before balanced posting and documents.
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=N'bound',BoundPolicyId={f.Policy.Id} WHERE Id={f.Policy.SourceQuoteId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicyVersion SET SnapshotJson=N'{{}}' WHERE Id={f.Version.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE PolicyTransaction WHERE Id={f.Transaction.Id}"));
            db.Add(new PolicyVersion { PolicyId = f.Policy.Id, TermId = f.Term.Id, TransactionId = f.Transaction.Id, Sequence = 2, SliceOrdinal = 2, SnapshotJson = "{", ContentHash = SHA256.HashData([1]), EffectiveAt = f.Term.StartsAt, ProcessedAt = f.Transaction.ProcessedAt });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            Assert.Equal(f.Version.SnapshotJson, await db.Set<PolicyVersion>().Where(x => x.Id == f.Version.Id).Select(x => x.SnapshotJson).SingleAsync());
        });
    }
}

public sealed partial class UnderwritingStorageTests
{
    [Fact]
    public async Task RealSqlPolicyStorageUpgradePreservesAcceptedEraQuoteBytesAndCredentials()
    {
        await WithDatabase(async db =>
        {
            await db.GetService<IMigrator>().MigrateAsync("20260917013041_QuoteTermsDeliveryStorage");
            await Seed(db); var f = await Fixture(db, published: true); var quote = await InsertQuote(db, f);
            var cycle = await InsertCycle(db, quote, f); var rating = await InsertRating(db, cycle);
            var before = await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ProposalJson, x.ContentHash }).ToArrayAsync();
            var passwords = await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToArrayAsync();
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear(); Assert.False(db.Database.HasPendingModelChanges());
            foreach (var old in before) { var after = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == old.Id); Assert.Equal(old.ProposalJson, after.ProposalJson); Assert.Equal(old.ContentHash, after.ContentHash); }
            Assert.Equal(passwords, await db.Set<UserCredential>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.PasswordHash).ToArrayAsync());
            Assert.NotNull(await db.Set<QuoteRatingResult>().FindAsync(rating.Id)); Assert.Null(await db.Set<Quote>().Where(x => x.Id == quote.Id).Select(x => x.BoundPolicyId).SingleAsync());
            Assert.Empty(await db.Set<Policy>().ToArrayAsync());
        });
    }
}
