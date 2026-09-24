using System.Security.Cryptography;
using System.Data;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Platform;

public sealed record CommandIdentity(Guid ActorId,string Route,string Key,Guid CorrelationId);
public sealed record CommandOutcome(Guid ResourceId,int Status,string Body,bool Replayed = false,string? Etag = null);
public sealed class CommandKeyConflictException : Exception
{
    public CommandKeyConflictException() : base("This command key was already used with different input.") { }
}
public sealed class CommandBusyException : Exception
{
    public CommandBusyException() : base("This command is still being processed. Retry with the same key.") { }
}

/// <summary>
/// For non-secret successful command DTOs only. Handlers perform authorization before entering,
/// and concurrency/prerequisite checks inside the handler (after replay resolution).
/// They must write effects/outbox through the supplied context and make no external calls.
/// </summary>
public sealed class SqlCommandBoundary(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public Task<CommandOutcome> ExecuteAsync<TRequest>(CommandIdentity identity,TRequest request,string eventType,
        Func<BackOfficeDbContext,CancellationToken,Task<CommandOutcome>> handler,CancellationToken cancellationToken = default)
        => ExecuteCore(identity,request,eventType,null,handler,cancellationToken,IsolationLevel.ReadCommitted);

    // Authorization runs in the same transaction before any receipt lookup, for
    // both a new command and replay. Its locks remain held through commit.
    public Task<CommandOutcome> ExecuteAuthorizedAsync<TRequest>(CommandIdentity identity,TRequest request,string eventType,
        Func<BackOfficeDbContext,CancellationToken,Task> authorize,
        Func<BackOfficeDbContext,CancellationToken,Task<CommandOutcome>> handler,CancellationToken cancellationToken = default,
        IsolationLevel isolation = IsolationLevel.ReadCommitted)
        => ExecuteCore(identity,request,eventType,authorize ?? throw new ArgumentNullException(nameof(authorize)),handler,cancellationToken,isolation);

    private async Task<CommandOutcome> ExecuteCore<TRequest>(CommandIdentity identity,TRequest request,string eventType,
        Func<BackOfficeDbContext,CancellationToken,Task>? authorize,
        Func<BackOfficeDbContext,CancellationToken,Task<CommandOutcome>> handler,CancellationToken cancellationToken,IsolationLevel isolation)
    {
        if (identity.ActorId == Guid.Empty || identity.CorrelationId == Guid.Empty || string.IsNullOrWhiteSpace(identity.Key) || identity.Key.Length > 200 || identity.Key != identity.Key.Trim() ||
            string.IsNullOrWhiteSpace(identity.Route) || identity.Route.Length > 200 || string.IsNullOrWhiteSpace(eventType) || eventType.Length > 100)
            throw new ArgumentException("A bounded actor, route, command key, correlation and event type are required.");
        if (identity.Route.StartsWith("/api/v1/auth/",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Authentication responses must never enter the command result store.");
        var actorScope=identity.ActorId.ToString("N");
        // Serialize a typed, normalized command DTO: request bytes themselves are never retained.
        var requestHash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
        var lockKey="CoverMGA.Command."+Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] {actorScope,identity.Route,identity.Key})));
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction=await db.Database.BeginTransactionAsync(isolation,cancellationToken);
        await AcquireLockAsync(db,lockKey,cancellationToken);
        if (authorize is not null) await authorize(db,cancellationToken);
        var existing=await db.Set<IdempotencyRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ActorScope==actorScope && x.Route==identity.Route && x.Key==identity.Key,cancellationToken);
        if (existing is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash,requestHash)) throw new CommandKeyConflictException();
            // Receipts remain durable beyond cache expiry; expiry never permits a second effect.
            var replay=JsonSerializer.Deserialize<StoredOutcome>(existing.ResultBody) ?? throw new InvalidOperationException("Stored command result is invalid.");
            ValidateEtag(replay.Etag);
            await transaction.CommitAsync(cancellationToken);
            return new CommandOutcome(replay.ResourceId,existing.ResultStatus,replay.Body,true,replay.Etag);
        }
        var result=await handler(db,cancellationToken);
        if (result.Status is < 200 or > 299 || result.Replayed || result.ResourceId==Guid.Empty)
            throw new InvalidOperationException("Only successful command results may be stored.");
        ValidateEtag(result.Etag);
        using var document=JsonDocument.Parse(result.Body);
        if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            throw new InvalidOperationException("Command result must be a JSON object or array.");
        var now=time.GetUtcNow();
        db.Add(new AuditEvent {ActorId=identity.ActorId,CreatedBy=identity.ActorId,EventType=eventType,OccurredAt=now,
            CorrelationId=identity.CorrelationId,After=JsonSerializer.Serialize(new {resourceId=result.ResourceId})});
        db.Add(new IdempotencyRecord {ActorScope=actorScope,Route=identity.Route,Key=identity.Key,RequestHash=requestHash,
            ResultStatus=result.Status,ResultBody=JsonSerializer.Serialize(new StoredOutcome(result.ResourceId,result.Body,result.Etag)),ExpiresAt=now.AddDays(1),CreatedBy=identity.ActorId});
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static async Task AcquireLockAsync(BackOfficeDbContext db,string resource,CancellationToken cancellationToken)
    {
        await using var command=db.Database.GetDbConnection().CreateCommand();
        command.Transaction=db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; SELECT @result;";
        var parameter=command.CreateParameter(); parameter.ParameterName="@resource"; parameter.Value=resource; command.Parameters.Add(parameter);
        var result=Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0) throw new CommandBusyException();
    }

    private static void ValidateEtag(string? etag)
    {
        if (etag is null) return; // Receipts written before response version support remain readable.
        if (etag.Length is < 3 or > 100 || etag[0]!='"' || etag[^1]!='"' ||
            etag[1..^1].Any(c => c < 33 || c > 126 || c == '"'))
            throw new InvalidOperationException("Command ETag must be a bounded strong opaque ASCII token.");
    }

    private sealed record StoredOutcome(Guid ResourceId,string Body,string? Etag = null);
}
