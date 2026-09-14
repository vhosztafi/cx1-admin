using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyNotificationRetry(AgencyDraftService agencies,SqlCommandBoundary commands,TimeProvider time)
{
    public async Task<CommandOutcome> Execute(ActorContext actor,Guid agencyId,Guid notificationId,string key,byte[] version,string reason,CancellationToken token)
    {
        await agencies.Authorize(actor,agencyId,token);
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000||reason.Any(char.IsControl))throw new AgencyCommandException(422,"reason-required");
        return await commands.ExecuteAsync(new(actor.UserId,$"/api/v1/agencies/{agencyId}/notifications/{notificationId}/retry",key,Guid.NewGuid()),new{reason},"agency.notification-retry",async(db,ct)=>
        {
            var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={agencyId}").SingleAsync(ct);
            var notification=await db.Set<AgencyNotification>().SingleOrDefaultAsync(x=>x.Id==notificationId&&x.AgencyId==agencyId,ct)
                ??throw new AgencyCommandException(404,"notification-not-found");
            var job=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM [OutboxWork] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={notification.WorkId}").SingleAsync(ct);
            if(!CryptographicOperations.FixedTimeEquals(job.RowVersion,version))throw new AgencyCommandException(412,"stale-notification");
            var limit=JobRetryBudget.ExpandedLimit(job.State,job.ErrorCode,job.Attempts,job.AttemptLimit);
            var validPurpose=notification.Purpose=="agency-activated"||notification.Purpose=="agency-invitation"&&await InvitationService.CurrentForDelivery(db,notification,time.GetUtcNow(),ct);
            if(agency.State!="active"||!validPurpose||job.Kind!=AgencyNotificationService.Kind||job.SubjectRecordId!=agencyId||limit is null)
                throw new AgencyCommandException(409,"notification-not-retryable");
            job.AttemptLimit=limit.Value;job.State="pending";job.NextAttemptAt=time.GetUtcNow();job.CompletedAt=null;job.ErrorCode=null;job.LeaseToken=null;job.LeaseExpiresAt=null;
            db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,SubjectRecordId=notificationId,EventType="agency.notification-retry-reason",Reason=reason,OccurredAt=time.GetUtcNow(),After=JsonSerializer.Serialize(new{notificationId,attemptLimit=limit.Value})});
            db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actor.UserId,CreatedBy=actor.UserId,Action="agency.notification-retry",OccurredAt=time.GetUtcNow()});
            await db.SaveChangesAsync(ct);return new(notificationId,202,JsonSerializer.Serialize(new{id=notificationId}),Etag:AgencyDraftService.Etag(job.RowVersion));
        },token);
    }
}
