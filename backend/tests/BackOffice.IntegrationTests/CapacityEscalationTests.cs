using System.Text;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCapacityEscalationRetainsSubmissionAndChecksAuthorityBeforeReplay()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password, p => p["risk"]!["business"]!["startedOn"] = "2025-01-01"); var service = new CapacityService(f.Factory, f.Clock);
            async Task<byte[]> Version() => await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.RowVersion).SingleAsync();
            var referral = await db.Set<QuoteReferral>().AsNoTracking().FirstAsync(x => x.CycleId == f.CycleId);
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
            var provider = await db.Set<BinderVersion>().Where(x => x.Id == cycle.BinderVersionId).Select(x => x.ProviderId).SingleAsync();
            var version = await Version(); var createKey = Guid.NewGuid().ToString();
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.CreateAsync(f.Servicing, f.QuoteId, f.CycleId, referral.Id, version, referral.RowVersion, provider, "Review exact request", createKey, Guid.NewGuid()))).Status);
            var created = await service.CreateAsync(f.Underwriter, f.QuoteId, f.CycleId, referral.Id, version, referral.RowVersion, provider, "Review exact request", createKey, Guid.NewGuid());
            Assert.True((await service.CreateAsync(f.Underwriter, f.QuoteId, f.CycleId, referral.Id, version, referral.RowVersion, provider, "Review exact request", createKey, Guid.NewGuid())).Replayed);
            var escalation = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == created.ResourceId);
            var scenario = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "capacity-escalation/query-proof");
            var sendVersion = await Version(); var sendKey = Guid.NewGuid().ToString();
            var sent = await service.SendAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, sendVersion, escalation.RowVersion, "Please review this fictional request.", [], scenario.Id, sendKey, Guid.NewGuid());
            Assert.Equal(202, sent.Status);
            Assert.True((await service.SendAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, sendVersion, escalation.RowVersion, "Please review this fictional request.", [], scenario.Id, sendKey, Guid.NewGuid())).Replayed);
            var submission = await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.EscalationId == escalation.Id);
            Assert.Equal("Please review this fictional request.", submission.Body);
            Assert.Equal("queued", (await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).State);
            Assert.Single(await db.Set<CapacityMessage>().Where(x => x.EscalationId == escalation.Id && x.Direction == "outbound").ToArrayAsync());
            Assert.Equal("capacity-escalation", (await db.Set<OutboxWork>().SingleAsync(x => x.Id == submission.WorkId)).Kind);
            var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var purpose = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "capacity-response");
            Assert.Equal(submission.Id, purpose.CapacitySubmissionId);
            var upload = await evidence.UploadAsync(f.Servicing, f.QuoteId, await Version(), "carrier.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional supplied carrier response"), Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.Equal(422, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await Version(), upload.ResourceId, purpose.Code, null, null, null, purpose.InputFingerprint, "Wrong submission", Guid.NewGuid().ToString(), Guid.NewGuid(), capacitySubmissionId: Guid.NewGuid()))).Status);
            var attached = await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, await Version(), upload.ResourceId, purpose.Code, null, null, null, purpose.InputFingerprint, "Exact supplied response", Guid.NewGuid().ToString(), Guid.NewGuid(), capacitySubmissionId: submission.Id);
            Assert.False((await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "capacity-response").Satisfied);
            escalation = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id);
            var response = new CapacityResponseInput(submission.Id, submission.ContextHash, "query", "Fictional carrier underwriter", "DEMO-Q-1", "Please provide further trading evidence.", f.Clock.GetUtcNow(), attached.ResourceId, null, null, [], []);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(async () => await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, await Version(), escalation.RowVersion, response, Guid.NewGuid().ToString(), Guid.NewGuid()))).Status);
            var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await evidence.ReviewAsync(f.Underwriter, f.QuoteId, f.CycleId, proof.Id, await Version(), proof.RowVersion, "accepted", purpose.InputFingerprint, "Reviewed the supplied carrier letter", Guid.NewGuid().ToString(), Guid.NewGuid());
            var responseVersion = await Version(); var responseKey = Guid.NewGuid().ToString();
            var recorded = await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, responseVersion, escalation.RowVersion, response, responseKey, Guid.NewGuid());
            Assert.Equal(201, recorded.Status);
            Assert.True((await service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, responseVersion, escalation.RowVersion, response, responseKey, Guid.NewGuid())).Replayed);
            var inbound = await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.Id == recorded.ResourceId);
            Assert.Equal("supplied-response", inbound.Provenance); Assert.Equal("query", inbound.Outcome);
            Assert.Equal("queried", (await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).State);
            Assert.Equal(proof.Id, inbound.EvidenceAssociationId);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Current authority revoked' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.RecordResponseAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, responseVersion, escalation.RowVersion, response, responseKey, Guid.NewGuid()))).Status);
        });
    }
}
