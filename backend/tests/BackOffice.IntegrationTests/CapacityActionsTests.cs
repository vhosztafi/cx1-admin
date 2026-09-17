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
    public async Task RealSqlCapacityActionsPreserveHistoryAndFenceLateResponsesAndReplay()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, initial, first) = await CapacityRequest(db, password, "approve-stock-150000");
            var service = new CapacityService(f.Factory, f.Clock);
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            async Task<CapacityEscalation> Escalation() => await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == initial.Id);
            var row = await Escalation(); var version = await Version(); var key = Guid.NewGuid().ToString();
            var leases = new SqlJobLeases(f.Factory, f.Clock); var worker = new CapacityWorker(f.Factory, f.Clock);
            var lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, first.WorkId))!;
            var providerResult = await worker.ExecuteProviderAsync(lease);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ActionAsync(f.Servicing, f.QuoteId, f.CycleId, row.Id, version, row.RowVersion, "withdraw", null, "Withdraw exact pending request", key, Guid.NewGuid()))).Status);
            await service.ActionAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, version, row.RowVersion, "withdraw", null, "Withdraw exact pending request", key, Guid.NewGuid());
            Assert.True((await service.ActionAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, version, row.RowVersion, "withdraw", null, "Withdraw exact pending request", key, Guid.NewGuid())).Replayed);
            Assert.True(await worker.ApplyAsync(lease, providerResult));
            var withdrawn = await Escalation(); Assert.Equal("draft", withdrawn.State); Assert.Equal(first.Id, withdrawn.CurrentSubmissionId); Assert.Null(withdrawn.CurrentResponseId);
            Assert.Equal("superseded", (await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.SubmissionId == first.Id && x.Direction == "inbound")).ApplicationState);
            Assert.Equal(2, await db.Set<CapacityMessage>().CountAsync(x => x.EscalationId == row.Id));
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ActionAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, version, row.RowVersion, "withdraw", null, "Stale request", Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            await service.SendAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, await Version(), withdrawn.RowVersion, "Deliberate new follow-up request", [], first.ScenarioVersionId, Guid.NewGuid().ToString(), Guid.NewGuid());
            var second = await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.EscalationId == row.Id && x.Sequence == 2);
            var nextLease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, second.WorkId))!;
            Assert.True(await worker.ApplyAsync(nextLease, await worker.ExecuteProviderAsync(nextLease)));
            var approved = await Escalation(); Assert.Equal("approved", approved.State);
            await service.ActionAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, await Version(), approved.RowVersion, "reopen", null, "Reopen for a changed carrier request", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("draft", (await Escalation()).State); Assert.Equal(4, await db.Set<CapacityMessage>().CountAsync(x => x.EscalationId == row.Id));
            var actions = await db.Set<AuditEvent>().AsNoTracking().Where(x => x.SubjectRecordId == row.Id && x.EventType == "capacity.action-recorded").OrderBy(x => x.OccurredAt).ToArrayAsync();
            Assert.Equal(2, actions.Length); Assert.Contains(actions, x => x.Reason == "Withdraw exact pending request");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Current authority revoked' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ActionAsync(f.Underwriter, f.QuoteId, f.CycleId, row.Id, version, row.RowVersion, "withdraw", null, "Withdraw exact pending request", key, Guid.NewGuid()))).Status);
        });
    }
}
