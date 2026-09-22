using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class MessageDeliveryWorker
{
    public async Task<InboxApplication> Apply(JobLease lease,OperationalDeliveryOutcome outcome,CancellationToken token=default)
    {
        if(lease.Kind!=MessageDeliveryService.WorkKind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var hint=await db.Set<OperationalDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        await using var transaction=await db.Database.BeginTransactionAsync(token);
        var authorized=true;
        try{await DeliveryAuthority.HoldSender(db,hint,token);}
        catch(Exception error)when(error is OperationalAccessException or QuoteOperationException){authorized=false;}
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={lease.WorkId}").SingleAsync(token);
        if(!DeliveryAuthority.MatchesWork(hint,work,lease))throw Failure(JobFailure.ProviderConflict);
        var provider=await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==outcome.OperationId,token);
        if(provider is null||provider.Kind!=lease.Kind||provider.OperationKey!=lease.OperationKey||provider.ScenarioVersionId!=lease.ScenarioVersionId||
            provider.Result is null||provider.State is not("succeeded" or "rejected")||!CryptographicOperations.FixedTimeEquals(provider.RequestHash,Convert.FromHexString(hint.ContentHash)))throw Failure(JobFailure.ProviderConflict);
        var retained=ReadProviderResult(provider.Result);
        if(retained.OperationId!=provider.Id||retained.EventId!=$"operational-delivery/{provider.Id:N}"||outcome.EventId!=retained.EventId||retained.CompletedAt!=provider.CompletedAt||
            retained.CompletedAt<hint.CreatedAt||retained.CompletedAt>time.GetUtcNow()||retained.State is not("delivered" or "rejected")||provider.State!=(retained.State=="delivered"?"succeeded":"rejected"))throw Failure(JobFailure.ProviderConflict);
        var serialized=JsonSerializer.Serialize(outcome,CommunicationScope.Json);var hash=SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(serialized));
        const string providerName="operational-delivery-demo";
        var existing=await db.Set<AdapterInbox>().SingleOrDefaultAsync(x=>x.Provider==providerName&&x.EventId==outcome.EventId,token);
        if(existing is not null)
        {
            if(existing.WorkId!=work.Id)throw Failure(JobFailure.ProviderConflict);
            if(CryptographicOperations.FixedTimeEquals(existing.ContentHash,hash)){await transaction.CommitAsync(token);return InboxApplication.Duplicate;}
            if(!await db.Set<AdapterQuarantine>().AnyAsync(x=>x.InboxId==existing.Id&&x.ObservedHash==hash,token))
                db.Add(new AdapterQuarantine{InboxId=existing.Id,ObservedHash=hash,ReceivedAt=time.GetUtcNow(),Reason="Delivery result changed for a retained provider event."});
            db.Add(new AuditEvent{ActorId=hint.CreatedBy,SubjectRecordId=hint.SubjectId,EventType="communication.delivery-quarantined",CorrelationId=work.CorrelationId,OccurredAt=time.GetUtcNow(),After=JsonSerializer.Serialize(new{deliveryId=hint.Id})});
            await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return InboxApplication.Quarantined;
        }
        if(serialized!=provider.Result)throw Failure(JobFailure.ProviderConflict);
        var now=time.GetUtcNow();
        if(work.State!="leased"||work.LeaseToken!=lease.Token||work.Attempts!=lease.Attempt||work.LeaseExpiresAt is null||work.LeaseExpiresAt<=now)return InboxApplication.StaleLease;
        var delivery=await db.Set<OperationalDelivery>().SingleAsync(x=>x.Id==hint.Id,token);
        if(delivery.State!="queued")throw Failure(JobFailure.ProviderConflict);
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id&&x.AttemptNumber==lease.Attempt,token);
        delivery.ProviderOperationId=provider.Id;delivery.AttemptId=attempt.Id;delivery.CompletedAt=now;
        attempt.EndedAt=now;attempt.Outcome=!authorized?"superseded":outcome.State=="delivered"?"succeeded":"rejected";
        attempt.Response=JsonSerializer.Serialize(new{deliveryId=delivery.Id,providerOutcome=outcome.State,applied=authorized},CommunicationScope.Json);
        if(authorized&&outcome.State=="delivered")
        {delivery.State="delivered";delivery.OutcomeCode=null;work.State="succeeded";work.CompletedAt=now;work.ErrorCode=null;work.LeaseToken=null;work.LeaseExpiresAt=null;work.Result=JsonSerializer.Serialize(new{resourceId=delivery.Id});}
        else
        {attempt.ErrorCode=!authorized?"delivery-context-unavailable":"provider-rejected";await SqlJobLeases.MarkTerminalAsync(db,work,attempt.ErrorCode,now,token);}
        db.Add(new AdapterInbox{Provider=providerName,EventId=outcome.EventId,ContentHash=hash,WorkId=work.Id,State="applied",AppliedAt=now});
        await DeliverySnapshots.UpdateMessageState(db,delivery,token);
        db.Add(new AuditEvent{ActorId=delivery.CreatedBy,SubjectRecordId=delivery.SubjectId,EventType="communication.delivery-applied",CorrelationId=work.CorrelationId,OccurredAt=now,After=attempt.Response});
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return InboxApplication.Applied;
    }
}
