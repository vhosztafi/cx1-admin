using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class LegacyOperationalBridge
{
    public async Task<Guid> LapseNotice(ActorContext actor,Guid lapseId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var lapse=await db.Set<RenewalLapseEvent>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==lapseId,token)
            ??throw new OperationalAccessException(404,"lapse-correspondence-unavailable");
        var registered=await tasks.Register(actor,new("policy",lapse.PolicyId),"lapse-correspondence-subject/"+lapseId.ToString("N"),token);
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var held=await OperationalScope.HoldSubjects(db,actor,[registered.ResourceId],"message-write",token);
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==lapse.PolicyId,token);
        if(!await db.Set<PolicyTerm>().AnyAsync(x=>x.Id==lapse.TermId&&x.PolicyId==policy.Id&&x.EndsAt==lapse.EffectiveAt,token))
            throw new OperationalAccessException(404,"lapse-correspondence-unavailable");
        await using var command=db.Database.GetDbConnection().CreateCommand();command.Transaction=transaction.GetDbTransaction();
        command.CommandText="DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @r;";
        var key=command.CreateParameter();key.ParameterName="@key";key.Value="CoverMGA.LapseCorrespondence."+lapseId.ToString("N");command.Parameters.Add(key);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(token))<0)throw new CommandBusyException();
        var existing=await db.Set<RenewalLapseCorrespondence>().AsNoTracking().SingleOrDefaultAsync(x=>x.LapseEventId==lapseId,token);
        if(existing is not null){await transaction.CommitAsync(token);return existing.MessageId;}
        var body=$"Recorded {lapse.Mode} renewal lapse for {policy.Reference}.\nOriginal cover ends {lapse.EffectiveAt:O}; this association changes no cover.\n\n{lapse.Reason}\n\nThe original notification attempts and legacy receipt remain in renewal history. This internal draft does not send or resend a notice.";
        var message=await threads.ImportInternal(db,held,$"Recorded renewal lapse · {policy.Reference}",body,token);
        db.Add(new RenewalLapseCorrespondence{LapseEventId=lapse.Id,MessageId=message.Id,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()});
        db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,OccurredAt=time.GetUtcNow(),EventType="renewal.lapse-correspondence-associated",CorrelationId=Guid.NewGuid(),
            After=System.Text.Json.JsonSerializer.Serialize(new{lapseId,messageId=message.Id})});
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return message.Id;
    }
}
