using System.Text;
using BackOffice.Application.Quotes;
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
    public async Task RealSqlQuoteReferralDecisionStoragePreservesUpgradeAndRejectsForeignPointersOrRewrittenProof(bool upgrade)
    {
        await WithDatabase(async db =>
        {
            if (upgrade) await db.GetService<IMigrator>().MigrateAsync("20260916184737_UnderwritingCoreStorage");
            await Seed(db); var f = await Fixture(db, published: true);
            var quote = await InsertQuote(db, f); var other = await InsertQuote(db, f);
            var cycle = await InsertCycle(db, quote, f, legacy: upgrade); var foreign = await InsertCycle(db, other, f, legacy: upgrade);
            var rating = await InsertRating(db, cycle); var otherRating = await InsertRating(db, foreign);
            var retained = await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync();
            if (upgrade) { await db.Database.MigrateAsync(); db.ChangeTracker.Clear(); }
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(retained, await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync());
            var now = DateTimeOffset.UtcNow;
            QuoteReferral Referral(UnderwritingCycle c, QuoteRatingResult r) => new() { CycleId = c.Id, QuoteId = c.QuoteId, RatingId = r.Id, Sequence = 1, RuleCode = "UW-22", Dimension = "trading-history", Reason = "Review actual trading history" };
            var referral = Referral(cycle, rating); var otherReferral = Referral(foreign, otherRating);
            db.AddRange(referral, otherReferral); await db.SaveChangesAsync();
            var decision = new QuoteReferralDecision { ReferralId = referral.Id, CycleId = cycle.Id, QuoteId = quote.Id, Sequence = 1,
                Outcome = "query", Question = "Supply trading history", ConditionsJson = "[{\"code\":\"provide-trading-history\"}]",
                ActorId = f.Actor, AuthorityVersionId = cycle.AuthorityVersionId, Reason = "Independent review", CreatedBy = f.Actor, CreatedAt = now, DecidedAt = now };
            db.Add(decision); await db.SaveChangesAsync();
            var condition = new QuoteCondition { DecisionId = decision.Id, ReferralId = referral.Id, CycleId = cycle.Id, QuoteId = quote.Id,
                Sequence = 1, Kind = "documentary", Code = "provide-trading-history", DefinitionJson = "{\"code\":\"provide-trading-history\"}" };
            db.Add(condition); await db.SaveChangesAsync();
            var bytes = QuoteEvidenceRules.File("proof.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional trading history"));
            var file = new QuoteEvidenceFile { QuoteId = quote.Id, FileName = bytes.FileName, ContentType = bytes.ContentType, Content = bytes.Content,
                Sha256 = bytes.Sha256, ByteLength = bytes.Content.Length, CreatedBy = f.Actor, CreatedAt = now };
            db.Add(file); await db.SaveChangesAsync();
            var association = new UnderwritingEvidenceAssociation { QuoteId = quote.Id, CycleId = cycle.Id, FileId = file.Id, ConditionId = condition.Id,
                RequirementCode = "trading-history", InputFingerprint = new string('a', 64), Reason = "Supplied proof", CreatedBy = f.Actor, CreatedAt = now };
            db.Add(association); await db.SaveChangesAsync();
            var review = new UnderwritingEvidenceEvent { AssociationId = association.Id, CycleId = cycle.Id, QuoteId = quote.Id, Sequence = 1,
                Kind = "review", Outcome = "accepted", Reason = "Reviewed", ActorId = f.Actor, AuthorityVersionId = cycle.AuthorityVersionId,
                RecordedAt = now, CreatedAt = now, CreatedBy = f.Actor, InputFingerprint = association.InputFingerprint };
            db.Add(review); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UnderwritingEvidenceAssociation SET LatestReviewId={review.Id} WHERE Id={association.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteReferral SET LatestDecisionId={decision.Id} WHERE Id={referral.Id}");
            async Task Reject(FormattableString sql) { await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql)); db.ChangeTracker.Clear(); }
            await Reject($"UPDATE QuoteReferral SET LatestDecisionId={decision.Id} WHERE Id={otherReferral.Id}");
            await Reject($"UPDATE QuoteReferral SET LatestDecisionId=NULL WHERE Id={referral.Id}");
            await Reject($"UPDATE UnderwritingEvidenceAssociation SET WithdrawnEventId={review.Id} WHERE Id={association.Id}");
            await Reject($"UPDATE UnderwritingEvidenceAssociation SET CycleId={foreign.Id},QuoteId={other.Id} WHERE Id={association.Id}");
            await Reject($"UPDATE UnderwritingEvidenceEvent SET Outcome=N'rejected' WHERE Id={review.Id}");
            await Reject($"DELETE QuoteReferralDecision WHERE Id={decision.Id}");
            await Reject($"UPDATE QuoteCondition SET Wording=N'Everything approved' WHERE Id={condition.Id}");
            var resolution = new QuoteConditionResolution { ConditionId = condition.Id, CycleId = cycle.Id, QuoteId = quote.Id, Sequence = 1,
                EvidenceAssociationId = association.Id, EvidenceReviewId = review.Id, Outcome = "satisfied", ActorId = f.Actor,
                AuthorityVersionId = cycle.AuthorityVersionId, Reason = "Exact proof reviewed", CreatedBy = f.Actor, CreatedAt = now, RecordedAt = now };
            db.Add(resolution); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuoteCondition SET LatestResolutionId={resolution.Id} WHERE Id={condition.Id}");
            await Reject($"UPDATE QuoteCondition SET LatestResolutionId=NULL WHERE Id={condition.Id}");
            await Reject($"DELETE QuoteConditionResolution WHERE Id={resolution.Id}");
            var withdrawal = new UnderwritingEvidenceEvent { AssociationId = association.Id, CycleId = cycle.Id, QuoteId = quote.Id, Sequence = 2,
                Kind = "withdrawal", Reason = "Proof withdrawn", ActorId = f.Actor, CreatedBy = f.Actor, CreatedAt = now, RecordedAt = now, InputFingerprint = association.InputFingerprint };
            db.Add(withdrawal); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UnderwritingEvidenceAssociation SET WithdrawnEventId={withdrawal.Id} WHERE Id={association.Id}");
            await Reject($"UPDATE UnderwritingEvidenceAssociation SET WithdrawnEventId=NULL WHERE Id={association.Id}");
            Assert.Equal(retained, await db.Set<QuoteRevision>().AsNoTracking().OrderBy(x => x.Id).Select(x => x.ProposalJson).ToArrayAsync());
        }, upgrade);
    }
}
