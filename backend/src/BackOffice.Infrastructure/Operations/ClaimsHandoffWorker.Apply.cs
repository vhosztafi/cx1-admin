using System.Security.Cryptography;
using System.Text;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed partial class ClaimsHandoffWorker
{
    public async Task<InboxApplication> Apply(JobLease lease,ClaimsProviderOutcome outcome,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var request=await db.Set<ClaimsRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        var hint=await db.Set<ClaimsHandoff>().AsNoTracking().SingleAsync(x=>x.Id==request.HandoffId,token);
        await using var tx=await db.Database.BeginTransactionAsync(token);var authorized=true;
        try{await ClaimsAuthority.HoldSender(db,request,hint,resolver,token);}catch(Exception e)when(e is OperationalAccessException or QuoteOperationException){authorized=false;}
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={lease.WorkId}").SingleAsync(token);
        if(!ClaimsAuthority.Matches(request,hint,work,lease))throw Failure(JobFailure.ProviderConflict);
        var provider=await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==outcome.OperationId,token);
        if(provider is null||provider.Kind!=lease.Kind||provider.OperationKey!=lease.OperationKey||provider.ScenarioVersionId!=request.ScenarioVersionId||provider.Result is null||
            !CryptographicOperations.FixedTimeEquals(provider.RequestHash,Convert.FromHexString(request.PayloadHash)))throw Failure(JobFailure.ProviderConflict);
        var saved=Read(provider.Result);Validate(provider,saved,request,hint,time.GetUtcNow());
        if(saved.EventId!=outcome.EventId)throw Failure(JobFailure.ProviderConflict);
        var serialized=ClaimsSnapshots.Serialize(outcome);var hash=SHA256.HashData(Encoding.UTF8.GetBytes(serialized));const string name="operational-claims-demo";
        var inbox=await db.Set<AdapterInbox>().SingleOrDefaultAsync(x=>x.Provider==name&&x.EventId==outcome.EventId,token);
        if(inbox is not null)
        {
            if(inbox.WorkId!=work.Id)throw Failure(JobFailure.ProviderConflict);
            if(CryptographicOperations.FixedTimeEquals(inbox.ContentHash,hash)){await tx.CommitAsync(token);return InboxApplication.Duplicate;}
            if(!await db.Set<AdapterQuarantine>().AnyAsync(x=>x.InboxId==inbox.Id&&x.ObservedHash==hash,token))db.Add(new AdapterQuarantine{InboxId=inbox.Id,ObservedHash=hash,ReceivedAt=time.GetUtcNow(),Reason="Claims result changed for a retained provider event."});
            db.Add(new AuditEvent{ActorId=request.CreatedBy,SubjectRecordId=hint.PolicyId,EventType="claims.result-quarantined",CorrelationId=work.CorrelationId,OccurredAt=time.GetUtcNow(),After=ClaimsSnapshots.Serialize(new{handoffId=hint.Id,requestId=request.Id})});
            await db.SaveChangesAsync(token);await tx.CommitAsync(token);return InboxApplication.Quarantined;
        }
        if(serialized!=provider.Result)throw Failure(JobFailure.ProviderConflict);
        var now=time.GetUtcNow();if(work.State!="leased"||work.LeaseToken!=lease.Token||work.Attempts!=lease.Attempt||work.LeaseExpiresAt is null||work.LeaseExpiresAt<=now)return InboxApplication.StaleLease;
        var handoff=await db.Set<ClaimsHandoff>().SingleAsync(x=>x.Id==hint.Id,token);
        var incident=await db.Set<OperationalIncident>().SingleAsync(x=>x.Id==hint.IncidentId,token);
        if(request.Purpose=="handoff"&&(handoff.State!="queued"||incident.CurrentRevisionId!=handoff.RevisionId||incident.CurrentResolutionId!=handoff.ResolutionId))throw Failure(JobFailure.ProviderConflict);
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id&&x.AttemptNumber==lease.Attempt,token);
        attempt.EndedAt=now;attempt.Outcome=!authorized?"superseded":outcome.State=="acknowledged"?"succeeded":"rejected";
        attempt.Response=ClaimsSnapshots.Serialize(new{handoffId=handoff.Id,requestId=request.Id,providerOutcome=outcome.State,applied=authorized});
        if(authorized&&outcome.State=="acknowledged")
        {
            work.State="succeeded";work.CompletedAt=now;work.ErrorCode=null;work.LeaseToken=null;work.LeaseExpiresAt=null;work.Result=ClaimsSnapshots.Serialize(new{resourceId=handoff.Id});
            if(request.Purpose=="handoff"){handoff.State="acknowledged";handoff.ProviderReference=outcome.Summary!.ProviderReference;handoff.CompletedAt=now;handoff.OutcomeCode=null;incident.State="handed-off";incident.UpdatedAt=now;}
        }
        else
        {
            attempt.ErrorCode=!authorized?"claims-context-unavailable":"provider-rejected";
            await SqlJobLeases.MarkTerminalAsync(db,work,attempt.ErrorCode,now,token);
        }
        if(authorized&&outcome.Summary is{} summary)
        {
            var json=ClaimsSnapshots.Serialize(summary);db.Add(new ClaimsSummary{HandoffId=handoff.Id,RequestId=request.Id,ProviderOperationId=provider.Id,ProviderEventId=summary.EventId,
                ContentHash=DeliverySnapshots.Hash(json),SummaryJson=json,AsOf=summary.AsOf,ReceivedAt=now,CreatedAt=now,CreatedBy=request.CreatedBy});
            if(request.Purpose=="handoff"&&outcome.State=="rejected")handoff.ProviderReference=summary.ProviderReference;
        }
        db.Add(new AdapterInbox{Provider=name,EventId=outcome.EventId,ContentHash=hash,WorkId=work.Id,State="applied",AppliedAt=now});
        db.Add(new AuditEvent{ActorId=request.CreatedBy,SubjectRecordId=hint.PolicyId,EventType="claims.result-applied",CorrelationId=work.CorrelationId,OccurredAt=now,After=attempt.Response});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return InboxApplication.Applied;
    }
}
