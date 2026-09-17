using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSqlCapacitySelectedProofWithdrawalInvalidatesPendingOrAppliedExtent(bool applied)
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "approve-stock-150000", includeProof: true);
            var worker = new CapacityWorker(f.Factory, f.Clock); var leases = new SqlJobLeases(f.Factory, f.Clock);
            var lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!; var outcome = await worker.ExecuteProviderAsync(lease);
            if (applied)
            {
                Assert.True(await worker.ApplyAsync(lease, outcome));
                var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == escalation.ReferralId);
                var quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Underwriter, f.QuoteId);
                await new QuoteReferralService(f.Factory, f.Clock).DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, quote.Quote.RowVersion,
                    [new(referral.Id, referral.RowVersion, "approve", "Current carrier extent accepted", [])], Guid.NewGuid().ToString(), Guid.NewGuid());
                Assert.Equal("approved", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            }
            var selected = await db.Set<CapacitySubmissionEvidence>().SingleAsync(x => x.SubmissionId == submission.Id);
            var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == selected.EvidenceAssociationId);
            var current = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Servicing, f.QuoteId);
            await new UnderwritingEvidenceService(f.Factory, f.Clock).WithdrawAsync(f.Servicing, f.QuoteId, f.CycleId, proof.Id, current.Quote.RowVersion, proof.RowVersion, "Selected proof was incorrect", Guid.NewGuid().ToString(), Guid.NewGuid());
            if (applied) Assert.Equal("conditional", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == escalation.ReferralId)).State);
            else
            {
                Assert.True(await worker.ApplyAsync(lease, outcome));
                Assert.Null((await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).CurrentResponseId);
                Assert.Equal("superseded", (await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.SubmissionId == submission.Id && x.Direction == "inbound")).ApplicationState);
            }
            Assert.Equal(submission.ContextHash, (await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.Id == submission.Id)).ContextHash);
        });
    }
}
