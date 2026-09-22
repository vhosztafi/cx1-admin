using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;

public sealed class ClaimsHandoffService(SqlCommandBoundary commands,IncidentOccurrenceResolver resolver,TimeProvider time)
{
    public const string WorkKind="operational-claims";
    public async Task<CommandOutcome> Handoff(ActorContext actor,Guid id,string etag,Guid revisionId,Guid resolutionId,Guid providerId,bool log,string key,CancellationToken token)
    {
        ClaimsSnapshot? snapshot=null;OperationalIncident? hint=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/incidents/{id}/{(log?"log-and-handoff":"handoff")}",key,Guid.NewGuid()),new{etag,revisionId,resolutionId,providerId},"claims.handoff-queued",
            async(db,ct)=>
            {
                hint=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw ClaimsSnapshots.Missing();
                snapshot=await ClaimsSnapshots.Capture(db,actor,hint,revisionId,resolutionId,providerId,resolver,ct);
            },async(db,ct)=>
            {
                var incident=await db.Set<OperationalIncident>().FromSqlInterpolated($"SELECT * FROM Incident WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}").SingleAsync(ct);
                if(TaskService.Etag(incident.RowVersion)!=etag||!incident.RowVersion.SequenceEqual(hint!.RowVersion))throw new OperationalAccessException(412,"stale-incident");
                if(incident.CurrentRevisionId!=revisionId||incident.CurrentResolutionId!=resolutionId)throw new OperationalAccessException(409,"claims-selection-changed");
                if(incident.State!="logged"&&!(log&&incident.State=="draft"))throw new OperationalAccessException(409,"claims-handoff-not-available");
                if(await db.Set<ClaimsHandoff>().AnyAsync(x=>x.RevisionId==revisionId,ct))throw new OperationalAccessException(409,"claims-revision-already-submitted");
                var json=ClaimsSnapshots.Serialize(snapshot!);var now=time.GetUtcNow();
                var handoff=new ClaimsHandoff{IncidentId=id,PolicyId=incident.PolicyId,RevisionId=revisionId,ResolutionId=resolutionId,SourceVersionId=snapshot!.SourceVersionId,
                    AdministratorId=providerId,RequestJson=json,RequestHash=DeliverySnapshots.Hash(json),CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
                db.Add(handoff);await db.SaveChangesAsync(ct);
                var result=await Queue(db,actor,handoff,"handoff",null,ct);incident.State="queued";incident.UpdatedAt=now;await db.SaveChangesAsync(ct);return result;
            },token);
    }
    internal async Task<CommandOutcome> Queue(BackOfficeDbContext db,ActorContext actor,ClaimsHandoff handoff,string purpose,string? body,CancellationToken token)
    {
        var now=time.GetUtcNow();var setting=await db.Set<SettingVersion>().Where(x=>x.Scope==WorkKind&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
        if(setting is null||OperationalClaimsSeed.Scenario(setting)is null)throw new OperationalAccessException(503,"claims-scenario-unavailable");
        var request=new ClaimsRequest{HandoffId=handoff.Id,Purpose=purpose,ScenarioVersionId=setting.Id,CreatedBy=actor.UserId,CreatedAt=now};
        request.PayloadJson=ClaimsSnapshots.Serialize(new{format="claims-operation-2",requestId=request.Id,handoffId=handoff.Id,purpose,handoffHash=handoff.RequestHash,providerReference=handoff.ProviderReference,body});
        request.PayloadHash=DeliverySnapshots.Hash(request.PayloadJson);
        var work=new OutboxWork{Kind=WorkKind,SubjectRecordId=request.Id,ScenarioVersionId=setting.Id,Payload=request.PayloadJson,CreatedBy=actor.UserId,CreatedAt=now,NextAttemptAt=now,OperationKey=$"operational-claims/{request.Id:N}"};
        request.WorkId=work.Id;db.Add(work);db.Add(request);await db.SaveChangesAsync(token);return MessageDeliveryService.JobOutcome(work);
    }
}
