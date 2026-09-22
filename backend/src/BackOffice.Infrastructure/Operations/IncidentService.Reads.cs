using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class IncidentService
{
    public async Task<CommandOutcome> Read(ActorContext actor,Guid id,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var row=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw Missing();
        await HoldPolicy(db,actor,row.PolicyId,"incident-read",token);row=await db.Set<OperationalIncident>().AsNoTracking().SingleAsync(x=>x.Id==id,token);var outcome=await Outcome(db,row,200,token);await transaction.CommitAsync(token);return outcome;
    }
    public async Task<CommunicationPage> List(ActorContext actor,Guid policyId,string? state,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);if(state is not(null or "draft" or "logged" or "queued" or "handed-off" or "failed"))throw new OperationalAccessException(400,"incident-state-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        await HoldPolicy(db,actor,policyId,"incident-read",token);
        var query=db.Set<OperationalIncident>().AsNoTracking().Where(x=>x.PolicyId==policyId&&x.CreatedAt<=asOf&&(state==null||x.State==state));var total=await query.CountAsync(token);
        if(before is Guid cursorId){var cursor=await query.SingleOrDefaultAsync(x=>x.Id==cursorId,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<cursor.CreatedAt||x.CreatedAt==cursor.CreatedAt&&x.Id.CompareTo(cursorId)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);var items=new List<object>();
        foreach(var row in rows.Take(size)){using var item=JsonDocument.Parse((await Outcome(db,row,200,token)).Body);items.Add(item.RootElement.Clone());}
        await transaction.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<CommunicationPage> Revisions(ActorContext actor,Guid id,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var row=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw Missing();await HoldPolicy(db,actor,row.PolicyId,"incident-read",token);
        var query=db.Set<IncidentRevision>().AsNoTracking().Where(x=>x.IncidentId==id&&x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid cursor){var ordinal=await query.Where(x=>x.Id==cursor).Select(x=>(int?)x.Number).SingleOrDefaultAsync(token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.Number<ordinal);}
        var rows=await query.OrderByDescending(x=>x.Number).Take(size+1).ToArrayAsync(token);
        var items=rows.Take(size).Select(x=>{using var draft=JsonDocument.Parse(x.DraftJson);return(object)new{x.Id,incidentId=id,x.Number,draft=draft.RootElement.Clone(),x.ContentHash,x.Reason,x.AuthorLabel,x.CreatedAt};}).ToArray();
        await transaction.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<object> Validate(ActorContext actor,JsonElement draft,CancellationToken token)
    {
        IncidentRules.Draft(draft);var policyId=draft.GetProperty("policyId").GetGuid();
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        await HoldPolicy(db,actor,policyId,"incident-write",token);await ValidateOwned(db,actor,policyId,draft,token);
        var occurrence=draft.TryGetProperty("occurrence",out var observed)?observed.Deserialize<IncidentOccurrence>(Json):null;
        var resolution=await resolver.ResolveHeld(db,actor,policyId,occurrence,time.GetUtcNow(),token,Subject(draft));
        var missing=IncidentRules.Missing(draft,resolution.State,Subject(draft)is not null&&resolution.State=="resolved");
        await transaction.CommitAsync(token);return new{valid=missing.Count==0,issues=missing.Select(x=>new{path="/"+x,code="incident-required",message="This detail must be completed or clarified before logging.",severity="error"}).ToArray()};
    }
    public async Task<CommunicationPage> Resolutions(ActorContext actor,Guid id,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var policy=await db.Set<OperationalIncident>().Where(x=>x.Id==id).Select(x=>(Guid?)x.PolicyId).SingleOrDefaultAsync(token)??throw Missing();await HoldPolicy(db,actor,policy,"incident-read",token);
        var query=db.Set<IncidentOccurrenceRecord>().AsNoTracking().Where(x=>x.IncidentId==id&&x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid cursor){var prior=await query.SingleOrDefaultAsync(x=>x.Id==cursor,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<prior.CreatedAt||x.CreatedAt==prior.CreatedAt&&x.Id.CompareTo(cursor)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);
        var items=rows.Take(size).Select(x=>{using var json=JsonDocument.Parse(x.ResolutionJson);return(object)json.RootElement.Clone();}).ToArray();await transaction.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
}
