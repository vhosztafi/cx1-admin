using System.Security.Cryptography;
using System.Text;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed partial class MidSubmissionWorker
{
    public async Task<InboxApplication> Apply(JobLease lease,MidProviderOutcome outcome,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);var sub=await db.Set<MidSubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        await using var tx=await db.Database.BeginTransactionAsync(token);var authorized=true;
        try{await MidAuthority.HoldSender(db,sub,token);}catch(Exception e)when(e is OperationalAccessException or QuoteOperationException){authorized=false;}
        var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={lease.WorkId}").SingleAsync(token);
        if(!await MidAuthority.Matches(db,sub,work,lease,token))throw Failure(JobFailure.ProviderConflict);
        var provider=await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==outcome.OperationId,token);
        if(provider is null||provider.Kind!=lease.Kind||provider.OperationKey!=lease.OperationKey||provider.ScenarioVersionId!=sub.ScenarioVersionId||provider.Result is null||!CryptographicOperations.FixedTimeEquals(provider.RequestHash,Convert.FromHexString(sub.RequestHash)))throw Failure(JobFailure.ProviderConflict);
        var saved=Read(provider.Result);Validate(provider,saved,sub,time.GetUtcNow());if(saved.EventId!=outcome.EventId)throw Failure(JobFailure.ProviderConflict);
        var json=MidSnapshots.Serialize(outcome);var hash=SHA256.HashData(Encoding.UTF8.GetBytes(json));const string name="operational-mid-demo";
        var inbox=await db.Set<AdapterInbox>().SingleOrDefaultAsync(x=>x.Provider==name&&x.EventId==outcome.EventId,token);
        if(inbox is not null)
        {
            if(inbox.WorkId!=work.Id)throw Failure(JobFailure.ProviderConflict);
            if(CryptographicOperations.FixedTimeEquals(inbox.ContentHash,hash)){await tx.CommitAsync(token);return InboxApplication.Duplicate;}
            if(!await db.Set<AdapterQuarantine>().AnyAsync(x=>x.InboxId==inbox.Id&&x.ObservedHash==hash,token))db.Add(new AdapterQuarantine{InboxId=inbox.Id,ObservedHash=hash,ReceivedAt=time.GetUtcNow(),Reason="MID result changed for a retained provider event."});
            await db.SaveChangesAsync(token);await tx.CommitAsync(token);return InboxApplication.Quarantined;
        }
        if(json!=provider.Result)throw Failure(JobFailure.ProviderConflict);
        var now=time.GetUtcNow();if(work.State!="leased"||work.LeaseToken!=lease.Token||work.Attempts!=lease.Attempt||work.LeaseExpiresAt is null||work.LeaseExpiresAt<=now)return InboxApplication.StaleLease;
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id&&x.AttemptNumber==lease.Attempt,token);
        attempt.EndedAt=now;attempt.Outcome=!authorized?"superseded":outcome.State=="rejected"?"rejected":"succeeded";attempt.Response=MidSnapshots.Serialize(new{submissionId=sub.Id,versionId=sub.VersionId,providerOutcome=outcome.State,applied=authorized});
        if(authorized&&outcome.State!="rejected")
        {work.State="succeeded";work.CompletedAt=now;work.ErrorCode=null;work.LeaseToken=null;work.LeaseExpiresAt=null;work.Result=MidSnapshots.Serialize(new{resourceId=sub.Id});}
        else{attempt.ErrorCode=!authorized?"mid-context-unavailable":"provider-rejected";await SqlJobLeases.MarkTerminalAsync(db,work,attempt.ErrorCode,now,token);}
        if(authorized)db.Add(new MidResult{SubmissionId=sub.Id,ProviderOperationId=provider.Id,ProviderEventId=outcome.EventId,ResultJson=json,ContentHash=MidSnapshots.Hash(json),CreatedAt=now,CreatedBy=sub.CreatedBy});
        db.Add(new AdapterInbox{Provider=name,EventId=outcome.EventId,ContentHash=hash,WorkId=work.Id,State="applied",AppliedAt=now});
        db.Add(new AuditEvent{ActorId=sub.CreatedBy,SubjectRecordId=sub.PolicyId,EventType="mid.result-applied",CorrelationId=work.CorrelationId,OccurredAt=now,After=attempt.Response});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return InboxApplication.Applied;
    }
}
