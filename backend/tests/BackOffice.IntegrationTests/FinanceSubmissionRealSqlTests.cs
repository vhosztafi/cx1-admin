using System.Security.Cryptography;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlFinanceSubmissionPinsExactValidatedVersionAndRetainsHistoricalDownload()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var journal = await db.Set<Journal>().AsNoTracking().SingleAsync();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var actor = await FinanceLedgerActor(db);
            f.Clock.Current = f.Clock.GetUtcNow().AddSeconds(2);
            var commands = new SqlCommandBoundary(f.Factory, f.Clock);
            var bordereaux = new FinanceBordereauService(f.Factory, commands, f.Clock);
            var submission = new FinanceSubmissionService(f.Factory, commands, f.Clock);
            var generated = await bordereaux.GenerateAsync(actor, obligation.ProviderId,
                journal.AccountingPeriodId!.Value, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var draft = await bordereaux.DetailAsync(actor, generated.ResourceId);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.QueueAsync(actor,
                draft.BatchId, draft.Id, new string('A', 64), Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            await bordereaux.ValidateAsync(actor, draft.BatchId, draft.Id, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var valid = await bordereaux.DetailAsync(actor, draft.BatchId);
            var originalBytes = (await bordereaux.DownloadAsync(actor, draft.BatchId, valid.Id)).Bytes.ToArray();
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.QueueAsync(actor,
                draft.BatchId, valid.Id, new string('B', 64), Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var key = Guid.NewGuid().ToString("N");
            var queued = await submission.QueueAsync(actor, draft.BatchId, valid.Id, valid.ContentHash!, key, Guid.NewGuid());
            var replay = await new FinanceSubmissionService(f.Factory, commands, f.Clock).QueueAsync(actor,
                draft.BatchId, valid.Id, valid.ContentHash!, key, Guid.NewGuid());
            Assert.True(replay.Replayed);
            Assert.Equal(queued.ResourceId, replay.ResourceId);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.QueueAsync(actor,
                draft.BatchId, valid.Id, valid.ContentHash!, Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var before = await submission.DetailAsync(actor, draft.BatchId, valid.Id);
            Assert.Equal("queued", before.State);
            var leases = new SqlJobLeases(f.Factory, f.Clock);
            var lease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinanceSubmissionWorker.WorkKind, before.WorkId));
            var worker = new FinanceSubmissionWorker(f.Factory, f.Clock);
            var outcome = Assert.IsType<FinanceSubmissionProviderOutcome>(await worker.ExecuteProviderAsync(lease));
            Assert.Equal(InboxApplication.Applied, await worker.ApplyAsync(lease, outcome));
            Assert.Equal(InboxApplication.Duplicate, await worker.ApplyAsync(lease, outcome));
            var applied = await submission.DetailAsync(actor, draft.BatchId, valid.Id);
            Assert.Equal("submitted", applied.State);
            Assert.Equal(outcome.OperationId, applied.ProviderOperationId);
            Assert.Equal(valid.ContentHash, applied.ContentHash);
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinanceSubmissionWorker.WorkKind));
            await bordereaux.CorrectAsync(actor, draft.BatchId, valid.Id, journal.Id, "LATER-MAP", null, null,
                "Fictional later mapping correction", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            Assert.Equal(originalBytes, (await bordereaux.DownloadAsync(actor, draft.BatchId, valid.Id)).Bytes);
            var successor = await bordereaux.DetailAsync(actor, draft.BatchId);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => bordereaux.DownloadAsync(actor,
                draft.BatchId, successor.Id))).Status);
            Assert.True((await submission.QueueAsync(actor, draft.BatchId, valid.Id, valid.ContentHash!, key, Guid.NewGuid())).Replayed);
            await bordereaux.ValidateAsync(actor, draft.BatchId, successor.Id,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var changedIntent = await bordereaux.DetailAsync(actor, draft.BatchId);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.QueueAsync(actor,
                draft.BatchId, changedIntent.Id, changedIntent.ContentHash!, key, Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.DetailAsync(actor,
                draft.BatchId, valid.Id))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.QueueAsync(actor,
                draft.BatchId, valid.Id, valid.ContentHash!, key, Guid.NewGuid()))).Status);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinanceSubmissionTimeoutSuccessorCrashAndRejectionUseOneProviderEffect()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var journal = await db.Set<Journal>().AsNoTracking().SingleAsync();
            var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync();
            var actor = await FinanceLedgerActor(db);
            f.Clock.Current = f.Clock.GetUtcNow().AddSeconds(2);
            var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
            var bordereaux = new FinanceBordereauService(f.Factory, boundary, f.Clock);
            var submission = new FinanceSubmissionService(f.Factory, boundary, f.Clock);
            var leases = new SqlJobLeases(f.Factory, f.Clock);
            async Task<BordereauVersionView> ValidBatch()
            {
                var generated = await bordereaux.GenerateAsync(actor, obligation.ProviderId,
                    journal.AccountingPeriodId!.Value, Guid.NewGuid().ToString("N"), Guid.NewGuid());
                var draft = await bordereaux.DetailAsync(actor, generated.ResourceId);
                await bordereaux.ValidateAsync(actor, draft.BatchId, draft.Id,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid());
                return await bordereaux.DetailAsync(actor, draft.BatchId);
            }
            async Task SetScenario(int version, string name)
            {
                db.Add(new SettingVersion { Scope = "finance-bordereau-submission-demo", Version = version,
                    EffectiveFrom = f.Clock.GetUtcNow(), Values = "{\"scenario\":\"" + name + "\"}" });
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
            await SetScenario(2, "timeout-after-success");
            var valid = await ValidBatch();
            var originalBytes = (await bordereaux.DownloadAsync(actor, valid.BatchId, valid.Id)).Bytes.ToArray();
            await submission.QueueAsync(actor, valid.BatchId, valid.Id, valid.ContentHash!,
                Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var before = await submission.DetailAsync(actor, valid.BatchId, valid.Id);
            var firstLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinanceSubmissionWorker.WorkKind, before.WorkId));
            var firstWorker = new FinanceSubmissionWorker(f.Factory, f.Clock);
            var timeout = await Assert.ThrowsAsync<FinanceSubmissionWorkerException>(() =>
                firstWorker.ExecuteProviderAsync(firstLease));
            Assert.Equal(JobFailure.ProviderTimeout, timeout.Failure);
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinanceSubmissionWorker.WorkKind));
            Assert.True(await leases.FailAsync(firstLease, timeout.Failure));
            Assert.Equal("uncertain", (await submission.DetailAsync(actor, valid.BatchId, valid.Id)).State);
            await bordereaux.CorrectAsync(actor, valid.BatchId, valid.Id, journal.Id, "LATER-MAP", null, null,
                "Fictional mapping correction after accepted timeout", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var successor = await bordereaux.DetailAsync(actor, valid.BatchId);
            Assert.Equal("unvalidated", successor.State);
            Assert.Equal(valid.Id, successor.ParentVersionId);
            var financeRoleId = await db.Set<Role>().Where(x => x.Code == "finance").Select(x => x.Id).SingleAsync();
            var underwriterRoleId = await db.Set<Role>().Where(x => x.Code == "underwriter")
                .Select(x => x.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={underwriterRoleId} WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.DetailAsync(actor,
                valid.BatchId, valid.Id))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => submission.QueueAsync(actor,
                valid.BatchId, valid.Id, valid.ContentHash!, Guid.NewGuid().ToString("N"), Guid.NewGuid()))).Status);
            var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == before.WorkId);
            f.Clock.Current = work.NextAttemptAt.AddSeconds(1);
            var secondLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinanceSubmissionWorker.WorkKind, before.WorkId));
            var restarted = new FinanceSubmissionWorker(f.Factory, f.Clock);
            var recovered = Assert.IsType<FinanceSubmissionProviderOutcome>(await restarted.ExecuteProviderAsync(secondLease));
            // Simulate a process crash after recovering the saved provider result but before local apply.
            f.Clock.Current = f.Clock.GetUtcNow().Add(SqlJobLeases.LeaseDuration).AddSeconds(1);
            var thirdLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinanceSubmissionWorker.WorkKind, before.WorkId));
            var afterCrash = Assert.IsType<FinanceSubmissionProviderOutcome>(await new FinanceSubmissionWorker(f.Factory, f.Clock)
                .ExecuteProviderAsync(thirdLease));
            Assert.Equal(recovered.OperationId, afterCrash.OperationId);
            Assert.Equal(InboxApplication.Applied, await restarted.ApplyAsync(thirdLease, afterCrash));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={financeRoleId} WHERE UserId={actor.UserId}");
            Assert.Equal("submitted", (await submission.DetailAsync(actor, valid.BatchId, valid.Id)).State);
            Assert.Equal(originalBytes, (await bordereaux.DownloadAsync(actor, valid.BatchId, valid.Id)).Bytes);
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinanceSubmissionWorker.WorkKind));
            Assert.Equal(1, await db.Set<AdapterInbox>().CountAsync(x => x.Provider == "finance-bordereau-demo"));
            Assert.Equal(3, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == before.WorkId));
            Assert.Equal(InboxApplication.Quarantined, await restarted.ApplyAsync(thirdLease,
                afterCrash with { ProviderReference = "CHANGED-DEMO-REFERENCE" }));
            Assert.Equal(1, await db.Set<AdapterQuarantine>().CountAsync());
            await SetScenario(3, "reject");
            var rejectedVersion = await ValidBatch();
            await submission.QueueAsync(actor, rejectedVersion.BatchId, rejectedVersion.Id,
                rejectedVersion.ContentHash!, Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var queuedReject = await submission.DetailAsync(actor, rejectedVersion.BatchId, rejectedVersion.Id);
            var rejectLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinanceSubmissionWorker.WorkKind, queuedReject.WorkId));
            var refusal = Assert.IsType<FinanceSubmissionProviderOutcome>(await restarted.ExecuteProviderAsync(rejectLease));
            Assert.Equal("rejected", refusal.State);
            Assert.Equal(InboxApplication.Applied, await restarted.ApplyAsync(rejectLease, refusal));
            Assert.Equal("rejected", (await submission.DetailAsync(actor, rejectedVersion.BatchId, rejectedVersion.Id)).State);
            Assert.Equal(2, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinanceSubmissionWorker.WorkKind));
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
