using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Platform;

public sealed record DiagnosticProbe([property:JsonRequired] string Probe);
public sealed record DiagnosticScenario([property:JsonRequired] string Kind,[property:JsonRequired] string Scenario);
public sealed record DiagnosticReply(Guid OperationId,string EventId,bool Accepted,string Reference,DateTimeOffset CompletedAt);
public sealed class DiagnosticProviderException(JobFailure failure) : Exception("The diagnostic provider did not return a usable response.")
{
    public JobFailure Failure {get;}=failure;
}

/// <summary>A durable synthetic provider. It never calls an external service or performs a business payment.</summary>
public sealed class DiagnosticDemoProvider(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};

    public async Task<DiagnosticReply> ExecuteAsync(JobLease lease,CancellationToken cancellationToken = default)
    {
        DiagnosticProbe probe;
        try {probe=JsonSerializer.Deserialize<DiagnosticProbe>(lease.Payload,Json) ?? throw new JsonException();}
        catch (JsonException) {throw new DiagnosticProviderException(JobFailure.InvalidPayload);}
        if (lease.Kind!=SqlJobLeases.DiagnosticKind || probe.Probe!="foundation" || string.IsNullOrWhiteSpace(lease.OperationKey) || lease.OperationKey.Length>200)
            throw new DiagnosticProviderException(JobFailure.InvalidPayload);
        var requestHash=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {lease.Kind,lease.ScenarioVersionId,probe}));
        await using var db=await factory.CreateDbContextAsync(cancellationToken);
        // This context/transaction is intentionally independent from local job application.
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken);
        var operation=await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM [DemoProviderOperation] WITH (UPDLOCK,HOLDLOCK) WHERE [Kind]={lease.Kind} AND [OperationKey]={lease.OperationKey}")
            .SingleOrDefaultAsync(cancellationToken);
        if (operation is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(operation.RequestHash,requestHash)) throw new DiagnosticProviderException(JobFailure.ProviderConflict);
            if (operation.State is "succeeded" or "rejected")
            {
                var previous=JsonSerializer.Deserialize<DiagnosticReply>(operation.Result!,Json) ?? throw new InvalidOperationException("Stored provider result is invalid.");
                await transaction.CommitAsync(cancellationToken); return previous;
            }
            if (operation.State!="transient-failed") throw new InvalidOperationException("Stored provider operation state is invalid.");
            // A fail-once result was already committed. Recovery succeeds without consulting changed settings.
            var recovered=Complete(operation,true,time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return recovered;
        }
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id==lease.ScenarioVersionId,cancellationToken);
        DiagnosticScenario scenario;
        try {scenario=JsonSerializer.Deserialize<DiagnosticScenario>(setting?.Values ?? "null",Json) ?? throw new JsonException();}
        catch (JsonException) {throw new DiagnosticProviderException(JobFailure.InvalidPayload);}
        if (setting is null || setting.Scope!="diagnostic-probe/"+scenario.Scenario || scenario.Kind!=SqlJobLeases.DiagnosticKind ||
            scenario.Scenario is not ("success" or "reject" or "fail-once" or "timeout-after-success")) throw new DiagnosticProviderException(JobFailure.InvalidPayload);
        operation=new DemoProviderOperation {Kind=lease.Kind,OperationKey=lease.OperationKey,RequestHash=requestHash,ScenarioVersionId=lease.ScenarioVersionId};
        db.Add(operation);
        if (scenario.Scenario=="fail-once")
        {
            operation.State="transient-failed";
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            throw new DiagnosticProviderException(JobFailure.ProviderUnavailable);
        }
        var result=Complete(operation,scenario.Scenario!="reject",time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        // The provider result is committed before the local worker observes a timeout.
        if (scenario.Scenario=="timeout-after-success") throw new DiagnosticProviderException(JobFailure.ProviderTimeout);
        return result;
    }

    private static DiagnosticReply Complete(DemoProviderOperation operation,bool accepted,DateTimeOffset now)
    {
        var identity=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] {operation.Kind,operation.OperationKey}))).ToLowerInvariant();
        var reply=new DiagnosticReply(operation.Id,"diagnostic/"+identity,accepted,"DEMO-PROBE-"+identity,now);
        operation.State=accepted ? "succeeded" : "rejected"; operation.CompletedAt=now; operation.Result=JsonSerializer.Serialize(reply,Json);
        return reply;
    }
}
