using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCapacitySuppliedExtentRequiresExactHashAmountAndRetainedReviewedLetter()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "query-proof");
            var service = new CapacityService(f.Factory, f.Clock); var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            async Task<CapacityEscalation> Escalation() => await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id);
            var purpose = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.CapacitySubmissionId == submission.Id);
            var file = await evidence.UploadAsync(f.Servicing, f.QuoteId, await Version(), "carrier-limit.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional exact stock approval letter"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var attached = await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await Version(), file.ResourceId, purpose.Code, null, null, null, purpose.InputFingerprint,
                "Actual carrier letter", Guid.NewGuid().ToString(), Guid.NewGuid(), capacitySubmissionId: submission.Id);
            var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await evidence.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, proof.Id, await Version(), proof.RowVersion, "accepted", purpose.InputFingerprint, "Reviewed letter against request", Guid.NewGuid().ToString(), Guid.NewGuid());
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
            var response = new CapacityResponseInput(submission.Id, submission.ContextHash, "approve", "Fictional carrier", "DEMO-LIMIT", "Explicit supplied stock limit", f.Clock.GetUtcNow(), proof.Id,
                cycle.StartsAt, cycle.EndsAt, [JsonSerializer.SerializeToElement(new { dimension = "stock-limit", maximumAmount = "149999.99" })], []);
            Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, await Version(), (await Escalation()).RowVersion,
                response with { SubmissionHash = new string('b', 64) }, Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, await Version(), (await Escalation()).RowVersion,
                response with { EvidenceAssociationId = Guid.NewGuid() }, Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, await Version(), (await Escalation()).RowVersion, response, Guid.NewGuid().ToString(), Guid.NewGuid());
            var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == escalation.ReferralId); var decisions = new QuoteReferralService(f.Factory, f.Clock);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await decisions.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(),
                [new(referral.Id, referral.RowVersion, "approve", "One penny beyond supplied extent", [])], Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            response = response with { AuthorisedLimits = [JsonSerializer.SerializeToElement(new { dimension = "stock-limit", maximumAmount = "150000.00" })] };
            await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, await Version(), (await Escalation()).RowVersion, response, Guid.NewGuid().ToString(), Guid.NewGuid());
            await decisions.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, await Version(), [new(referral.Id, referral.RowVersion, "approve", "Exact supplied extent", [])], Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("approved", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
            proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == proof.Id);
            await evidence.WithdrawAsync(f.Servicing, f.QuoteId, f.CycleId, proof.Id, await Version(), proof.RowVersion, "Carrier letter withdrawn", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal("conditional", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
        });
    }
}
