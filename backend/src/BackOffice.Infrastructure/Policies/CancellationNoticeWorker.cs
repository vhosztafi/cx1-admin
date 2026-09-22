using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class CancellationNoticeException(JobFailure failure):Exception("Demo cancellation notice could not complete.")
{public JobFailure Failure{get;}=failure;}

public sealed class CancellationNoticeWorker(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public const string Kind="cancellation-notice";
    public async Task<Guid?> Deliver(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=Kind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await CancellationOperationsAuthority.Hold(db,lease.WorkId,token);
        var work=await SqlJobLeases.OwnedAsync(db,lease,time.GetUtcNow(),token);if(work is null)return null;
        var intent=await Validate(db,lease,work,token);
        if(await db.Set<CancellationNoticeDispatch>().AnyAsync(x=>x.ConsequenceId==intent.Id,token))throw Failure(JobFailure.ProviderConflict);
        var prior=await db.Set<CancellationNoticeReceipt>().AsNoTracking().SingleOrDefaultAsync(x=>x.ConsequenceId==intent.Id,token);
        if(prior is not null){await tx.CommitAsync(token);return prior.Id;}
        using var payload=JsonDocument.Parse(intent.PayloadJson);
        if(!payload.RootElement.TryGetProperty("recipients",out var recipients)||recipients.ValueKind!=JsonValueKind.Array)throw Failure(JobFailure.InvalidPayload);
        var receipt=new CancellationNoticeReceipt{ConsequenceId=intent.Id,WorkId=work.Id,PayloadHash=Hash(work.Payload),CreatedAt=time.GetUtcNow(),
            Outcome=recipients.GetArrayLength()==0?"demo-no-recipient":"demo-delivered"};
        db.Add(receipt);await db.SaveChangesAsync(token);await tx.CommitAsync(token);return receipt.Id;
    }
    public async Task<bool> Apply(JobLease lease,Guid receiptId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        await CancellationOperationsAuthority.Hold(db,lease.WorkId,token);
        var now=time.GetUtcNow();var work=await SqlJobLeases.OwnedAsync(db,lease,now,token);if(work is null)return false;
        var intent=await Validate(db,lease,work,token);
        var receipt=await db.Set<CancellationNoticeReceipt>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==receiptId&&x.ConsequenceId==intent.Id&&x.WorkId==work.Id,token);
        if(receipt is null||!CryptographicOperations.FixedTimeEquals(receipt.PayloadHash,Hash(work.Payload)))throw Failure(JobFailure.ProviderConflict);
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id&&x.AttemptNumber==lease.Attempt,token);
        attempt.EndedAt=now;attempt.Outcome=receipt.Outcome=="demo-delivered"?"succeeded":"rejected";
        attempt.Response=JsonSerializer.Serialize(new{consequenceId=intent.Id,receiptId=receipt.Id,outcome=receipt.Outcome});
        work.Result=attempt.Response;work.State="succeeded";work.CompletedAt=now;work.LeaseToken=null;work.LeaseExpiresAt=null;work.ErrorCode=null;
        if(receipt.Outcome!="demo-delivered"){attempt.ErrorCode=receipt.Outcome;await SqlJobLeases.MarkTerminalAsync(db,work,receipt.Outcome,now,token);}
        db.Add(new AuditEvent{SubjectRecordId=intent.PolicyId,EventType="policy.cancellation-notice-completed",CorrelationId=work.CorrelationId,
            OccurredAt=now,CreatedAt=now,After=attempt.Response});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return true;
    }
    private static async Task<CancellationConsequence> Validate(BackOfficeDbContext db,JobLease lease,OutboxWork work,CancellationToken token)
    {
        var intent=await db.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==work.Id,token);
        if(intent is null||intent.Kind!="notice"||work.Kind!=Kind||lease.Kind!=Kind||work.Payload!=lease.Payload||work.Payload!=intent.PayloadJson||
            work.OperationKey!=lease.OperationKey||work.OperationKey!=$"cancellation-notice/{intent.TransactionId:N}"||work.SubjectRecordId!=intent.Id||work.ScenarioVersionId!=lease.ScenarioVersionId)
            throw Failure(JobFailure.ProviderConflict);
        var valid=await(from transaction in db.Set<PolicyTransaction>() join decision in db.Set<CancellationIssueDecision>() on transaction.CancellationIssueDecisionId equals decision.Id
            join draft in db.Set<ServicingDraft>() on decision.DraftId equals draft.Id join preview in db.Set<CancellationPreview>() on decision.PreviewId equals preview.Id
            where transaction.Id==intent.TransactionId&&transaction.Kind=="cancellation"&&decision.Id==intent.DecisionId&&draft.State=="issued"&&draft.IssuedTransactionId==transaction.Id
                &&preview.RuleSettingVersionId==work.ScenarioVersionId select transaction.Id).AnyAsync(token);
        if(!valid||!CryptographicOperations.FixedTimeEquals(intent.PayloadHash,Hash(work.Payload)))throw Failure(JobFailure.ProviderConflict);
        return intent;
    }
    private static byte[] Hash(string value)=>SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static CancellationNoticeException Failure(JobFailure failure)=>new(failure);
}
