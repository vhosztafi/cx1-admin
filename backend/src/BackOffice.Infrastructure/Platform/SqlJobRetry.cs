using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Platform;

public sealed class JobRetryException(int status, string code) : Exception("The job could not be retried.")
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class SqlJobRetry
{
    // Called inside the audited command transaction, after current authorization/replay.
    public static async Task<OutboxWork> ApplyAsync(BackOfficeDbContext db, Guid jobId, byte[] expectedVersion,
        Guid actorId, Guid correlationId, string reason, DateTimeOffset now, CancellationToken token)
    {
        var job = await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM [OutboxWork] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={jobId}").SingleOrDefaultAsync(token);
        if (job is null || job.Kind != SqlJobLeases.DiagnosticKind) throw new JobRetryException(404, "job-not-found");
        if (!CryptographicOperations.FixedTimeEquals(job.RowVersion, expectedVersion)) throw new JobRetryException(412, "stale-job");
        var expanded = JobRetryBudget.ExpandedLimit(job.State, job.ErrorCode, job.Attempts, job.AttemptLimit);
        if (expanded is null) throw new JobRetryException(409, "job-not-retryable");
        job.AttemptLimit = expanded.Value; job.State = "pending"; job.NextAttemptAt = now;
        job.CompletedAt = null; job.ErrorCode = null; job.LeaseToken = null; job.LeaseExpiresAt = null;
        db.Add(new AuditEvent {ActorId = actorId, CreatedBy = actorId, SubjectRecordId = job.Id,
            EventType = "diagnostic.retry-authorized", Reason = reason, OccurredAt = now, CorrelationId = correlationId,
            After = JsonSerializer.Serialize(new {attemptLimit = expanded.Value})});
        return job;
    }
}
