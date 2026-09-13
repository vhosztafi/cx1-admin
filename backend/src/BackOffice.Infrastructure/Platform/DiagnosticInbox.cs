using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Platform;

public enum InboxApplication { Applied, Duplicate, StaleLease, Quarantined }

public sealed class DiagnosticInbox(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private const string Provider="diagnostic-demo";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    public async Task<InboxApplication> ApplyAsync(JobLease lease,DiagnosticReply reply,CancellationToken cancellationToken = default)
    {
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize completion/duplicates with claims and failures for this same work record.
        var job=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM [OutboxWork] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={lease.WorkId}")
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null || job.Kind!=lease.Kind || job.OperationKey!=lease.OperationKey || job.ScenarioVersionId!=lease.ScenarioVersionId)
            throw new DiagnosticProviderException(JobFailure.ProviderConflict);
        var provider=await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id==reply.OperationId,cancellationToken);
        if (provider is null || provider.Kind!=job.Kind || provider.OperationKey!=job.OperationKey || provider.ScenarioVersionId!=job.ScenarioVersionId || provider.State is not ("succeeded" or "rejected"))
            throw new DiagnosticProviderException(JobFailure.ProviderConflict);
        var stored=JsonSerializer.Deserialize<DiagnosticReply>(provider.Result!,Json) ?? throw new InvalidOperationException("Stored provider result is invalid.");
        if (reply.EventId!=stored.EventId) throw new DiagnosticProviderException(JobFailure.ProviderConflict);
        var hash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(reply,Json));
        var existing=await db.Set<AdapterInbox>().SingleOrDefaultAsync(x => x.Provider==Provider && x.EventId==reply.EventId,cancellationToken);
        var now=time.GetUtcNow();
        if (existing is not null)
        {
            if (existing.WorkId!=job.Id) throw new DiagnosticProviderException(JobFailure.ProviderConflict);
            if (CryptographicOperations.FixedTimeEquals(existing.ContentHash,hash))
            {
                await transaction.CommitAsync(cancellationToken); return InboxApplication.Duplicate;
            }
            if (!await db.Set<AdapterQuarantine>().AnyAsync(x => x.InboxId==existing.Id && x.ObservedHash==hash,cancellationToken))
            {
                db.Add(new AdapterQuarantine {InboxId=existing.Id,ObservedHash=hash,ReceivedAt=now,Reason="Duplicate provider event has different content."});
                db.Add(new AuditEvent {ActorId=job.CreatedBy,EventType="diagnostic.callback-quarantined",OccurredAt=now,
                    CorrelationId=job.CorrelationId,After=JsonSerializer.Serialize(new {jobId=job.Id})});
            }
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return InboxApplication.Quarantined;
        }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(stored,Json)),hash))
            throw new DiagnosticProviderException(JobFailure.ProviderConflict);
        if (job.State!="leased" || job.LeaseToken!=lease.Token || job.Attempts!=lease.Attempt || job.LeaseExpiresAt is null || job.LeaseExpiresAt<=now)
            return InboxApplication.StaleLease;
        db.Add(new AdapterInbox {Provider=Provider,EventId=reply.EventId,ContentHash=hash,WorkId=job.Id,State="applied",AppliedAt=now});
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId==job.Id && x.AttemptNumber==lease.Attempt,cancellationToken);
        attempt.EndedAt=now; attempt.Outcome=reply.Accepted ? "succeeded" : "rejected"; attempt.Response=JsonSerializer.Serialize(reply,Json);
        if (reply.Accepted)
        {
            var receipt=new DiagnosticReceipt {WorkId=job.Id,ProviderOperationId=reply.OperationId,Reference=reply.Reference,CompletedAt=reply.CompletedAt};
            db.Add(receipt); job.Result=JsonSerializer.Serialize(new {resourceId=receipt.Id},Json);
            job.State="succeeded"; job.CompletedAt=now; job.ErrorCode=null; job.LeaseToken=null; job.LeaseExpiresAt=null;
        }
        else
        {
            attempt.ErrorCode="provider-rejected";
            await SqlJobLeases.MarkTerminalAsync(db,job,"provider-rejected",now,cancellationToken);
        }
        db.Add(new AuditEvent {ActorId=job.CreatedBy,EventType=reply.Accepted ? "diagnostic.completed" : "diagnostic.rejected",
            OccurredAt=now,CorrelationId=job.CorrelationId,After=JsonSerializer.Serialize(new {jobId=job.Id})});
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return InboxApplication.Applied;
    }
}
