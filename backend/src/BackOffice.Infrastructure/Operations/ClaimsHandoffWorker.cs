using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed record ClaimsProviderOutcome(Guid OperationId,string EventId,string State,DateTimeOffset CompletedAt,ClaimsAdministratorSummary? Summary);
public sealed class ClaimsWorkerException(JobFailure failure):Exception("Demo claims operation did not complete."){public JobFailure Failure {get;}=failure;}
public sealed partial class ClaimsHandoffWorker(IDbContextFactory<BackOfficeDbContext> factory,IncidentOccurrenceResolver resolver,FileService files,TimeProvider time)
{
    public async Task<ClaimsProviderOutcome?> ExecuteProvider(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=ClaimsHandoffService.WorkKind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var request=await db.Set<ClaimsRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        var handoff=await db.Set<ClaimsHandoff>().AsNoTracking().SingleAsync(x=>x.Id==request.HandoffId,token);
        await using var transaction=await db.Database.BeginTransactionAsync(token);ClaimsSnapshot snapshot;
        try{snapshot=await ClaimsAuthority.HoldSender(db,request,handoff,resolver,token);}
        catch(Exception e)when(e is OperationalAccessException or QuoteOperationException){throw Failure(JobFailure.Superseded);}
        foreach(var evidence in snapshot.Evidence)
        {
            var file=await(from c in db.Set<DocumentVersionContent>() join f in db.Set<FileObject>() on c.FileObjectId equals f.Id where c.VersionId==evidence.VersionId select f).AsNoTracking().SingleAsync(token);
            try{await using var stream=await files.Open(db,file,token);}
            catch(OperationalFileStoreException){throw Failure(JobFailure.InvalidPayload);}
        }
        var work=await SqlJobLeases.OwnedAsync(db,lease,time.GetUtcNow(),token);if(work is null)return null;
        if(!ClaimsAuthority.Matches(request,handoff,work,lease)||request.Purpose=="handoff"&&handoff.State!="queued"||request.Purpose!="handoff"&&handoff.State!="acknowledged")throw Failure(JobFailure.ProviderConflict);
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==request.ScenarioVersionId,token);var scenario=OperationalClaimsSeed.Scenario(setting)??throw Failure(JobFailure.InvalidPayload);
        var result=await Provider(request,handoff,lease,scenario,token);await transaction.CommitAsync(token);return result;
    }
    private async Task<ClaimsProviderOutcome> Provider(ClaimsRequest request,ClaimsHandoff handoff,JobLease lease,string scenario,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var operation=await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed=operation is not null;var hash=Convert.FromHexString(request.PayloadHash);
        if(operation is not null&&(operation.ScenarioVersionId!=request.ScenarioVersionId||!CryptographicOperations.FixedTimeEquals(operation.RequestHash,hash)))throw Failure(JobFailure.ProviderConflict);
        if(operation?.Result is not null){var saved=Read(operation.Result);Validate(operation,saved,request,handoff,time.GetUtcNow());await tx.CommitAsync(token);return saved;}
        if(operation is null){operation=new DemoProviderOperation{Kind=lease.Kind,OperationKey=lease.OperationKey,ScenarioVersionId=request.ScenarioVersionId,RequestHash=hash,CreatedAt=time.GetUtcNow()};db.Add(operation);}
        if(!existed&&scenario=="transient-once"||scenario=="retry-required"&&lease.Attempt<=6){operation.State="transient-failed";await db.SaveChangesAsync(token);await tx.CommitAsync(token);throw Failure(JobFailure.ProviderUnavailable);}
        var now=time.GetUtcNow();var rejected=scenario=="reject";var eventId=$"operational-claims/{operation.Id:N}";
        ClaimsAdministratorSummary? summary=request.Purpose=="contact"?null:new(handoff.ProviderReference??$"CLM-DEMO-{handoff.Id:N}",eventId,now,rejected?"rejected":request.Purpose=="handoff"?"notified":"open",null,null);
        // Explicit fictional scenario only. Default and retained unknown amounts stay unknown.
        if(summary is not null&&scenario=="summary-details")summary=summary with{Status="open",Paid="0.00",Reserved="6500.00",
            Liability="Not yet determined by administrator",Incurred="6500.00",RecoveryExpected="Recovery enquiries pending",
            ExcessApplied="750.00",MovementNote="Security footage requested"};
        var outcome=new ClaimsProviderOutcome(operation.Id,eventId,rejected?"rejected":"acknowledged",now,summary);
        operation.State=rejected?"rejected":"succeeded";operation.CompletedAt=now;operation.Result=ClaimsSnapshots.Serialize(outcome);
        Validate(operation,outcome,request,handoff,now);await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        if(!existed&&scenario=="timeout-after-success")throw Failure(JobFailure.ProviderTimeout);return outcome;
    }
    private static void Validate(DemoProviderOperation operation,ClaimsProviderOutcome value,ClaimsRequest request,ClaimsHandoff handoff,DateTimeOffset now)
    {
        if(value.OperationId!=operation.Id||value.EventId!=$"operational-claims/{operation.Id:N}"||value.CompletedAt!=operation.CompletedAt||value.CompletedAt<request.CreatedAt||value.CompletedAt>now||
            value.State is not("acknowledged" or "rejected")||operation.State!=(value.State=="acknowledged"?"succeeded":"rejected")||
            request.Purpose=="contact"&&value.Summary is not null||request.Purpose!="contact"&&value.Summary is null)throw Failure(JobFailure.ProviderConflict);
        if(value.Summary is{} summary)
        {
            try{ClaimsRules.Summary(summary,handoff.CreatedAt,value.CompletedAt);}catch(ClaimsRuleException){throw Failure(JobFailure.ProviderConflict);}
            if(summary.EventId!=value.EventId||summary.ProviderReference!=(handoff.ProviderReference??$"CLM-DEMO-{handoff.Id:N}")||
                value.State=="rejected"&&summary.Status!="rejected"||value.State=="acknowledged"&&summary.Status=="rejected")throw Failure(JobFailure.ProviderConflict);
        }
    }
    private static ClaimsProviderOutcome Read(string json)
    {try{return JsonSerializer.Deserialize<ClaimsProviderOutcome>(json,ClaimsSnapshots.Json)??throw Failure(JobFailure.ProviderConflict);}catch(JsonException){throw Failure(JobFailure.ProviderConflict);}}
    private static ClaimsWorkerException Failure(JobFailure failure)=>new(failure);
}
