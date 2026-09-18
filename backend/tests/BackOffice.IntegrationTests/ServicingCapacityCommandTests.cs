using System.Text.Json;
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
    private static async Task VerifyServicingCapacityCreate(BackOfficeDbContext db, DecisionFixture f, ServicingCycle cycle, string etag, bool submissionStorage = false, bool selectedEvidence = false, bool submitCommand = false,string? workerScenario=null,string? apiPassword=null)
    {
        static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
        static string Key() => Guid.NewGuid().ToString();
        var takeover = await new ServicingDraftService(f.Factory, f.Clock).LeaseAsync(f.Underwriter, cycle.DraftId,
            Version(etag), "takeover", null, "Review fictional capacity escalation", Key(), Guid.NewGuid());
        var lease = JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var rule=workerScenario=="capacity-worker-conditional"?"cover-stock-custody":"cover-tools-equipment";
        var referral = await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x => x.CycleId == cycle.Id && x.RuleCode == rule);
        if(apiPassword is not null) { await VerifyServicingCapacityHttp(db,f,apiPassword,cycle,referral,lease,takeover.Etag!);return; }
        var service = new ServicingCapacityService(f.Factory, f.Clock); var key = Key();
        const string reason = "Request fictional tools capacity exception";
        Task<CommandOutcome> Create(string operation, string version, ActorContext? actor = null, Guid? owner = null,
            Guid? ownerCycle = null, Guid? referralId = null, Guid? fence = null, byte[]? referralVersion = null, string why = reason) =>
            service.CreateAsync(actor ?? f.Underwriter, owner ?? cycle.DraftId, ownerCycle ?? cycle.Id, referralId ?? referral.Id,
                Version(version), referralVersion ?? referral.RowVersion, fence ?? lease, why, operation, Guid.NewGuid());
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!, actor: f.Servicing))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!, owner: Guid.NewGuid()))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!, ownerCycle: Guid.NewGuid()))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!, referralId: Guid.NewGuid()))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), "\"AAAAAAAAAAA=\""))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!, referralVersion: new byte[8]))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!, fence: Guid.NewGuid()))).Status);
        var binder = await db.Set<BinderVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.BinderVersionId);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityProvider SET State='inactive' WHERE Id={binder.ProviderId}");
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), takeover.Etag!))).Status);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE CapacityProvider SET State='active' WHERE Id={binder.ProviderId}");
        var created = await Create(key, takeover.Etag!);
        Assert.Equal(201, created.Status); Assert.False(created.Replayed);
        Assert.NotEqual(takeover.Etag, created.Etag);
        var row = await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync();
        Assert.Equal(created.ResourceId, row.Id); Assert.Equal(cycle.Id, row.CycleId); Assert.Equal(cycle.RevisionId, row.RevisionId);
        Assert.Equal(cycle.CurrentRatingId, row.RatingId); Assert.Equal(binder.ProviderId, row.ProviderId);
        Assert.Equal(referral.Id, row.ReferralId); Assert.Equal(f.Underwriter.UserId, row.RaisedBy); Assert.Equal("draft", row.State);
        var replay = await Create(key, takeover.Etag!); Assert.True(replay.Replayed); Assert.Equal(created.Body, replay.Body);
        if (submissionStorage) { await VerifyServicingCapacitySubmissionStorage(db, f, row, lease, selectedEvidence); return; }
        if (workerScenario is not null) { await VerifyServicingCapacityWorker(db,f,row,lease,created.Etag!,workerScenario); return; }
        if (submitCommand) { await VerifyServicingCapacitySubmit(db, f, row, lease, created.Etag!); return; }
        Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(Key(), created.Etag!))).Status);
        await Assert.ThrowsAsync<CommandKeyConflictException>(() => Create(key, takeover.Etag!, why: "Changed request under original key"));
        Assert.Single(await db.Set<AuditEvent>().Where(x => x.EventType == "servicing.capacity-created").ToArrayAsync());
        var now = f.Clock.Current; f.Clock.Current = now.AddMinutes(6);
        Assert.Equal("servicing-lease-conflict", (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(key, takeover.Etag!))).Code);
        f.Clock.Current = now;
        var roles = await db.Set<UserRole>().AsNoTracking().Where(x => x.UserId == f.Underwriter.UserId).ToArrayAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Underwriter.UserId}");
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(key, takeover.Etag!))).Status);
        db.ChangeTracker.Clear(); db.AddRange(roles); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Withdraw fictional capacity authority' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => Create(key, takeover.Etag!))).Status);
        Assert.Single(await db.Set<ServicingCapacityCase>().AsNoTracking().ToArrayAsync());
    }
}
