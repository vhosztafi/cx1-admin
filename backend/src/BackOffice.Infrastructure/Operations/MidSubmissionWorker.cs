using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed record MidProviderOutcome(Guid OperationId,string EventId,Guid SubmissionId,Guid PolicyVersionId,string State,string? ProviderReference,string[] ReasonCodes,DateTimeOffset CompletedAt);
public sealed class MidWorkerException(JobFailure failure):Exception("Demo MID operation did not complete."){public JobFailure Failure{get;}=failure;}
public sealed partial class MidSubmissionWorker(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<MidProviderOutcome?> ExecuteProvider(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind is not("mid-update" or "cancellation-mid-removal"))throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);var sub=await db.Set<MidSubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        await using var tx=await db.Database.BeginTransactionAsync(token);
        try{await MidAuthority.HoldSender(db,sub,token);}catch(Exception e)when(e is OperationalAccessException or QuoteOperationException){throw Failure(JobFailure.Superseded);}
        var work=await SqlJobLeases.OwnedAsync(db,lease,time.GetUtcNow(),token);if(work is null)return null;
        if(!await MidAuthority.Matches(db,sub,work,lease,token))throw Failure(JobFailure.ProviderConflict);
        var snapshot=JsonSerializer.Deserialize<MidSnapshot>(sub.RequestJson,MidSnapshots.Json)??throw Failure(JobFailure.InvalidPayload);
        if(snapshot.Purpose=="cancellation"&&time.GetUtcNow()<snapshot.EffectiveAt)throw Failure(JobFailure.InvalidPayload);
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==sub.ScenarioVersionId,token);var scenario=OperationalMidSeed.Scenario(setting)??throw Failure(JobFailure.InvalidPayload);
        var outcome=await Provider(sub,snapshot,lease,scenario,token);await tx.CommitAsync(token);return outcome;
    }
    private async Task<MidProviderOutcome> Provider(MidSubmission sub,MidSnapshot snapshot,JobLease lease,string scenario,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var operation=await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed=operation is not null;var hash=Convert.FromHexString(sub.RequestHash);
        if(operation is not null&&(operation.ScenarioVersionId!=sub.ScenarioVersionId||!CryptographicOperations.FixedTimeEquals(operation.RequestHash,hash)))throw Failure(JobFailure.ProviderConflict);
        if(operation?.Result is not null){var saved=Read(operation.Result);Validate(operation,saved,sub,time.GetUtcNow());await tx.CommitAsync(token);return saved;}
        if(operation is null){operation=new DemoProviderOperation{Kind=lease.Kind,OperationKey=lease.OperationKey,ScenarioVersionId=sub.ScenarioVersionId,RequestHash=hash,CreatedAt=time.GetUtcNow()};db.Add(operation);}
        if(snapshot.Items.Count>0&&(!existed&&scenario=="transient-once"||scenario=="retry-required"&&lease.Attempt<=6))
        {operation.State="transient-failed";await db.SaveChangesAsync(token);await tx.CommitAsync(token);throw Failure(JobFailure.ProviderUnavailable);}
        var state=snapshot.Items.Count==0?"not-required":scenario=="reject"?"rejected":"accepted";
        var outcome=new MidProviderOutcome(operation.Id,$"operational-mid/{operation.Id:N}",sub.Id,sub.VersionId,state,state=="not-required"?null:$"MID-DEMO-{sub.Id:N}",state=="rejected"?["demo-provider-rejected"]:state=="not-required"?["no-reportable-changes"]:[],time.GetUtcNow());
        operation.State=state=="rejected"?"rejected":"succeeded";operation.CompletedAt=outcome.CompletedAt;operation.Result=MidSnapshots.Serialize(outcome);
        Validate(operation,outcome,sub,time.GetUtcNow());await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        if(!existed&&scenario=="timeout-after-success"&&state!="not-required")throw Failure(JobFailure.ProviderTimeout);return outcome;
    }
    private static MidProviderOutcome Read(string value){try{return JsonSerializer.Deserialize<MidProviderOutcome>(value,MidSnapshots.Json)??throw Failure(JobFailure.ProviderConflict);}catch(JsonException){throw Failure(JobFailure.ProviderConflict);}}
    private static void Validate(DemoProviderOperation op,MidProviderOutcome value,MidSubmission sub,DateTimeOffset now)
    {
        var snapshot=JsonSerializer.Deserialize<MidSnapshot>(sub.RequestJson,MidSnapshots.Json)??throw Failure(JobFailure.InvalidPayload);
        if(value.OperationId!=op.Id||value.EventId!=$"operational-mid/{op.Id:N}"||value.SubmissionId!=sub.Id||value.PolicyVersionId!=sub.VersionId||value.CompletedAt!=op.CompletedAt||value.CompletedAt<sub.CreatedAt||value.CompletedAt>now||
            value.State is not("accepted" or "rejected" or "not-required")||op.State!=(value.State=="rejected"?"rejected":"succeeded")||
            (snapshot.Items.Count==0)!=(value.State=="not-required")||value.ProviderReference!=(value.State=="not-required"?null:$"MID-DEMO-{sub.Id:N}")||
            !value.ReasonCodes.SequenceEqual(value.State=="rejected"?["demo-provider-rejected"]:value.State=="not-required"?["no-reportable-changes"]:[]))throw Failure(JobFailure.ProviderConflict);
    }
    private static MidWorkerException Failure(JobFailure failure)=>new(failure);
}
