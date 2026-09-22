using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;
public sealed partial class ClaimsSummaryService(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,ClaimsHandoffService queue,IncidentOccurrenceResolver resolver,TimeProvider time)
{
    public async Task<CommandOutcome> FollowUp(ActorContext actor,Guid incidentId,string etag,string purpose,string? body,string key,CancellationToken token)
    {
        if(purpose is not("refresh" or "contact")||purpose=="contact"&&(string.IsNullOrWhiteSpace(body)||body.Length>8000))throw new ClaimsRuleException("claims-contact-required");
        OperationalIncident? incident=null;ClaimsHandoff? handoff=null;
        return await commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/incidents/{incidentId}/{purpose}",key,Guid.NewGuid()),new{etag,body},"claims."+purpose+"-queued",
            async(db,ct)=>
            {
                incident=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==incidentId,ct)??throw ClaimsSnapshots.Missing();
                await OperationalScope.HoldParents(db,actor,[new("policy",incident.PolicyId)],"incident-handoff",ct);
                handoff=await db.Set<ClaimsHandoff>().AsNoTracking().SingleOrDefaultAsync(x=>x.RevisionId==incident.CurrentRevisionId,ct)??throw ClaimsSnapshots.Missing();
                await ClaimsAuthority.Hold(db,actor,handoff,resolver,ct);
            },async(db,ct)=>
            {
                var current=await db.Set<OperationalIncident>().FromSqlInterpolated($"SELECT * FROM Incident WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={incidentId}").SingleAsync(ct);
                if(TaskService.Etag(current.RowVersion)!=etag||!current.RowVersion.SequenceEqual(incident!.RowVersion))throw new OperationalAccessException(412,"stale-incident");
                if(current.State!="handed-off"||handoff!.State!="acknowledged"||handoff.RevisionId!=current.CurrentRevisionId)throw new OperationalAccessException(409,"claims-acknowledgement-required");
                return await queue.Queue(db,actor,handoff,purpose,body,ct);
            },token);
    }
    public async Task<CommandOutcome> Administrators(ActorContext actor,Guid incidentId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);await HoldRead(db,actor,incidentId,token);
        var items=await db.Set<ClaimsAdministrator>().AsNoTracking().Where(x=>x.State=="active").OrderBy(x=>x.Code).Select(x=>new{x.Id,label=x.Name}).ToArrayAsync(token);
        await tx.CommitAsync(token);return new(incidentId,200,ClaimsSnapshots.Serialize(new{items}));
    }
    public async Task<CommunicationPage> Summaries(ActorContext actor,Guid incidentId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);await HoldRead(db,actor,incidentId,token);
        var query=from summary in db.Set<ClaimsSummary>().AsNoTracking() join handoff in db.Set<ClaimsHandoff>() on summary.HandoffId equals handoff.Id where handoff.IncidentId==incidentId&&summary.ReceivedAt<=asOf select summary;
        var total=await query.CountAsync(token);
        if(before is Guid cursor){var row=await query.SingleOrDefaultAsync(x=>x.Id==cursor,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.AsOf<row.AsOf||x.AsOf==row.AsOf&&(x.ReceivedAt<row.ReceivedAt||x.ReceivedAt==row.ReceivedAt&&x.Id.CompareTo(cursor)<0));}
        var rows=await query.OrderByDescending(x=>x.AsOf).ThenByDescending(x=>x.ReceivedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);
        var items=rows.Take(size).Select(x=>{var value=JsonSerializer.Deserialize<ClaimsAdministratorSummary>(x.SummaryJson,ClaimsSnapshots.Json)!;return(object)new{x.Id,incidentId,x.HandoffId,x.AsOf,x.ReceivedAt,value.Status,value.Paid,value.Reserved,value.Currency,value.ProviderReference,providerEventId=x.ProviderEventId};}).ToArray();
        await tx.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<CommunicationPage> Handoffs(ActorContext actor,Guid incidentId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);await HoldRead(db,actor,incidentId,token);
        var query=db.Set<ClaimsHandoff>().AsNoTracking().Where(x=>x.IncidentId==incidentId&&x.CreatedAt<=asOf);var total=await query.CountAsync(token);
        if(before is Guid cursor){var row=await query.SingleOrDefaultAsync(x=>x.Id==cursor,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<row.CreatedAt||x.CreatedAt==row.CreatedAt&&x.Id.CompareTo(cursor)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);
        var items=rows.Take(size).Select(x=>{using var json=JsonDocument.Parse(x.RequestJson);return(object)new{x.Id,incidentId,x.RevisionId,x.ResolutionId,x.SourceVersionId,x.AdministratorId,x.State,x.OutcomeCode,x.ProviderReference,x.CreatedAt,x.CompletedAt,submitted=json.RootElement.Clone()};}).ToArray();
        await tx.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    public async Task<CommunicationPage> Requests(ActorContext actor,Guid incidentId,Guid? before,int size,DateTimeOffset asOf,CancellationToken token)
    {
        CommunicationScope.Page(size);await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);await HoldRead(db,actor,incidentId,token);
        var query=from request in db.Set<ClaimsRequest>().AsNoTracking() join handoff in db.Set<ClaimsHandoff>() on request.HandoffId equals handoff.Id where handoff.IncidentId==incidentId&&request.CreatedAt<=asOf select request;
        var total=await query.CountAsync(token);
        if(before is Guid cursor){var row=await query.SingleOrDefaultAsync(x=>x.Id==cursor,token)??throw CommunicationScope.BadCursor();query=query.Where(x=>x.CreatedAt<row.CreatedAt||x.CreatedAt==row.CreatedAt&&x.Id.CompareTo(cursor)<0);}
        var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(size+1).ToArrayAsync(token);var items=new List<object>();
        foreach(var row in rows.Take(size))
        {
            var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==row.WorkId,token);
            var attempts=await db.Set<AdapterAttempt>().AsNoTracking().Where(x=>x.WorkId==work.Id).OrderBy(x=>x.AttemptNumber).Select(x=>new{x.Id,number=x.AttemptNumber,x.StartedAt,x.EndedAt,x.Outcome,x.ErrorCode}).ToArrayAsync(token);
            using var payload=JsonDocument.Parse(row.PayloadJson);
            items.Add(new{row.Id,row.HandoffId,row.Purpose,jobId=work.Id,work.State,work.ErrorCode,row.CreatedAt,work.CompletedAt,attempts,body=payload.RootElement.GetProperty("body").ValueKind==JsonValueKind.String?payload.RootElement.GetProperty("body").GetString():null,retryAllowed=JobRetryBudget.ExpandedLimit(work.State,work.ErrorCode,work.Attempts,work.AttemptLimit)is not null,etag=TaskService.Etag(work.RowVersion)});
        }
        await tx.CommitAsync(token);return new(items,total,rows.Length>size?rows[size-1].Id:null);
    }
    internal static async Task HoldRead(BackOfficeDbContext db,ActorContext actor,Guid incidentId,CancellationToken token)
    {
        var policy=await db.Set<OperationalIncident>().Where(x=>x.Id==incidentId).Select(x=>(Guid?)x.PolicyId).SingleOrDefaultAsync(token)??throw ClaimsSnapshots.Missing();
        await OperationalScope.HoldParents(db,actor,[new("policy",policy)],"incident-read",token);
    }
}
