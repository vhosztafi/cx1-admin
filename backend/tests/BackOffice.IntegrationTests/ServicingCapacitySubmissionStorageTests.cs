using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacitySubmissionStorage(BackOfficeDbContext db, DecisionFixture f, ServicingCapacityCase capacity, Guid lease, bool selectedEvidence = false)
    {
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacitySubmission").SingleAsync());
        var scenario = await db.Set<SettingVersion>().AsNoTracking().FirstAsync(x => x.Scope == "capacity-escalation/query-proof");
        var now = f.Clock.GetUtcNow();
        ServicingEvidenceAssociation? proof = null;
        if (selectedEvidence)
        {
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacitySubmissionEvidence").SingleAsync());
            var service = new ServicingEvidenceService(f.Factory, f.Clock);
            var draft = await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x => x.Id == capacity.DraftId);
            var uploaded = await service.UploadAsync(f.Underwriter, draft.Id, draft.RowVersion, lease, "fictional-proof.txt", "text/plain",
                Encoding.UTF8.GetBytes("Fictional motor trade evidence for a capacity request"), Guid.NewGuid().ToString(), Guid.NewGuid());
            var requirement = (await service.RequirementsAsync(f.Underwriter, draft.Id)).Requirements.Single(x => x.Requirement.Code == "motor-trader-proof").Requirement;
            var attached = await service.AttachAsync(f.Underwriter, draft.Id, capacity.CycleId, Convert.FromBase64String(uploaded.Etag!.Trim('"')), lease,
                uploaded.ResourceId, requirement.Code, requirement.RiskItemId, requirement.InputFingerprint, "Attach fictional capacity proof", Guid.NewGuid().ToString(), Guid.NewGuid());
            proof = await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
            await service.ReviewAsync(f.Underwriter, draft.Id, capacity.CycleId, proof.Id, Convert.FromBase64String(attached.Etag!.Trim('"')), lease,
                proof.RowVersion, "accepted", proof.InputFingerprint, "Review fictional capacity proof", Guid.NewGuid().ToString(), Guid.NewGuid());
            proof = await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == attached.ResourceId);
        }
        async Task<(Guid Id, Guid Work, string Json, byte[] Hash)> Prepare(int sequence)
        {
            var id = Guid.NewGuid();
            var context = JsonSerializer.Serialize(new { format = "servicing-capacity-submission-1", draftId = capacity.DraftId,
                revisionId = capacity.RevisionId, cycleId = capacity.CycleId, ratingId = capacity.RatingId, caseId = capacity.Id,
                referralId = capacity.ReferralId, providerId = capacity.ProviderId, binderVersionId = capacity.BinderVersionId,
                submissionId = id, sequence, evidence = sequence == 1 && proof is not null
                    ? new[] { new { associationId = proof.Id, reviewId = proof.LatestReviewId!.Value } } : [] });
            var work = new OutboxWork { Kind = "servicing-capacity", SubjectRecordId = id, OperationKey = $"servicing-capacity/{id:N}",
                ScenarioVersionId = scenario.Id, Payload = "{}", NextAttemptAt = now, CreatedAt = now, UpdatedAt = now, CreatedBy = f.Underwriter.UserId };
            db.Add(work); await db.SaveChangesAsync();
            return (id, work.Id, context, SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        }
        async Task Insert((Guid Id, Guid Work, string Json, byte[] Hash) item, int sequence, Guid? draft = null, Guid? owner = null, byte[]? hash = null, string body = "Fictional carrier request") =>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCapacitySubmission (Id,CaseId,DraftId,RevisionId,CycleId,RatingId,Sequence,Body,Reason,ContextJson,ContextHash,WorkId,ScenarioVersionId,SubmittedBy,SubmittedAt,ResponseDueAt,CreatedAt,CreatedBy) VALUES ({item.Id},{owner ?? capacity.Id},{draft ?? capacity.DraftId},{capacity.RevisionId},{capacity.CycleId},{capacity.RatingId},{sequence},{body},'Review fictional carrier request',{item.Json},{hash ?? item.Hash},{item.Work},{scenario.Id},{f.Underwriter.UserId},{now},{now.AddDays(2)},{now},{f.Underwriter.UserId})");
        var first = await Prepare(1);
        await Assert.ThrowsAsync<SqlException>(() => Insert(first, 1, draft: Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(() => Insert(first, 1, owner: Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(() => Insert(first, 1, hash: new byte[32]));
        await Assert.ThrowsAsync<SqlException>(() => Insert(first, 1, body: new string('x', 10001)));
        Assert.Equal(51430, (await Assert.ThrowsAsync<SqlException>(() => Insert(first, 2))).Number);
        await Insert(first, 1);
        if (proof is not null)
        {
            async Task Select(Guid association, Guid review, Guid? draft = null) =>
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCapacitySubmissionEvidence (Id,SubmissionId,CaseId,CycleId,DraftId,RevisionId,RatingId,AssociationId,ReviewId,CreatedAt,CreatedBy) VALUES ({Guid.NewGuid()},{first.Id},{capacity.Id},{capacity.CycleId},{draft ?? capacity.DraftId},{capacity.RevisionId},{capacity.RatingId},{association},{review},{now},{f.Underwriter.UserId})");
            Assert.Equal(51442, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentSubmissionId={first.Id},State='queued' WHERE Id={capacity.Id}"))).Number);
            await Assert.ThrowsAsync<SqlException>(() => Select(proof.Id, proof.LatestReviewId!.Value, Guid.NewGuid()));
            await Assert.ThrowsAsync<SqlException>(() => Select(Guid.NewGuid(), proof.LatestReviewId!.Value));
            await Assert.ThrowsAsync<SqlException>(() => Select(proof.Id, Guid.NewGuid()));
            await Select(proof.Id, proof.LatestReviewId!.Value);
            await Assert.ThrowsAsync<SqlException>(() => Select(proof.Id, proof.LatestReviewId!.Value));
            Assert.Equal(51441, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingCapacitySubmissionEvidence WHERE SubmissionId={first.Id}"))).Number);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentSubmissionId={first.Id},State='queued' WHERE Id={capacity.Id}");
        Assert.Equal(51431, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacitySubmission SET Body='Changed request' WHERE Id={first.Id}"))).Number);
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingCapacitySubmission WHERE Id={first.Id}"));
        // Withdrawal preserves the pointer, but cannot be undone by a delayed worker.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='draft' WHERE Id={capacity.Id}");
        Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='queued' WHERE Id={capacity.Id}"))).Number);
        var second = await Prepare(2); await Insert(second, 2);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentSubmissionId={second.Id},State='queued' WHERE Id={capacity.Id}");
        Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentSubmissionId={first.Id} WHERE Id={capacity.Id}"))).Number);
        Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentSubmissionId=NULL WHERE Id={capacity.Id}"))).Number);
        Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='approved' WHERE Id={capacity.Id}"))).Number);
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacitySubmission").SingleAsync());
    }
}
