using System.Text.Json;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlFinancePeriodPendingAndAcknowledgedRefundPaymentBlockClose()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, actor, refundId, version, _) = await ApprovedPaymentRefund(db, password);
            db.Add(new SettingVersion { Scope = "finance-refund-payment-demo", Version = 2,
                EffectiveFrom = f.Clock.GetUtcNow(), Values = "{\"scenario\":\"timeout-after-success\"}" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var payments = new FinancePaymentService(f.Factory,
                new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var queued = await payments.QueueAsync(actor, refundId, version, null,
                "Queue reviewed refund before closing credit period", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var creditId = await db.Set<RefundRequest>().AsNoTracking()
                .Where(x => x.Id == refundId).Select(x => x.CreditObligationId).SingleAsync();
            var journal = await db.Set<Journal>().AsNoTracking()
                .SingleAsync(x => x.ObligationId == creditId);
            var period = await db.Set<AccountingPeriod>().AsNoTracking()
                .SingleAsync(x => x.Id == journal.AccountingPeriodId);
            var service = new FinancePeriodService(f.Factory,
                new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            Assert.Contains("refund-payment-pending", (await service.ReviewAsync(actor, period.Id)).Blockers);
            var now = f.Clock.GetUtcNow();
            var checklist = JsonSerializer.Serialize(new { reconciliation = "complete-or-not-applicable",
                payments = "no-acknowledged-unapplied-or-pending",
                statements = "current-for-every-active-agency-or-not-applicable",
                bordereaux = "valid-for-every-active-provider-or-not-applicable", checkedAt = now });
            var denied = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE AccountingPeriod SET State={"closed"},ClosedAt={now},ClosedBy={actor.UserId},
                    CloseReason={"Reviewed payment must settle before closure"},SourceCutoff={now},
                    CloseChecklistJson={checklist} WHERE Id={period.Id}
                """));
            Assert.Equal(52703, denied.Number);
            var workId = (await payments.DetailAsync(actor, queued.ResourceId)).WorkId;
            var lease = Assert.IsType<JobLease>(await new SqlJobLeases(f.Factory, f.Clock)
                .ClaimWorkAsync(FinancePaymentWorker.WorkKind, workId));
            var timeout = await Assert.ThrowsAsync<FinancePaymentWorkerException>(() =>
                new FinancePaymentWorker(f.Factory, f.Clock).ExecuteProviderAsync(lease));
            Assert.Equal(JobFailure.ProviderTimeout, timeout.Failure);
            Assert.Contains("refund-provider-acknowledged-unapplied",
                (await service.ReviewAsync(actor, period.Id)).Blockers);
            Assert.Equal("open", (await db.Set<AccountingPeriod>().AsNoTracking()
                .SingleAsync(x => x.Id == period.Id)).State);
        });
    }

    [Fact]
    public async Task RealSqlFinancePeriodCloseSealsHistoryAndLaterCorrectionPostsSeparately()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await StoredPolicy(db, password);
            var source = await StoredIssuePosting(db, f, false);
            var postedAt = f.Source.Clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Journal SET PostedAt={postedAt} WHERE Id={source.Journal.Id}");
            db.ChangeTracker.Clear();
            var actor = await FinanceLedgerActor(db);
            var period = await db.Set<AccountingPeriod>().AsNoTracking()
                .OrderBy(x => x.StartsOn).FirstAsync();
            var service = new FinancePeriodService(f.Source.Factory,
                new SqlCommandBoundary(f.Source.Factory, f.Source.Clock), f.Source.Clock);
            var review = await service.ReviewAsync(actor, period.Id);
            Assert.Contains("agency-statement-missing-or-stale", review.Blockers);
            Assert.Contains("provider-bordereau-missing-or-stale", review.Blockers);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CloseAsync(actor,
                period.Id, Convert.FromBase64String(review.Etag.Trim('"')),
                "Missing reports must block closure", Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            await new FinanceStatementService(f.Source.Factory,
                new SqlCommandBoundary(f.Source.Factory, f.Source.Clock), f.Source.Clock)
                .GenerateAsync(actor, source.Obligation.AgencyId, period.StartsOn, period.EndsOn,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var bordereaux = new FinanceBordereauService(f.Source.Factory,
                new SqlCommandBoundary(f.Source.Factory, f.Source.Clock), f.Source.Clock);
            var batch = await bordereaux.GenerateAsync(actor, source.Obligation.ProviderId, period.Id,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var batchVersionId = await db.Set<FinanceBordereauBatch>().AsNoTracking()
                .Where(x => x.Id == batch.ResourceId).Select(x => x.CurrentVersionId).SingleAsync();
            await bordereaux.ValidateAsync(actor, batch.ResourceId, batchVersionId,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            review = await service.ReviewAsync(actor, period.Id);
            Assert.Empty(review.Blockers);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CloseAsync(
                f.Source.Underwriter, period.Id, Convert.FromBase64String(review.Etag.Trim('"')),
                "Unprivileged close must be denied", Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var key = Guid.NewGuid().ToString("N");
            var closed = await service.CloseAsync(actor, period.Id,
                Convert.FromBase64String(review.Etag.Trim('"')),
                "Reviewed reported period and sealed history", key, Guid.NewGuid());
            Assert.Equal(period.Id, closed.ResourceId);
            Assert.Equal(period.Id, (await service.CloseAsync(actor, period.Id,
                Convert.FromBase64String(review.Etag.Trim('"')),
                "Reviewed reported period and sealed history", key, Guid.NewGuid())).ResourceId);
            var saved = await service.ReviewAsync(actor, period.Id);
            Assert.Equal("closed", saved.State);
            Assert.NotNull(saved.CloseChecklistJson);
            Assert.Contains("not-applicable", saved.CloseChecklistJson);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE AccountingPeriod SET State={"open"} WHERE Id={period.Id}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Journal SET PostedAt={postedAt.AddSeconds(1)} WHERE Id={source.Journal.Id}"));
            var sealedLineId = await db.Set<JournalLine>().AsNoTracking()
                .Where(x => x.JournalId == source.Journal.Id).Select(x => x.Id).FirstAsync();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE JournalLine SET CoverageStartsAt=DATEADD(day,1,CoverageStartsAt) WHERE Id={sealedLineId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE JournalLine WHERE Id={sealedLineId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT AccountingPeriod(Id,StartsOn,EndsOn,State,CreatedAt,UpdatedAt)
                VALUES({Guid.NewGuid()},{period.StartsOn.AddDays(1)},{period.EndsOn.AddDays(-1)},
                    {"open"},{postedAt},{postedAt})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT FinanceCorrection(Id,OriginalSourceKind,OriginalSourceId,AgencyId,RelationshipId,
                    PolicyId,TransactionId,DebtorKind,Reason,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{"insurance"},{source.Journal.Id},{source.Obligation.AgencyId},NULL,
                    {source.Obligation.PolicyId},{source.Obligation.TransactionId},{source.Obligation.DebtorKind},
                    {"Forged correction with mismatched relationship"},{postedAt},{actor.UserId})
                """));
            var corrected = await service.PostCorrectionAsync(actor, "insurance", source.Journal.Id,
                "-15.20", "10.00", "25.20", "0.00", postedAt,
                "Correct historical classification after period close", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var correction = await db.Set<FinanceCorrection>().AsNoTracking().SingleAsync(x => x.Id == corrected.ResourceId);
            var posting = await db.Set<FinancePosting>().AsNoTracking()
                .SingleAsync(x => x.SourceKind == "correction" && x.SourceId == correction.Id);
            Assert.Equal(source.Journal.Id, correction.OriginalSourceId);
            Assert.Equal(-15.20m, posting.DebtorDelta);
            Assert.Equal(25.20m, posting.CashDelta);
            Assert.NotEqual(period.Id, posting.AccountingPeriodId);
            Assert.True(posting.PostingDate >= period.EndsOn);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT FinancePosting(Id,SourceKind,SourceId,AgencyId,RelationshipId,PolicyId,TransactionId,
                    DebtorKind,AccountingPeriodId,PostingDate,EffectiveAt,PostedAt,Currency,DebtorDelta,
                    ProviderDelta,CashDelta,InternalDelta,Reason,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{"correction"},{Guid.NewGuid()},{posting.AgencyId},{posting.RelationshipId},
                    {posting.PolicyId},{posting.TransactionId},{posting.DebtorKind},{posting.AccountingPeriodId},
                    {posting.PostingDate},{posting.EffectiveAt},{postedAt},{"GBP"},{-1m},0,{1m},0,
                    {"Forged correction cash without source row"},{postedAt},{actor.UserId})
                """));
            var nextPeriod = await db.Set<AccountingPeriod>().AsNoTracking()
                .SingleAsync(x => x.Id == posting.AccountingPeriodId);
            var nextReview = await service.ReviewAsync(actor, nextPeriod.Id);
            Assert.Contains("cash-or-bank-reconciliation-missing", nextReview.Blockers);
            Assert.Contains("agency-statement-missing-or-stale", nextReview.Blockers);
            var recon = new FinanceReconciliationService(f.Source.Factory,
                new SqlCommandBoundary(f.Source.Factory, f.Source.Clock), f.Source.Clock);
            var window = await recon.CreateAsync(actor, posting.AgencyId, nextPeriod.StartsOn,
                nextPeriod.EndsOn, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var line = await recon.ImportAsync(actor, posting.AgencyId, "correction-bank-line-1",
                posting.PostingDate, "Fictional correction cash", "25.20", "GBP", "{\"source\":\"demo-bank\"}",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            await recon.MatchAsync(actor, window.ResourceId, line.ResourceId, posting.Id, "25.20",
                "Match signed correction cash source", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            await recon.CompleteAsync(actor, window.ResourceId, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Contains("agency-statement-missing-or-stale",
                (await service.ReviewAsync(actor, nextPeriod.Id)).Blockers);
            var statements = new FinanceStatementService(f.Source.Factory,
                new SqlCommandBoundary(f.Source.Factory, f.Source.Clock), f.Source.Clock);
            await statements.GenerateAsync(actor, posting.AgencyId, nextPeriod.StartsOn, nextPeriod.EndsOn,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Empty((await service.ReviewAsync(actor, nextPeriod.Id)).Blockers);
            f.Source.Clock.Current = f.Source.Clock.Current.AddMinutes(1);
            await service.PostCorrectionAsync(actor, "insurance", source.Journal.Id,
                "1.00", "0.00", "0.00", "-1.00", postedAt,
                "Later noncash correction makes the saved statement stale",
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Contains("agency-statement-missing-or-stale",
                (await service.ReviewAsync(actor, nextPeriod.Id)).Blockers);
            Assert.Equal("closed", (await db.Set<AccountingPeriod>().AsNoTracking()
                .SingleAsync(x => x.Id == period.Id)).State);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinancePeriodCloseWinsOverNewIssueWithoutBackdating()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            var actor = await FinanceLedgerActor(db);
            var period = await db.Set<AccountingPeriod>().AsNoTracking()
                .OrderBy(x => x.StartsOn).FirstAsync();
            var now = f.Clock.GetUtcNow();
            var checklist = JsonSerializer.Serialize(new { reconciliation = "complete-or-not-applicable",
                payments = "no-acknowledged-unapplied-or-pending",
                statements = "current-for-every-active-agency-or-not-applicable",
                bordereaux = "valid-for-every-active-provider-or-not-applicable", checkedAt = now });
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE AccountingPeriod SET State={"closed"},ClosedAt={now},ClosedBy={f.Underwriter.UserId},
                    CloseReason={"Unprivileged direct period close attempt"},SourceCutoff={now},
                    CloseChecklistJson={checklist} WHERE Id={period.Id}
                """));
            await using var closing = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE AccountingPeriod SET State={"closed"},ClosedAt={now},ClosedBy={actor.UserId},
                    CloseReason={"Reviewed no-activity close before issue"},SourceCutoff={now},
                    CloseChecklistJson={checklist}
                WHERE Id={period.Id}
                """);
            var issueTask = new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter,
                f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            await Task.Delay(400);
            Assert.False(issueTask.IsCompleted);
            await closing.CommitAsync();
            await issueTask;
            var journal = await db.Set<Journal>().AsNoTracking().SingleAsync();
            Assert.NotEqual(period.Id, journal.AccountingPeriodId);
            Assert.True(journal.PostingDate >= period.EndsOn);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
