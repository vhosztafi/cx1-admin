using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCapacityJobRecoveryRequiresBothCurrentCapabilitiesAndPreservesSubmission()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "approve-stock-150000");
            var leases = new SqlJobLeases(f.Factory, f.Clock); var jobs = new CapacityJobs(f.Factory, f.Clock);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
                Assert.True(await leases.FailAsync(lease, JobFailure.ProviderUnavailable)); f.Clock.Current = f.Clock.Current.AddHours(1);
            }
            Assert.Equal("failed", (await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).State);
            var failed = await jobs.ReadAsync(f.Underwriter, submission.WorkId, default); Assert.False(failed.RetryAllowed);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => jobs.RetryAsync(f.Underwriter, submission.WorkId, failed.Work.RowVersion, "Recover exact request", Guid.NewGuid().ToString(), Guid.NewGuid(), default))).Status);
            var adminRole = await db.Set<Role>().SingleAsync(x => x.Code == "system-admin");
            var link = new UserRole { UserId = f.Underwriter.UserId, RoleId = adminRole.Id }; db.Add(link); await db.SaveChangesAsync();
            var actor = f.Underwriter with { Roles = new HashSet<string> { "underwriter", "system-admin" } };
            Assert.True((await jobs.ReadAsync(actor, submission.WorkId, default)).RetryAllowed);
            var key = Guid.NewGuid().ToString();
            var retry = await jobs.RetryAsync(actor, submission.WorkId, failed.Work.RowVersion, "Recover exact request", key, Guid.NewGuid(), default);
            Assert.Equal(202, retry.Status);
            Assert.True((await jobs.RetryAsync(actor, submission.WorkId, failed.Work.RowVersion, "Recover exact request", key, Guid.NewGuid(), default)).Replayed);
            Assert.Equal(submission.Id, (await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).CurrentSubmissionId);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE Id={link.Id}"); db.ChangeTracker.Clear();
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => jobs.RetryAsync(actor, submission.WorkId, failed.Work.RowVersion, "Recover exact request", key, Guid.NewGuid(), default))).Status);
            var recoveryLease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!; var worker = new CapacityWorker(f.Factory, f.Clock);
            Assert.True(await worker.ApplyAsync(recoveryLease, await worker.ExecuteProviderAsync(recoveryLease)));
            Assert.Equal("approved", (await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).State);
            Assert.Equal(7, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == submission.WorkId));
            Assert.Single(await db.Set<CapacitySubmission>().Where(x => x.EscalationId == escalation.Id).ToArrayAsync());
        });
    }
}
