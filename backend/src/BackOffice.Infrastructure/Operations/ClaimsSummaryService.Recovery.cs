using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed partial class ClaimsSummaryService
{
    public async Task<(OutboxWork Work,bool RetryAllowed)> Job(ActorContext actor,Guid jobId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var joined=await(from request in db.Set<ClaimsRequest>() join handoff in db.Set<ClaimsHandoff>() on request.HandoffId equals handoff.Id where request.WorkId==jobId select new{request,handoff}).AsNoTracking().SingleOrDefaultAsync(token)??throw ClaimsSnapshots.Missing();
        await HoldRead(db,actor,joined.handoff.IncidentId,token);var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==jobId,token);
        await tx.CommitAsync(token);return(work,JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit)is not null);
    }
    public async Task<CommandOutcome> Retry(ActorContext actor,Guid incidentId,Guid requestId,string etag,string reason,string key,CancellationToken token)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000)throw new ClaimsRuleException("claims-retry-reason-required");
        ClaimsRequest? request=null;ClaimsHandoff? hint=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/incidents/{incidentId}/requests/{requestId}/retry",key,Guid.NewGuid()),new{etag,reason},"claims.retry-requested",
            async(db,ct)=>
            {
                request=await db.Set<ClaimsRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId,ct)??throw ClaimsSnapshots.Missing();
                hint=await db.Set<ClaimsHandoff>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.HandoffId&&x.IncidentId==incidentId,ct)??throw ClaimsSnapshots.Missing();
                await ClaimsAuthority.Hold(db,actor,hint,resolver,ct);
                // An authorized colleague cannot revive work whose actual original sender was revoked.
                await ClaimsAuthority.HoldSender(db,request,hint,resolver,ct);
            },async(db,ct)=>
            {
                var work=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM OutboxWork WITH(UPDLOCK,ROWLOCK) WHERE Id={request!.WorkId}").SingleAsync(ct);
                if(TaskService.Etag(work.RowVersion)!=etag)throw new OperationalAccessException(412,"stale-claims-request");
                var expanded=JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit);
                if(expanded is null)throw new OperationalAccessException(409,"claims-request-not-retryable");
                var handoff=await db.Set<ClaimsHandoff>().SingleAsync(x=>x.Id==hint!.Id,ct);
                if(request!.Purpose=="handoff")
                {
                    if(handoff.State!="failed")throw new OperationalAccessException(409,"claims-request-not-retryable");
                    handoff.State="queued";handoff.OutcomeCode=null;handoff.CompletedAt=null;
                    var incident=await db.Set<OperationalIncident>().SingleAsync(x=>x.Id==incidentId,ct);incident.State="queued";incident.UpdatedAt=time.GetUtcNow();
                }
                work.AttemptLimit=expanded.Value;work.State="pending";work.NextAttemptAt=time.GetUtcNow();work.ErrorCode=null;work.CompletedAt=null;work.LeaseToken=null;work.LeaseExpiresAt=null;
                db.Add(new AuditEvent{ActorId=actor.UserId,SubjectRecordId=handoff.PolicyId,EventType="claims.retry-reason",Reason=reason,OccurredAt=time.GetUtcNow(),After=ClaimsSnapshots.Serialize(new{requestId})});
                await db.SaveChangesAsync(ct);return MessageDeliveryService.JobOutcome(work);
            },token);
    }
}
