using System.Text.Json;
using System.Text;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacitySubmit(BackOfficeDbContext db, DecisionFixture f, ServicingCapacityCase capacity, Guid lease, string etag)
    {
        static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
        static string Key() => Guid.NewGuid().ToString();
        var service = new ServicingCapacityService(f.Factory, f.Clock);
        var scenario = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == "capacity-escalation/query-proof").OrderByDescending(x => x.Version).FirstAsync();
        var key = Key(); const string body = "Please review this fictional capacity request";
        Task<CommandOutcome> Submit(string command, string version, ActorContext? actor = null, Guid? caseId = null,
            Guid? fence = null, Guid? scenarioId = null, byte[]? caseVersion = null, Guid[]? evidence = null, string message = body) =>
            service.SubmitAsync(actor ?? f.Underwriter, capacity.DraftId, capacity.CycleId, caseId ?? capacity.Id,
                Version(version), caseVersion ?? capacity.RowVersion, fence ?? lease, message, "Submit fictional capacity request", evidence ?? [],
                scenarioId ?? scenario.Id, command, Guid.NewGuid());
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, actor: f.Servicing))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, caseId: Guid.NewGuid()))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, fence: Guid.NewGuid()))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, scenarioId: Guid.NewGuid()))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), "\"AAAAAAAAAAA=\""))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, caseVersion: new byte[8]))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, evidence: [Guid.NewGuid()]))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), etag, message: new string('x', 10001)))).Status);
        Assert.Empty(await db.Set<ServicingCapacitySubmission>().ToArrayAsync());
        var sent = await Submit(key, etag); Assert.Equal(202, sent.Status); Assert.False(sent.Replayed);
        var row = await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleAsync();
        var updated = await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x => x.Id == capacity.Id);
        Assert.Equal(row.Id, updated.CurrentSubmissionId); Assert.Equal("queued", updated.State);
        Assert.Equal(capacity.Id, row.CaseId); Assert.Equal(capacity.RatingId, row.RatingId); Assert.Equal(body, row.Body);
        var firstMessage = await db.Set<ServicingCapacityMessage>().AsNoTracking().SingleAsync();
        Assert.Equal(row.Id, firstMessage.SubmissionId); Assert.Equal("submission", firstMessage.Kind); Assert.Equal(body, firstMessage.Body);
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == row.WorkId);
        Assert.Equal("servicing-capacity", work.Kind); Assert.Equal(row.Id, work.SubjectRecordId); Assert.Equal("pending", work.State);
        Assert.Equal(row.WorkId, JsonSerializer.Deserialize<JsonElement>(sent.Body).GetProperty("jobId").GetGuid());
        using var context = JsonDocument.Parse(row.ContextJson);
        Assert.Equal(capacity.ProviderId, context.RootElement.GetProperty("providerId").GetGuid());
        Assert.Equal(capacity.ReferralId, context.RootElement.GetProperty("referralId").GetGuid());
        Assert.False(context.RootElement.TryGetProperty("proposal", out _)); Assert.False(context.RootElement.TryGetProperty("supportFlags", out _));
        var replay = await Submit(key, etag); Assert.True(replay.Replayed); Assert.Equal(sent.Body, replay.Body);
        await Assert.ThrowsAsync<CommandKeyConflictException>(() => Submit(key, etag, message: "Changed fictional provider request"));
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(Key(), sent.Etag!, caseVersion: updated.RowVersion))).Status);
        Assert.Single(await db.Set<AuditEvent>().Where(x => x.EventType == "servicing.capacity-submitted").ToArrayAsync());
        var actionVersion = await VerifyServicingCapacityChase(db, f, updated, row.Id, lease, sent.Etag!);
        // A new request freezes the reviewed proof identities, not just file IDs.
        Task<CommandOutcome> Act(string command, string version, byte[] caseVersion, string action = "withdraw", ActorContext? actor = null,
            Guid? fence = null, Guid? id = null) => service.ActionAsync(actor ?? f.Underwriter, capacity.DraftId, capacity.CycleId,
                id ?? capacity.Id, Version(version), caseVersion, fence ?? lease, action, "Withdraw fictional capacity request", command, Guid.NewGuid());
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), actionVersion, updated.RowVersion, actor: f.Servicing))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), actionVersion, updated.RowVersion, id: Guid.NewGuid()))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), actionVersion, updated.RowVersion, fence: Guid.NewGuid()))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), etag, updated.RowVersion))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), actionVersion, new byte[8]))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), actionVersion, updated.RowVersion, action: "reopen"))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), actionVersion, updated.RowVersion, action: "approve"))).Status);
        var withdrawKey = Key(); var withdrawal = await Act(withdrawKey, actionVersion, updated.RowVersion);
        Assert.Equal(200, withdrawal.Status);
        Assert.True((await Act(withdrawKey, actionVersion, updated.RowVersion)).Replayed);
        await Assert.ThrowsAsync<CommandKeyConflictException>(() => Act(withdrawKey, actionVersion, updated.RowVersion, action: "reopen"));
        var withdrawn = await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x => x.Id == capacity.Id);
        Assert.Equal("draft", withdrawn.State); Assert.Equal(row.Id, withdrawn.CurrentSubmissionId);
        Assert.Single(await db.Set<ServicingCapacitySubmission>().ToArrayAsync());
        Assert.Single(await db.Set<AuditEvent>().Where(x => x.EventType == "servicing.capacity-action-detail").ToArrayAsync());
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(Key(), withdrawal.Etag!, withdrawn.RowVersion))).Status);
        var proofs = new ServicingEvidenceService(f.Factory, f.Clock);
        var uploaded = await proofs.UploadAsync(f.Underwriter, capacity.DraftId, Version(withdrawal.Etag!), lease,
            "fictional-capacity.txt", "text/plain", Encoding.UTF8.GetBytes("Fictional capacity supporting proof"), Key(), Guid.NewGuid());
        var requirement = (await proofs.RequirementsAsync(f.Underwriter, capacity.DraftId)).Requirements.Single(x => x.Requirement.Code == "motor-trader-proof").Requirement;
        var attached = await proofs.AttachAsync(f.Underwriter, capacity.DraftId, capacity.CycleId, Version(uploaded.Etag!), lease,
            uploaded.ResourceId, requirement.Code, requirement.RiskItemId, requirement.InputFingerprint, "Attach fictional carrier proof", Key(), Guid.NewGuid());
        var association = await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
        Assert.Equal("servicing-capacity-evidence-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() =>
            Submit(Key(), attached.Etag!, caseVersion: withdrawn.RowVersion, evidence: [association.Id]))).Code);
        var reviewed = await proofs.ReviewAsync(f.Underwriter, capacity.DraftId, capacity.CycleId, association.Id, Version(attached.Etag!), lease,
            association.RowVersion, "accepted", association.InputFingerprint, "Review fictional carrier proof", Key(), Guid.NewGuid());
        association = await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
        var secondKey = Key();
        var second = await Submit(secondKey, reviewed.Etag!, caseVersion: withdrawn.RowVersion, evidence: [association.Id]);
        Assert.Equal(202, second.Status);
        var selection = await db.Set<ServicingCapacitySubmissionEvidence>().AsNoTracking().SingleAsync();
        Assert.Equal(association.Id, selection.AssociationId); Assert.Equal(association.LatestReviewId, selection.ReviewId);
        Assert.True((await Submit(secondKey, reviewed.Etag!, caseVersion: withdrawn.RowVersion, evidence: [association.Id])).Replayed);
        await proofs.WithdrawAsync(f.Underwriter, capacity.DraftId, capacity.CycleId, association.Id, Version(second.Etag!), lease,
            association.RowVersion, "Withdraw fictional carrier proof", Key(), Guid.NewGuid());
        Assert.Equal("servicing-capacity-evidence-unavailable", (await Assert.ThrowsAsync<QuoteOperationException>(() =>
            Submit(secondKey, reviewed.Etag!, caseVersion: withdrawn.RowVersion, evidence: [association.Id]))).Code);
        var now = f.Clock.Current; f.Clock.Current = now.AddMinutes(6);
        Assert.Equal("servicing-lease-conflict", (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(key, etag))).Code); f.Clock.Current = now;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional capacity authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Submit(key, etag))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Act(withdrawKey, actionVersion, updated.RowVersion))).Status);
        Assert.Equal(2, await db.Set<ServicingCapacitySubmission>().CountAsync());
    }
}
