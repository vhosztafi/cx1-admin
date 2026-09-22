using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class CancellationOperationsWorker
{
    public async Task RecordNoticeUnavailable(Guid workId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={workId}").SingleOrDefaultAsync(token);
        var now=time.GetUtcNow();
        if(work is null || work.Kind!="cancellation-notice" || work.State!="pending" && !(work.State=="leased" && work.LeaseExpiresAt<=now))return;
        if(work.State=="leased")
        {
            var attempt=await db.Set<AdapterAttempt>().SingleOrDefaultAsync(x=>x.WorkId==work.Id&&x.AttemptNumber==work.Attempts&&x.EndedAt==null,token);
            if(attempt is not null){attempt.EndedAt=now;attempt.Outcome="rejected";attempt.ErrorCode="cancellation-context-unavailable";}
        }
        await SqlJobLeases.MarkTerminalAsync(db,work,"cancellation-context-unavailable",now,token);
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
    public async Task<bool> PrepareNotice(Guid workId, MessageDeliveryService deliveries, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var source = await CancellationOperationsAuthority.Hold(db, workId, token);
        if (source.Consequence.Kind != "notice") throw CancellationOperationsAuthority.Invalid();
        if (!CancellationOperationsRules.NeedsNoticeDelivery(await db.Set<CancellationNoticeReceipt>().AnyAsync(x => x.ConsequenceId == source.Consequence.Id, token))) return true;
        var prior = await db.Set<CancellationNoticeDispatch>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == source.Consequence.Id, token);
        if (prior is not null)
            return await db.Set<OperationalDelivery>().AnyAsync(x => x.Id == prior.DeliveryId && x.State != "queued", token);
        using var payload = JsonDocument.Parse(source.Consequence.PayloadJson);
        if (payload.RootElement.GetProperty("recipients").GetArrayLength() == 0) return true;
        var version = await db.Set<DocumentVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.CancellationConsequenceId == source.Consequence.Id, token);
        if (version is null || !await db.Set<DocumentVersionContent>().AnyAsync(x => x.VersionId == version.Id, token)) return false;
        var content = await CancellationNoticeDelivery.Capture(db, source.Actor, source.Consequence.Id, version.Id, token);
        // Serialize competing orchestration only after current parent/recipient/file checks.
        var work = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={workId}").SingleAsync(token);
        if (work.State != "pending" || await db.Set<CancellationNoticeReceipt>().AnyAsync(x => x.ConsequenceId == source.Consequence.Id, token)) return false;
        if (await db.Set<CancellationNoticeDispatch>().AnyAsync(x => x.ConsequenceId == source.Consequence.Id, token)) return false;
        await deliveries.Queue(db, source.Actor, content, null, null, token, async delivery =>
        {
            db.Add(new CancellationNoticeDispatch { ConsequenceId = source.Consequence.Id, DocumentVersionId = version.Id, DeliveryId = delivery.Id,
                PayloadHash = Convert.ToHexStringLower(source.Consequence.PayloadHash), CreatedBy = source.Actor.UserId, CreatedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(token);
        });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return false;
    }

    public async Task<bool> ApplyNotice(JobLease lease, CancellationToken token = default)
    {
        if (lease.Kind != "cancellation-notice") throw CancellationOperationsAuthority.Invalid();
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var source = await CancellationOperationsAuthority.Hold(db, lease.WorkId, token);
        var legacy = await db.Set<CancellationNoticeReceipt>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == source.Consequence.Id, token);
        var dispatch = await db.Set<CancellationNoticeDispatch>().AsNoTracking().SingleOrDefaultAsync(x => x.ConsequenceId == source.Consequence.Id, token);
        var delivery = dispatch is null ? null : await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x => x.Id == dispatch.DeliveryId, token);
        if (delivery is not null) await DeliveryAuthority.HoldSender(db, delivery, token);
        var now = time.GetUtcNow(); var work = await SqlJobLeases.OwnedAsync(db, lease, now, token);
        if (work is null) return false;
        if (!CancellationOperationsAuthority.Matches(source, work, lease)) throw CancellationOperationsAuthority.Invalid();
        using var payload = JsonDocument.Parse(source.Consequence.PayloadJson);
        var empty = payload.RootElement.GetProperty("recipients").GetArrayLength() == 0;
        if (legacy is null && !empty && (delivery is null || delivery.State == "queued")) return false;
        var outcome = legacy is not null ? "legacy-" + legacy.Outcome : empty ? "no-recipient" : delivery!.State == "delivered" ? "demo-delivered" : "delivery-failed";
        var result = JsonSerializer.Serialize(new { consequenceId = source.Consequence.Id, outcome, legacyReceiptId = legacy?.Id, deliveryId = delivery?.Id, documentVersionId = dispatch?.DocumentVersionId }, CommunicationScope.Json);
        db.Add(new CancellationOperationalReceipt { ConsequenceId = source.Consequence.Id, WorkId = work.Id, EffectiveAt = source.Decision.EffectiveAt, Outcome = outcome,
            PayloadHash = Convert.ToHexStringLower(source.Consequence.PayloadHash), ResultJson = result, CreatedBy = source.Actor.UserId, CreatedAt = now });
        var attempt = await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId == work.Id && x.AttemptNumber == lease.Attempt, token);
        attempt.EndedAt = now; attempt.Outcome = "succeeded"; attempt.Response = result;
        // Delivery failure belongs to the original child delivery and its existing exception task.
        // Completion here records orchestration, never asserts a failed message was delivered.
        work.State = "succeeded"; work.Result = result; work.CompletedAt = now; work.LeaseToken = null; work.LeaseExpiresAt = null; work.ErrorCode = null;
        if (empty && legacy is null) await SqlJobLeases.MarkTerminalAsync(db, work, "cancellation-no-recipient", now, token);
        db.Add(new AuditEvent { SubjectRecordId = source.Policy.Id, EventType = "policy.cancellation-notice-recorded", ActorId = source.Actor.UserId, CreatedBy = source.Actor.UserId,
            CreatedAt = now, OccurredAt = now, CorrelationId = work.CorrelationId, After = result });
        await db.SaveChangesAsync(token); await tx.CommitAsync(token); return true;
    }
}
