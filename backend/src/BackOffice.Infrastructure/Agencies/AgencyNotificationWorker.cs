using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencyNotificationScenario([property:JsonRequired]bool Demo,[property:JsonRequired]string Scenario);
public sealed class AgencyNotificationProviderException(JobFailure failure):Exception("Demo agency delivery did not complete.")
{public JobFailure Failure {get;}=failure;}

// No network transport: this simulates a provider with independently committed receipts.
public sealed class AgencyNotificationWorker(IDbContextFactory<BackOfficeDbContext> factory,AgencyNotificationPayload payload,TimeProvider time)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};

    public static string Scenario(string values)
    {
        try
        {
            var setting=JsonSerializer.Deserialize<AgencyNotificationScenario>(values,Json);
            if(setting?.Demo!=true||setting.Scenario is not ("pass" or "reject" or "transient" or "unavailable" or "timeout-after-success"))throw new JsonException();
            return setting.Scenario;
        }
        catch(JsonException){throw new AgencyNotificationProviderException(JobFailure.InvalidPayload);}
    }

    public async Task<Guid?> Deliver(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=AgencyNotificationService.Kind)throw new AgencyNotificationProviderException(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        // Agency-first lock ordering matches future suspension/invitation revocation commands.
        var owner=await db.Set<AgencyNotification>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token);
        if(owner is null)throw new AgencyNotificationProviderException(JobFailure.InvalidPayload);
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={owner.AgencyId}").SingleAsync(token);
        var job=await SqlJobLeases.OwnedAsync(db,lease,time.GetUtcNow(),token);
        if(job is null)return null;
        if(job.Kind!=lease.Kind||job.OperationKey!=lease.OperationKey||job.ScenarioVersionId!=lease.ScenarioVersionId||job.SubjectRecordId!=owner.AgencyId||job.Payload!=lease.Payload)
            throw new AgencyNotificationProviderException(JobFailure.ProviderConflict);
        var prior=await db.Set<AgencyNotificationReceipt>().SingleOrDefaultAsync(x=>x.NotificationId==owner.Id,token);
        if(prior is not null){await tx.CommitAsync(token);return prior.Id;}
        if(owner.Purpose=="agency-invitation")
        {
            if(agency.State!="active"||!await InvitationService.CurrentForDelivery(db,owner,time.GetUtcNow(),token))
                throw new AgencyNotificationProviderException(JobFailure.Superseded);
        }
        else if(agency.State!="active"||owner.Purpose!="agency-activated")throw new AgencyNotificationProviderException(JobFailure.ProviderRejected);
        AgencyDeliveryEnvelope envelope;
        try{envelope=payload.Unprotect(owner.AgencyId,owner.Id,owner.ProtectedPayload);}
        catch(CryptographicException){throw new AgencyNotificationProviderException(JobFailure.InvalidPayload);}
        if(envelope.Template!=owner.Purpose||!CryptographicOperations.FixedTimeEquals(AgencyNotificationPayload.Fingerprint(envelope),owner.ContentHash))
            throw new AgencyNotificationProviderException(JobFailure.ProviderConflict);
        if(owner.InvitationId is Guid invitationId)
        {
            var hash=await db.Set<AgencyInvitation>().Where(x=>x.Id==invitationId).Select(x=>x.TokenHash).SingleAsync(token);
            if(hash is null||!InvitationToken.TryHash(envelope.Content,out var observed)||!CryptographicOperations.FixedTimeEquals(hash,observed))
                throw new AgencyNotificationProviderException(JobFailure.ProviderConflict);
        }
        var setting=await db.Set<SettingVersion>().SingleAsync(x=>x.Id==lease.ScenarioVersionId,token);
        if(setting.Scope!="agency-notification"&&!(owner.InvitationId is not null&&setting.Scope=="agency-invitation-delivery"))throw new AgencyNotificationProviderException(JobFailure.InvalidPayload);
        var scenario=Scenario(setting.Values);
        if(scenario=="unavailable"||(scenario=="transient"&&lease.Attempt==1))throw new AgencyNotificationProviderException(JobFailure.ProviderUnavailable);
        var receipt=new AgencyNotificationReceipt{NotificationId=owner.Id,AgencyId=owner.AgencyId,Accepted=scenario!="reject",
            ResultCode=scenario=="reject"?"demo-rejected":"demo-delivered",CompletedAt=time.GetUtcNow(),CreatedAt=time.GetUtcNow()};
        db.Add(receipt);await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        // Simulate the provider committing an effect before the worker receives a response.
        if(scenario=="timeout-after-success")throw new AgencyNotificationProviderException(JobFailure.ProviderTimeout);
        return receipt.Id;
    }

    public async Task<bool> Apply(JobLease lease,Guid receiptId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var now=time.GetUtcNow();var job=await SqlJobLeases.OwnedAsync(db,lease,now,token);if(job is null)return false;
        var notification=await db.Set<AgencyNotification>().SingleOrDefaultAsync(x=>x.WorkId==job.Id,token);
        var receipt=await db.Set<AgencyNotificationReceipt>().SingleOrDefaultAsync(x=>x.Id==receiptId,token);
        if(job.Kind!=AgencyNotificationService.Kind||job.Kind!=lease.Kind||job.OperationKey!=lease.OperationKey||job.ScenarioVersionId!=lease.ScenarioVersionId||
            notification is null||receipt is null||receipt.NotificationId!=notification.Id||receipt.AgencyId!=notification.AgencyId)
            throw new AgencyNotificationProviderException(JobFailure.ProviderConflict);
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==job.Id&&x.AttemptNumber==lease.Attempt,token);
        attempt.EndedAt=now;attempt.Outcome=receipt.Accepted?"succeeded":"rejected";attempt.Response=JsonSerializer.Serialize(new{receiptId});
        if(receipt.Accepted)
        {job.State="succeeded";job.CompletedAt=now;job.ErrorCode=null;job.LeaseToken=null;job.LeaseExpiresAt=null;job.Result=JsonSerializer.Serialize(new{receiptId});}
        else{attempt.ErrorCode="provider-rejected";await SqlJobLeases.MarkTerminalAsync(db,job,"provider-rejected",now,token);}
        db.Add(new AgencyActivity{AgencyId=notification.AgencyId,Action=receipt.Accepted?"agency.notification-delivered":"agency.notification-rejected",OccurredAt=now,CreatedAt=now});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return true;
    }
}
