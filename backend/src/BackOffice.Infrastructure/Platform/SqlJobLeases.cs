using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Platform;

public sealed record JobLease(Guid WorkId,Guid Token,int Attempt,string Kind,string OperationKey,Guid ScenarioVersionId,string Payload);
public enum JobFailure { ProviderUnavailable, ProviderTimeout, ProviderRejected, InvalidPayload, ProviderConflict }

public sealed class SqlJobLeases(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public const string DiagnosticKind="diagnostic-probe";
    public static readonly TimeSpan LeaseDuration=TimeSpan.FromSeconds(30);

    public async Task<JobLease?> ClaimAsync(CancellationToken cancellationToken = default)
    {
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        // REPEATABLE READ permits READPAST even when the database uses read-committed snapshots.
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,cancellationToken);
        var now=time.GetUtcNow();
        var job=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT TOP (1) * FROM [OutboxWork] WITH (UPDLOCK,READPAST,ROWLOCK) WHERE [Kind]={DiagnosticKind} AND (([State]='pending' AND [NextAttemptAt]<={now}) OR ([State]='leased' AND [LeaseExpiresAt]<={now})) ORDER BY [NextAttemptAt],[CreatedAt],[Id]")
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null) return null;
        if (job.State=="leased")
        {
            var abandoned=await db.Set<AdapterAttempt>().SingleOrDefaultAsync(x => x.WorkId==job.Id && x.AttemptNumber==job.Attempts && x.EndedAt==null,cancellationToken);
            if (abandoned is not null) {abandoned.EndedAt=now;abandoned.Outcome="lease-expired";abandoned.ErrorCode="lease-expired";}
        }
        if (job.Attempts>=RetrySchedule.MaximumAttempts)
        {
            await MarkTerminalAsync(db,job,"attempts-exhausted",now,cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return null;
        }
        job.State="leased"; job.LeaseToken=Guid.NewGuid(); job.LeaseExpiresAt=now+LeaseDuration; job.Attempts++;
        db.Add(new AdapterAttempt {WorkId=job.Id,AttemptNumber=job.Attempts,StartedAt=now,Outcome="started",
            Request=JsonSerializer.Serialize(new {kind=job.Kind,operationKey=job.OperationKey})});
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new JobLease(job.Id,job.LeaseToken.Value,job.Attempts,job.Kind,job.OperationKey,job.ScenarioVersionId!.Value,job.Payload);
    }

    public async Task<bool> FailAsync(JobLease lease,JobFailure failure,CancellationToken cancellationToken = default)
    {
        var code=failure switch
        {
            JobFailure.ProviderUnavailable => "provider-unavailable",
            JobFailure.ProviderTimeout => "provider-timeout",
            JobFailure.ProviderRejected => "provider-rejected",
            JobFailure.InvalidPayload => "invalid-payload",
            JobFailure.ProviderConflict => "provider-conflict",
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        var transient=failure is JobFailure.ProviderUnavailable or JobFailure.ProviderTimeout;
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
        var now=time.GetUtcNow();
        var job=await OwnedAsync(db,lease,now,cancellationToken);
        if (job is null) return false;
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x => x.WorkId==job.Id && x.AttemptNumber==lease.Attempt,cancellationToken);
        attempt.EndedAt=now; attempt.Outcome=transient ? "transient-failure" : "rejected"; attempt.ErrorCode=code;
        job.ErrorCode=code;
        if (!transient || job.Attempts>=RetrySchedule.MaximumAttempts) await MarkTerminalAsync(db,job,code,now,cancellationToken);
        else
        {
            job.State="pending"; job.NextAttemptAt=now+RetrySchedule.AfterFailure(job.Attempts,job.OperationKey);
            job.LeaseToken=null; job.LeaseExpiresAt=null;
        }
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return true;
    }

    // Lock and fence every final state change, including provider-result application.
    internal static Task<OutboxWork?> OwnedAsync(BackOfficeDbContext db,JobLease lease,DateTimeOffset now,CancellationToken cancellationToken) =>
        db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM [OutboxWork] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={lease.WorkId} AND [State]='leased' AND [LeaseToken]={lease.Token} AND [LeaseExpiresAt]>{now} AND [Attempts]={lease.Attempt}")
            .SingleOrDefaultAsync(cancellationToken);

    internal static async Task MarkTerminalAsync(BackOfficeDbContext db,OutboxWork job,string code,DateTimeOffset now,CancellationToken cancellationToken)
    {
        job.State="failed"; job.CompletedAt=now; job.ErrorCode=code; job.LeaseToken=null; job.LeaseExpiresAt=null;
        if (!await db.Set<JobException>().AnyAsync(x => x.WorkId==job.Id,cancellationToken))
            db.Add(new JobException {WorkId=job.Id,Code=code,OccurredAt=now});
    }
}
