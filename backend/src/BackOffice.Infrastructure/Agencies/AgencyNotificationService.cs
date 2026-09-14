using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed class AgencyNotificationService(AgencyNotificationPayload payload,TimeProvider time)
{
    public const string Kind="agency-notification";

    // Internal transaction participant, never a public enqueue endpoint. The owning activation
    // command authorizes and locks its agency before calling; all rows commit/roll back together.
    public async Task<Guid> EnqueueActivation(BackOfficeDbContext db,Guid agencyId,Guid operationId,
        Guid scenarioVersionId,Guid actorId,AgencyDeliveryEnvelope envelope,CancellationToken token=default)
        =>await Enqueue(db,agencyId,null,operationId,scenarioVersionId,actorId,envelope,token);

    internal async Task<Guid> EnqueueInvitation(BackOfficeDbContext db,Guid agencyId,Guid invitationId,Guid scenarioVersionId,Guid actorId,AgencyDeliveryEnvelope envelope,CancellationToken token)
        =>await Enqueue(db,agencyId,invitationId,invitationId,scenarioVersionId,actorId,envelope,token);

    private async Task<Guid> Enqueue(BackOfficeDbContext db,Guid agencyId,Guid? invitationId,Guid operationId,Guid scenarioVersionId,Guid actorId,AgencyDeliveryEnvelope envelope,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Agency delivery requires its owning transaction.");
        if(operationId==Guid.Empty||envelope.Template!=(invitationId is null?"agency-activated":"agency-invitation"))throw new ArgumentException("Invalid agency delivery identity.");
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={agencyId}").SingleOrDefaultAsync(token);
        if(agency is null||agency.State!="active")throw new InvalidOperationException("Activation delivery requires an active agency.");
        if(invitationId is Guid invitation&&!await db.Set<AgencyInvitation>().AnyAsync(x=>x.Id==invitation&&x.AgencyId==agencyId&&x.State=="staged",token))
            throw new InvalidOperationException("Invitation delivery requires an owned staged invitation.");
        var setting=await db.Set<SettingVersion>().SingleOrDefaultAsync(x=>x.Id==scenarioVersionId,token);
        if(setting is null||(setting.Scope!="agency-notification"&&!(invitationId is not null&&setting.Scope=="agency-invitation-delivery")))throw new ArgumentException("Invalid notification scenario.");
        AgencyNotificationWorker.Scenario(setting.Values);
        var hash=AgencyNotificationPayload.Fingerprint(envelope);var operationKey=operationId.ToString("N");
        var existing=await db.Set<OutboxWork>().FromSqlInterpolated($"SELECT * FROM [OutboxWork] WITH (UPDLOCK,HOLDLOCK) WHERE [Kind]={Kind} AND [OperationKey]={operationKey}").SingleOrDefaultAsync(token);
        if(existing is not null)
        {
            var prior=await db.Set<AgencyNotification>().SingleAsync(x=>x.WorkId==existing.Id,token);
            if(prior.AgencyId!=agencyId||prior.InvitationId!=invitationId||existing.SubjectRecordId!=agencyId||existing.ScenarioVersionId!=scenarioVersionId||
                !CryptographicOperations.FixedTimeEquals(prior.ContentHash,hash))throw new InvalidOperationException("Notification operation conflicts with its saved content.");
            return prior.Id;
        }
        var now=time.GetUtcNow();var notificationId=Guid.NewGuid();
        var work=new OutboxWork{Kind=Kind,OperationKey=operationKey,SubjectRecordId=agencyId,ScenarioVersionId=scenarioVersionId,
            Payload=JsonSerializer.Serialize(new{notificationId}),NextAttemptAt=now,CreatedAt=now,CreatedBy=actorId};
        db.Add(work);db.Add(new AgencyNotification{Id=notificationId,AgencyId=agencyId,WorkId=work.Id,InvitationId=invitationId,Purpose=envelope.Template,
            ContentHash=hash,ProtectedPayload=payload.Protect(agencyId,notificationId,envelope),CreatedAt=now,CreatedBy=actorId});
        db.Add(new AgencyActivity{AgencyId=agencyId,ActorId=actorId,Action="agency.notification-queued",OccurredAt=now,CreatedAt=now,CreatedBy=actorId});
        await db.SaveChangesAsync(token);return notificationId;
    }
}
