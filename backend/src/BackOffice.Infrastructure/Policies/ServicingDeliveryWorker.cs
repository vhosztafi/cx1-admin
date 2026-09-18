using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingDeliveryOutcome(Guid OperationId,string State,DateTimeOffset CompletedAt);
public sealed class ServicingDeliveryException(JobFailure failure):Exception("Fictional servicing delivery did not complete.")
{public JobFailure Failure{get;}=failure;}

public sealed class ServicingDeliveryWorker(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<ServicingDeliveryOutcome> ExecuteProviderAsync(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=ServicingTermsService.WorkKind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        if(lease.OperationKey!=$"servicing-delivery/{delivery.Id:N}" || lease.ScenarioVersionId!=delivery.ScenarioVersionId)throw Failure(JobFailure.ProviderConflict);
        var scenario=ServicingTermsSeed.Scenario(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==delivery.ScenarioVersionId,token))??throw Failure(JobFailure.InvalidPayload);
        var hash=Convert.FromHexString(delivery.PayloadHash);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var operation=await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed=operation is not null;
        if(operation is not null && (operation.ScenarioVersionId!=lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash,hash)))throw Failure(JobFailure.ProviderConflict);
        if(operation?.Result is not null)
        {var saved=JsonSerializer.Deserialize<ServicingDeliveryOutcome>(operation.Result,ServicingRatingService.Json)!;await tx.CommitAsync(token);return saved;}
        if(operation is null)
        {operation=new DemoProviderOperation{Kind=lease.Kind,OperationKey=lease.OperationKey,RequestHash=hash,ScenarioVersionId=lease.ScenarioVersionId,CreatedAt=time.GetUtcNow()};db.Add(operation);}
        if(!existed && scenario=="transient-once")
        {operation.State="transient-failed";await db.SaveChangesAsync(token);await tx.CommitAsync(token);throw Failure(JobFailure.ProviderUnavailable);}
        var outcome=new ServicingDeliveryOutcome(operation.Id,scenario=="reject"?"rejected":"delivered",time.GetUtcNow());
        operation.State=outcome.State=="delivered"?"succeeded":"rejected";operation.CompletedAt=outcome.CompletedAt;operation.Result=JsonSerializer.Serialize(outcome,ServicingRatingService.Json);
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        if(!existed && scenario=="timeout-after-success")throw Failure(JobFailure.ProviderTimeout);
        return outcome;
    }

    public async Task<bool> ApplyAsync(JobLease lease,ServicingDeliveryOutcome outcome,CancellationToken token=default)
    {
        if(lease.Kind!=ServicingTermsService.WorkKind)throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var hint=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        var cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==hint.CycleId,token);
        var sourceId=await db.Set<Policy>().Where(x=>x.Id==cycle.PolicyId).Select(x=>x.SourceQuoteId).SingleAsync(token);
        var agencyId=await db.Set<Quote>().Where(x=>x.Id==sourceId).Select(x=>x.AgencyId).SingleAsync(token);
        var reference=await IdentitySnapshot.Reference(db,hint.SentBy,token);
        await using var tx=await db.Database.BeginTransactionAsync(token);
        await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK) WHERE Id={agencyId}").AsNoTracking().SingleAsync(token);
        var identity=reference is null?null:await IdentitySnapshot.Lock(db,reference,token);
        await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK) WHERE Id={sourceId}").AsNoTracking().SingleAsync(token);
        await db.Set<Policy>().FromSqlInterpolated($"SELECT * FROM Policy WITH(UPDLOCK,HOLDLOCK) WHERE Id={cycle.PolicyId}").AsNoTracking().SingleAsync(token);
        await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(UPDLOCK,HOLDLOCK) WHERE Id={cycle.BaseTermId}").AsNoTracking().SingleAsync(token);
        var draft=await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(UPDLOCK,HOLDLOCK) WHERE Id={cycle.DraftId}").SingleAsync(token);
        var now=time.GetUtcNow();var work=await SqlJobLeases.OwnedAsync(db,lease,now,token);if(work is null)return false;
        var operation=await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==outcome.OperationId && x.Kind==lease.Kind && x.OperationKey==lease.OperationKey,token);
        if(work.SubjectRecordId!=hint.Id || work.ScenarioVersionId!=hint.ScenarioVersionId || lease.ScenarioVersionId!=hint.ScenarioVersionId ||
            lease.OperationKey!=$"servicing-delivery/{hint.Id:N}" || operation is null || operation.ScenarioVersionId!=hint.ScenarioVersionId ||
            operation.Result!=JsonSerializer.Serialize(outcome,ServicingRatingService.Json) || outcome.State is not("delivered" or "rejected") ||
            outcome.CompletedAt< hint.CreatedAt || outcome.CompletedAt>now || operation.CompletedAt!=outcome.CompletedAt ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash,Convert.FromHexString(hint.PayloadHash)))throw Failure(JobFailure.ProviderConflict);
        var delivery=await db.Set<ServicingTermsDelivery>().SingleAsync(x=>x.Id==hint.Id,token);
        if(delivery.State!="queued")return false;
        ServicingDecisionContext? held=null;
        if(identity is not null && draft.State=="draft" && draft.CurrentCycleId==cycle.Id && draft.CurrentRevisionId==cycle.RevisionId)
        {
            try
            {
                var actor=new ActorContext(identity.User.Id,identity.User.TeamId,identity.User.AgencyId,identity.Roles.Select(x=>x.Code).ToHashSet(StringComparer.Ordinal));
                held=await ServicingDecisionContext.Hold(db,actor,draft.Id,"policy-draft-write",now,token,cycle.Id);
                var terms=await ServicingTermsService.CurrentTerms(db,held,delivery.TermsVersionId,now,token);
                var incoming=new ServicingTermsSubject(delivery.DraftId,delivery.CycleId,delivery.RevisionId,terms.BaseVersionId,delivery.RatingId,terms.Id,terms.TermsHash);
                var current=new ServicingTermsSubject(draft.Id,held.Cycle.Id,held.Cycle.RevisionId,held.Cycle.BaseVersionId,held.Rating.Id,terms.Id,terms.TermsHash);
                if(!ServicingTermsRules.CanApplyDelivery(incoming,current,delivery.Id,held.Cycle.CurrentDeliveryId,delivery.State,now,held.Rating.ExpiresAt))
                    throw new QuoteOperationException(409,"servicing-delivery-stale");
                await new ServicingTermsService(factory,time).Ready(db,held,now,true,token);
                var recipients=JsonSerializer.Deserialize<ServicingTermsRecipient[]>(delivery.RecipientSnapshotJson,ServicingRatingService.Json)!;
                var currentRecipients=await ServicingTermsService.Recipients(db,held,recipients.Select(x=>x.Id).ToArray(),token);
                if(!recipients.SequenceEqual(currentRecipients) ||
                    delivery.AssuranceHashAtSend!=await ServicingTermsService.Assurance(db,held,token))throw new QuoteOperationException(409,"servicing-delivery-stale");
            }
            catch(QuoteOperationException){held=null;}
        }
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id && x.AttemptNumber==lease.Attempt,token);
        delivery.State=held is null?"superseded":outcome.State=="delivered"?"delivered":"failed";
        delivery.CompletedAt=now;delivery.ProviderOperationId=operation.Id;delivery.AttemptId=attempt.Id;
        delivery.OutcomeCode=held is null?"servicing-delivery-stale":outcome.State=="delivered"?null:"provider-rejected";delivery.UpdatedAt=now;
        draft.UpdatedAt=now;db.Entry(draft).Property(x=>x.UpdatedAt).IsModified=true;
        attempt.EndedAt=now;attempt.Outcome=held is null?"superseded":outcome.State=="delivered"?"succeeded":"rejected";
        attempt.Response=JsonSerializer.Serialize(new{deliveryId=delivery.Id,state=delivery.State});
        work.State="succeeded";work.CompletedAt=now;work.Result=attempt.Response;work.LeaseToken=null;work.LeaseExpiresAt=null;work.ErrorCode=null;
        if(outcome.State=="rejected")await SqlJobLeases.MarkTerminalAsync(db,work,"provider-rejected",now,token);
        db.Add(new AuditEvent{ActorId=delivery.SentBy,EventType="servicing.delivery-completed",SubjectRecordId=draft.Id,CorrelationId=work.CorrelationId,OccurredAt=now,After=attempt.Response});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return true;
    }

    private static ServicingDeliveryException Failure(JobFailure failure)=>new(failure);
}
