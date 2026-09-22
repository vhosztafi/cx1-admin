using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record OperationalDeliveryOutcome(Guid OperationId,string EventId,string State,DateTimeOffset CompletedAt);
public sealed class MessageDeliveryException(JobFailure failure):Exception("Demo delivery did not complete.")
{
    public JobFailure Failure {get;}=failure;
}
public sealed partial class MessageDeliveryWorker(IDbContextFactory<BackOfficeDbContext> factory,FileService files,TimeProvider time)
{
    public async Task<OperationalDeliveryOutcome?> ExecuteProvider(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=MessageDeliveryService.WorkKind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var delivery=await db.Set<OperationalDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        await using var authority=await db.Database.BeginTransactionAsync(token);
        DeliveryContent content;
        try{content=await DeliveryAuthority.HoldSender(db,delivery,token);}
        catch(Exception error)when(error is OperationalAccessException or QuoteOperationException){throw Failure(JobFailure.Superseded);}
        // Validate actual bytes while original file and sender authority is held.
        foreach(var selected in content.Attachments)
        {
            var row=await db.Set<FileObject>().AsNoTracking().SingleAsync(x=>x.Id==selected.FileId,token);
            try{await using var verified=await files.Open(db,row,token);}
            catch(OperationalFileStoreException){throw Failure(JobFailure.InvalidPayload);}
        }
        var work=await SqlJobLeases.OwnedAsync(db,lease,time.GetUtcNow(),token);if(work is null)return null;
        if(delivery.State!="queued"||!DeliveryAuthority.MatchesWork(delivery,work,lease))throw Failure(JobFailure.ProviderConflict);
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==delivery.ScenarioVersionId,token);
        var scenario=OperationalDeliverySeed.Scenario(setting)??throw Failure(JobFailure.InvalidPayload);
        // A separate durable provider transaction deliberately survives a crash or
        // rollback in the local application transaction, like a remote effect.
        var outcome=await Provider(delivery,lease,scenario,token);
        await authority.CommitAsync(token);return outcome;
    }
    private async Task<OperationalDeliveryOutcome> Provider(OperationalDelivery delivery,JobLease lease,string scenario,CancellationToken token)
    {
        await using var provider=await factory.CreateDbContextAsync(token);await using var transaction=await provider.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var operation=await provider.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed=operation is not null;var hash=Convert.FromHexString(delivery.ContentHash);
        if(operation is not null&&(operation.ScenarioVersionId!=delivery.ScenarioVersionId||!CryptographicOperations.FixedTimeEquals(operation.RequestHash,hash)))throw Failure(JobFailure.ProviderConflict);
        if(operation?.Result is not null)
        {
            var saved=ReadProviderResult(operation.Result);
            if(saved.OperationId!=operation.Id||saved.EventId!=$"operational-delivery/{operation.Id:N}"||saved.State is not("delivered" or "rejected")||saved.CompletedAt!=operation.CompletedAt||operation.State!=(saved.State=="delivered"?"succeeded":"rejected"))
                throw Failure(JobFailure.ProviderConflict);
            await transaction.CommitAsync(token);return saved;
        }
        if(operation is null){operation=new DemoProviderOperation{Kind=lease.Kind,OperationKey=lease.OperationKey,ScenarioVersionId=delivery.ScenarioVersionId,RequestHash=hash,CreatedAt=time.GetUtcNow()};provider.Add(operation);}
        if((!existed&&scenario=="transient-once")||(scenario=="retry-required"&&lease.Attempt<=6))
        {operation.State="transient-failed";await provider.SaveChangesAsync(token);await transaction.CommitAsync(token);throw Failure(JobFailure.ProviderUnavailable);}
        var outcome=new OperationalDeliveryOutcome(operation.Id,$"operational-delivery/{operation.Id:N}",scenario=="reject"?"rejected":"delivered",time.GetUtcNow());
        operation.State=outcome.State=="delivered"?"succeeded":"rejected";operation.CompletedAt=outcome.CompletedAt;operation.Result=JsonSerializer.Serialize(outcome,CommunicationScope.Json);
        await provider.SaveChangesAsync(token);await transaction.CommitAsync(token);
        if(!existed&&scenario=="timeout-after-success")throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }
    private static MessageDeliveryException Failure(JobFailure failure)=>new(failure);
    private static OperationalDeliveryOutcome ReadProviderResult(string json)
    {
        try{return JsonSerializer.Deserialize<OperationalDeliveryOutcome>(json,CommunicationScope.Json)??throw Failure(JobFailure.ProviderConflict);}
        catch(JsonException){throw Failure(JobFailure.ProviderConflict);}
    }
}
