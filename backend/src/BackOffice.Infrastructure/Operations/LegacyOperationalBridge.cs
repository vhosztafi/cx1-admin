using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class LegacyOperationalBridge(IDbContextFactory<BackOfficeDbContext> factory, TaskService tasks, ThreadService threads, TimeProvider time)
{
    public async Task<Guid> MatchRequest(Guid requestId, CancellationToken token = default)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var request=await db.Set<MatchInformationRequest>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==requestId,token)
            ??throw new OperationalAccessException(404,"match-request-unavailable");
        var actor=await MidAuthority.Sender(db,request.ActorId,token);
        var review=await new MatchScope(actor).Reviews(db).SingleOrDefaultAsync(x=>x.Id==request.MatchId,token)
            ??throw new OperationalAccessException(404,"match-request-unavailable");
        var intake=await db.Set<MatchSubmission>().AsNoTracking().SingleAsync(x=>x.Id==review.SubmissionId,token);
        // An unresolved candidate is never an audience. Keep original review
        // content internal to the submitting agency, even after later linking.
        var registered=await tasks.Register(actor,new("agency",intake.AgencyId),"match-request-subject/"+requestId.ToString("N"),token);
        await using var transaction=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,token);
        var held=await OperationalScope.HoldSubjects(db,actor,[registered.ResourceId],"message-write",token);
        if(!await new MatchScope(held.Actor).InformationRequests(db).AnyAsync(x=>x.Id==requestId,token))
            throw new OperationalAccessException(404,"match-request-unavailable");
        await using var command=db.Database.GetDbConnection().CreateCommand();command.Transaction=transaction.GetDbTransaction();
        command.CommandText="DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @r;";
        var key=command.CreateParameter();key.ParameterName="@key";key.Value="CoverMGA.MatchCorrespondence."+requestId.ToString("N");command.Parameters.Add(key);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(token))<0)throw new BackOffice.Infrastructure.Platform.CommandBusyException();
        var existing=await db.Set<MatchCorrespondence>().AsNoTracking().SingleOrDefaultAsync(x=>x.InformationRequestId==requestId,token);
        if(existing is not null){await transaction.CommitAsync(token);return existing.MessageId;}
        var message=await threads.ImportRecordedRequest(db,held,request,intake.Reference,token);
        db.Add(new MatchCorrespondence{InformationRequestId=requestId,MessageId=message.Id,CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()});
        db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,OccurredAt=time.GetUtcNow(),EventType="match.correspondence-associated",CorrelationId=Guid.NewGuid(),
            After=System.Text.Json.JsonSerializer.Serialize(new{requestId,messageId=message.Id})});
        await db.SaveChangesAsync(token);await transaction.CommitAsync(token);return message.Id;
    }
}
