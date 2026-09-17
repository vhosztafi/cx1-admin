using System.Text;
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
    public async Task RealSqlCapacityConditionsSurviveStaffReopenAndWithdrawnAcknowledgementRestoresBlocker()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "conditional-security");
            var worker = new CapacityWorker(f.Factory, f.Clock); var leases = new SqlJobLeases(f.Factory, f.Clock);
            var lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
            Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            var condition = await db.Set<QuoteCondition>().AsNoTracking().SingleAsync(x => x.ReferralId == escalation.ReferralId);
            var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock); var decisions = new QuoteReferralService(f.Factory, f.Clock);
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            async Task<QuoteReferral> Referral() => await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == escalation.ReferralId);
            var row = await Referral();
            await decisions.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(), [new(row.Id, row.RowVersion, "reopen", "Staff review reopened", [])], Guid.NewGuid().ToString(), Guid.NewGuid());
            row = await Referral(); Assert.NotEqual(condition.DecisionId, row.LatestDecisionId);
            var purpose = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.ConditionId == condition.Id);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await decisions.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(row.Id, row.RowVersion, "approve", "Unresolved carrier warranty cannot disappear", [])], Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            var uploaded = await evidence.UploadAsync(f.Servicing, f.QuoteId, await Version(), "warranty.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional acknowledgement of the exact premises security warranty"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var attached = await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await Version(), uploaded.ResourceId, purpose.Code, purpose.RiskItemId, condition.Id, null,
                purpose.InputFingerprint, "Exact carrier warranty acknowledgement", Guid.NewGuid().ToString(), Guid.NewGuid());
            var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await evidence.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, proof.Id, await Version(), proof.RowVersion, "accepted", purpose.InputFingerprint, "Content reviewed", Guid.NewGuid().ToString(), Guid.NewGuid());
            await decisions.ResolveAsync(f.Underwriter, f.QuoteId, row.Id, f.CycleId, condition.Id, await Version(), condition.RowVersion, proof.Id, "satisfied", "Carrier warranty acknowledged", Guid.NewGuid().ToString(), Guid.NewGuid());
            row = await Referral();
            await decisions.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(), [new(row.Id, row.RowVersion, "approve", "Exact carrier condition now satisfied", [])], Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("approved", (await Referral()).State);
            proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == proof.Id);
            await evidence.WithdrawAsync(f.Servicing, f.QuoteId, f.CycleId, proof.Id, await Version(), proof.RowVersion, "Acknowledgement withdrawn", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("conditional", (await Referral()).State);
            Assert.False((await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.ConditionId == condition.Id).Satisfied);
        });
    }
}
