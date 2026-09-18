using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class RenewalLapseNotificationException(JobFailure failure):Exception("Fictional renewal notification could not complete.")
{ public JobFailure Failure { get; }=failure; }

// The persistent demo provider commits its receipt independently of application
// of the result. Restarting after Deliver cannot produce another notification.
public sealed class RenewalLapseNotificationWorker(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<Guid?> Deliver(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=RenewalLifecycleService.NotificationKind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var work=await SqlJobLeases.OwnedAsync(db,lease,time.GetUtcNow(),token);if(work is null)return null;
        var lapse=await Validate(db,lease,work,token);
        var prior=await db.Set<RenewalLapseNotificationReceipt>().AsNoTracking().SingleOrDefaultAsync(x=>x.LapseEventId==lapse.Id,token);
        if(prior is not null){await tx.CommitAsync(token);return prior.Id;}
        using var recipients=JsonDocument.Parse(lapse.RecipientSnapshotJson);
        var row=new RenewalLapseNotificationReceipt{LapseEventId=lapse.Id,WorkId=work.Id,PayloadHash=Hash(work.Payload),CreatedAt=time.GetUtcNow(),
            Outcome=recipients.RootElement.GetArrayLength()==0?"demo-no-recipient":"demo-delivered"};
        db.Add(row);await db.SaveChangesAsync(token);await tx.CommitAsync(token);return row.Id;
    }

    public async Task<bool> Apply(JobLease lease,Guid receiptId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var now=time.GetUtcNow();var work=await SqlJobLeases.OwnedAsync(db,lease,now,token);if(work is null)return false;
        var lapse=await Validate(db,lease,work,token);
        var receipt=await db.Set<RenewalLapseNotificationReceipt>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receiptId && x.LapseEventId==lapse.Id && x.WorkId==work.Id,token);
        if(receipt is null || !CryptographicOperations.FixedTimeEquals(receipt.PayloadHash,Hash(work.Payload)))throw Failure(JobFailure.ProviderConflict);
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id && x.AttemptNumber==lease.Attempt,token);
        attempt.EndedAt=now;attempt.Outcome=receipt.Outcome=="demo-delivered"?"succeeded":"rejected";
        attempt.Response=JsonSerializer.Serialize(new{lapseEventId=lapse.Id,receiptId=receipt.Id,outcome=receipt.Outcome});
        work.Result=attempt.Response;work.State="succeeded";work.CompletedAt=now;work.LeaseToken=null;work.LeaseExpiresAt=null;work.ErrorCode=null;
        if(receipt.Outcome!="demo-delivered")
        {attempt.ErrorCode=receipt.Outcome;await SqlJobLeases.MarkTerminalAsync(db,work,receipt.Outcome,now,token);}
        db.Add(new AuditEvent{SubjectRecordId=lapse.TermId,EventType="renewal.lapse-notification-completed",CorrelationId=work.CorrelationId,OccurredAt=now,CreatedAt=now,After=attempt.Response});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return true;
    }

    private static async Task<RenewalLapseEvent> Validate(BackOfficeDbContext db,JobLease lease,OutboxWork work,CancellationToken token)
    {
        var lapse=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==work.Id,token);
        if(lapse is null || work.Kind!=RenewalLifecycleService.NotificationKind || work.Kind!=lease.Kind || work.Payload!=lease.Payload || work.OperationKey!=lease.OperationKey ||
            work.OperationKey!=$"renewal-lapse/{lapse.TermId:N}" || work.ScenarioVersionId!=lease.ScenarioVersionId || work.ScenarioVersionId!=lapse.RuleSettingVersionId || work.SubjectRecordId!=lapse.Id)
            throw Failure(JobFailure.ProviderConflict);
        return lapse;
    }
    private static byte[] Hash(string value)=>SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static RenewalLapseNotificationException Failure(JobFailure failure)=>new(failure);
}
