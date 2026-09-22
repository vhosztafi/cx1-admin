using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class IncidentService
{
    public Task<CommandOutcome> Update(ActorContext actor,Guid id,string etag,JsonElement draft,string key,CancellationToken token)
        =>Mutate(actor,id,etag,draft,"update",key,token);
    public Task<CommandOutcome> SaveDescription(ActorContext actor,Guid id,string etag,string description,string key,CancellationToken token)
        =>Mutate(actor,id,etag,JsonSerializer.SerializeToElement(new{description}),"description",key,token);
    public Task<CommandOutcome> Clarify(ActorContext actor,Guid id,string etag,JsonElement occurrence,string reason,string key,CancellationToken token)
        =>Mutate(actor,id,etag,JsonSerializer.SerializeToElement(new{occurrence,reason}),"occurrence",key,token);
    private async Task<CommandOutcome> Mutate(ActorContext actor,Guid id,string etag,JsonElement input,string action,string key,CancellationToken token)
    {
        OperationalIncident? hint=null;HeldOperationalScope? held=null;JsonElement draft=default;
        var reason=action=="description"?"Description saved":action=="occurrence"?IncidentRules.Text(input,"reason"):"Draft updated";
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>1000)throw new IncidentRuleException("incident-change-reason-required");
        var route=$"/api/v1/incidents/{id}"+(action=="update"?"":"/"+action);
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,route,key,Guid.NewGuid()),new{etag,input},"incident."+action,
            async(db,ct)=>
            {
                hint=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Missing();
                held=await HoldPolicy(db,actor,hint.PolicyId,"incident-write",ct);
                if(action=="update")draft=input;
                else
                {
                    var original=await db.Set<IncidentRevision>().Where(x=>x.Id==hint.CurrentRevisionId).Select(x=>x.DraftJson).SingleAsync(ct);
                    var node=JsonNode.Parse(original)!;
                    node[action=="description"?"description":"occurrence"]=JsonNode.Parse(input.GetProperty(action=="description"?"description":"occurrence").GetRawText());
                    draft=JsonSerializer.SerializeToElement(node,Json);
                }
                IncidentRules.Draft(draft);await ValidateOwned(db,actor,hint.PolicyId,draft,ct);
            },async(db,ct)=>
            {
                var row=await Head(db,id,etag,ct);if(!row.RowVersion.SequenceEqual(hint!.RowVersion))throw new OperationalAccessException(412,"stale-incident");
                await Editable(db,row,ct);await Append(db,row,draft,reason!,held!.ActorLabel,actor.UserId,ct);return await Outcome(db,row,200,ct);
            },token);
    }
    public async Task<CommandOutcome> Resolve(ActorContext actor,Guid id,string etag,bool log,string key,CancellationToken token)
    {
        OperationalIncident? hint=null;JsonElement draft=default;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/incidents/{id}/{(log?"log":"occurrence-resolution")}",key,Guid.NewGuid()),new{etag},log?"incident.logged-unsent":"incident.occurrence-resolved",
            async(db,ct)=>
            {
                hint=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw Missing();
                await HoldPolicy(db,actor,hint.PolicyId,"incident-write",ct);
                using var source=JsonDocument.Parse(await db.Set<IncidentRevision>().Where(x=>x.Id==hint.CurrentRevisionId).Select(x=>x.DraftJson).SingleAsync(ct));draft=source.RootElement.Clone();
                await ValidateOwned(db,actor,hint.PolicyId,draft,ct);
            },async(db,ct)=>
            {
                // Original policy/identity/evidence locks precede this mutable head.
                var row=await Head(db,id,etag,ct);if(!row.RowVersion.SequenceEqual(hint!.RowVersion))throw new OperationalAccessException(412,"stale-incident");await Editable(db,row,ct);
                var occurrence=draft.TryGetProperty("occurrence",out var observed)?observed.Deserialize<IncidentOccurrence>(Json):null;
                var known=time.GetUtcNow();var result=await resolver.ResolveHeld(db,actor,row.PolicyId,occurrence,known,ct,Subject(draft));
                var missing=IncidentRules.Missing(draft,result.State,Subject(draft)is not null&&result.State=="resolved");
                if(log&&missing.Count>0)throw new OperationalAccessException(422,"incident-not-ready");
                var record=new IncidentOccurrenceRecord{IncidentId=id,RevisionId=row.CurrentRevisionId!.Value,KnownAt=known,State=result.State,CreatedAt=known,CreatedBy=actor.UserId};
                record.ResolutionJson=JsonSerializer.Serialize(new{id=record.Id,policyId=row.PolicyId,revisionId=record.RevisionId,result.KnownAt,result.State,result.Window,result.Candidates,result.SourceHash},Json);record.ContentHash=Hash(record.ResolutionJson);
                db.Add(record);await db.SaveChangesAsync(ct);
                foreach(var source in result.Candidates)db.Add(new IncidentResolutionSource{ResolutionId=record.Id,VersionId=source.VersionId,SourceHash=source.SourceHash,From=source.From,To=source.To,CreatedBy=actor.UserId,CreatedAt=known});
                row.CurrentResolutionId=record.Id;if(log)row.State="logged";else if(result.State!="resolved")row.State="draft";
                row.UpdatedAt=known;await db.SaveChangesAsync(ct);
                return log?await Outcome(db,row,200,ct):new(record.Id,200,record.ResolutionJson,Etag:TaskService.Etag(row.RowVersion));
            },token);
    }
    private static async Task<OperationalIncident> Head(BackOfficeDbContext db,Guid id,string etag,CancellationToken token)
    {
        var row=await db.Set<OperationalIncident>().FromSqlInterpolated($"SELECT * FROM Incident WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={id}").SingleOrDefaultAsync(token)??throw Missing();
        if(TaskService.Etag(row.RowVersion)!=etag)throw new OperationalAccessException(412,"stale-incident");return row;
    }
    private static async Task Editable(BackOfficeDbContext db,OperationalIncident row,CancellationToken token)
    {
        var handoff=await db.Set<ClaimsHandoff>().AsNoTracking().SingleOrDefaultAsync(x=>x.RevisionId==row.CurrentRevisionId,token);
        if(!ClaimsRules.CanCorrect(row.State,handoff?.State,handoff?.OutcomeCode))throw new OperationalAccessException(409,"incident-handoff-unresolved");
    }
}
