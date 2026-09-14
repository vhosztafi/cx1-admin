using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BackOffice.Infrastructure.Agencies;

public sealed class InvitationDemoReveal(IDbContextFactory<BackOfficeDbContext> factory,AgencyDraftService agencies,AgencyNotificationPayload payload,TimeProvider time,IHostEnvironment environment)
{
    public async Task<string> Reveal(ActorContext actor,Guid invitationId,CancellationToken token=default)
    {
        if(!environment.IsDevelopment())throw new AgencyCommandException(404,"invitation-not-found");
        await agencies.Authorize(actor,null,token);
        await using var db=await factory.CreateDbContextAsync(token);
        var reference=await db.Set<AgencyInvitation>().AsNoTracking().Where(x=>x.Id==invitationId).Select(x=>new{x.AgencyId,x.UserId}).SingleOrDefaultAsync(token)
            ??throw new AgencyCommandException(404,"invitation-not-found");
        await using var transaction=await db.Database.BeginTransactionAsync(token);
        var agency=await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM [Agency] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.AgencyId}").SingleAsync(token);
        var user=await db.Set<StaffUser>().FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={reference.UserId}").SingleAsync(token);
        var invitation=await db.Set<AgencyInvitation>().FromSqlInterpolated($"SELECT * FROM [AgencyInvitation] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={invitationId}").SingleAsync(token);
        var now=time.GetUtcNow();
        if(agency.State!="active"||user.State!="invited"||user.AgencyId!=agency.Id||invitation.State!="pending"||invitation.IssuedAt is null||invitation.IssuedAt>now||invitation.ExpiresAt is null||invitation.ExpiresAt<=now)
            throw new AgencyCommandException(409,"invitation-not-current");
        var message=await db.Set<AgencyNotification>().SingleAsync(x=>x.Id==invitation.NotificationId&&x.InvitationId==invitationId&&x.AgencyId==agency.Id,token);
        var envelope=payload.Unprotect(agency.Id,message.Id,message.ProtectedPayload);
        if(envelope.Template!="agency-invitation"||!CryptographicOperations.FixedTimeEquals(AgencyNotificationPayload.Fingerprint(envelope),message.ContentHash)||
            !InvitationToken.TryHash(envelope.Content,out var hash)||invitation.TokenHash is null||!CryptographicOperations.FixedTimeEquals(hash,invitation.TokenHash))
            throw new AgencyCommandException(409,"invitation-delivery-unavailable");
        db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,SubjectRecordId=invitationId,EventType="agency.invitation-demo-revealed",OccurredAt=now,After=JsonSerializer.Serialize(new{invitationId})});
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return envelope.Content;
    }
}
