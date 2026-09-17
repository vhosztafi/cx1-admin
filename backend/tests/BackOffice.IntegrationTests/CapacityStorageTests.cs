using System.Security.Cryptography;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSqlCapacityStoragePreservesUpgradeAndRejectsForeignPointersOrMutableCorrespondence(bool upgrade)
    {
        await WithDatabase(async db =>
        {
            if (upgrade) await db.GetService<IMigrator>().MigrateAsync("20260916220625_UnderwritingPreparedTermsFence");
            await Seed(db); var f = await Fixture(db, published: true);
            var quote = await InsertQuote(db, f); var other = await InsertQuote(db, f);
            var cycle = await InsertCycle(db, quote, f, legacy: upgrade); var foreign = await InsertCycle(db, other, f, legacy: upgrade);
            var rating = await InsertRating(db, cycle); var otherRating = await InsertRating(db, foreign);
            var retained = await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync();
            if (upgrade) { await db.Database.MigrateAsync(); db.ChangeTracker.Clear(); }
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(retained, await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync());
            var provider = await db.Set<BinderVersion>().Where(x => x.Id == cycle.BinderVersionId).Select(x => x.ProviderId).SingleAsync();
            var scenario = await db.Set<SettingVersion>().FirstAsync();
            async Task<(CapacityEscalation Escalation, CapacitySubmission Submission)> Add(UnderwritingCycle c, QuoteRatingResult r)
            {
                var now = DateTimeOffset.UtcNow;
                var referral = new QuoteReferral { CycleId = c.Id, QuoteId = c.QuoteId, RatingId = r.Id, Sequence = 1, RuleCode = "stock-limit", Dimension = "stock-limit", Reason = "Review stock" };
                db.Add(referral); await db.SaveChangesAsync();
                var escalation = new CapacityEscalation { QuoteId = c.QuoteId, CycleId = c.Id, ReferralId = referral.Id, ProviderId = provider, BinderVersionId = c.BinderVersionId, RaisedBy = f.Actor, CreatedBy = f.Actor, Reason = "Explicit capacity request", CreatedAt = now };
                db.Add(escalation); await db.SaveChangesAsync();
                var work = new OutboxWork { Kind = "capacity-escalation", OperationKey = Guid.NewGuid().ToString(), ScenarioVersionId = scenario.Id, SubjectRecordId = escalation.Id, NextAttemptAt = now };
                db.Add(work); await db.SaveChangesAsync();
                var submission = new CapacitySubmission { QuoteId = c.QuoteId, CycleId = c.Id, EscalationId = escalation.Id, Sequence = 1, Body = "Fictional exact request", ContextHash = new string('a', 64), WorkId = work.Id, ScenarioVersionId = scenario.Id, SubmittedBy = f.Actor, CreatedBy = f.Actor, SubmittedAt = now, CreatedAt = now, ResponseDueAt = now.AddDays(2) };
                db.Add(submission); await db.SaveChangesAsync(); escalation.CurrentSubmissionId = submission.Id; await db.SaveChangesAsync();
                return (escalation, submission);
            }
            var first = await Add(cycle, rating); var second = await Add(foreign, otherRating);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityEscalation SET CurrentSubmissionId={second.Submission.Id} WHERE Id={first.Escalation.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacitySubmission SET Body=N'rewritten request' WHERE Id={first.Submission.Id}"));
            var recorded = DateTimeOffset.UtcNow;
            var outbound = new CapacityMessage { QuoteId = cycle.QuoteId, CycleId = cycle.Id, EscalationId = first.Escalation.Id, SubmissionId = first.Submission.Id, ReferralId = first.Escalation.ReferralId,
                ProviderId = provider, Sequence = 1, Body = "Fictional outward correspondence", ContentHash = SHA256.HashData([1]), RecordedAt = recorded, CreatedAt = recorded, RecordedBy = f.Actor, CreatedBy = f.Actor };
            db.Add(outbound); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityEscalation SET CurrentResponseId={outbound.Id} WHERE Id={first.Escalation.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityMessage SET Body=N'rewritten correspondence' WHERE Id={outbound.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM CapacityMessage WHERE Id={outbound.Id}"));
            Assert.Equal("Fictional exact request", await db.Set<CapacitySubmission>().AsNoTracking().Where(x => x.Id == first.Submission.Id).Select(x => x.Body).SingleAsync());
        }, upgrade);
    }
}
