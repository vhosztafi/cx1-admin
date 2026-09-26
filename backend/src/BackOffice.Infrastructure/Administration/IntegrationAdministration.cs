using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
namespace BackOffice.Infrastructure.Administration;
public sealed record IntegrationScenarioEdit(string Scope,string Scenario,string Reason);
public sealed class IntegrationAdministration(IDbContextFactory<BackOfficeDbContext> factory,SqlCommandBoundary commands,TimeProvider time,IHostEnvironment environment)
{
    public static readonly string[] EditableScopes=["operational-delivery","operational-claims","operational-mid","quote-delivery","servicing-delivery","finance-refund-payment-demo","finance-bordereau-submission-demo"];
    private static readonly string[] Families=["diagnostic-probe","agency-notification","quote-lookup","quote-rating","capacity-escalation","quote-delivery","servicing-rating","servicing-delivery","renewal-lapse-notification","cancellation-notice","file-finalization","policy-reconstruction","document-generation","operational-delivery","operational-claims","mid-update","cancellation-mid-removal","finance-refund-payment","finance-bordereau-submit"];
    public async Task<object> HealthAsync(ActorContext actor,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdminAccess.Authorize(db,actor,ct);
        var counts=await db.Set<OutboxWork>().GroupBy(x=>new{x.Kind,x.State}).Select(g=>new{g.Key.Kind,g.Key.State,count=g.Count(),latest=g.Max(x=>x.CompletedAt)}).ToArrayAsync(ct);
        var scopes=EditableScopes;
        var rows=await db.Set<SettingVersion>().AsNoTracking().Where(x=>scopes.Contains(x.Scope)&&x.EffectiveFrom<=time.GetUtcNow()).ToArrayAsync(ct);
        var settings=rows.GroupBy(x=>x.Scope).Select(g=>g.MaxBy(x=>x.Version)!).Select(x=>new{x.Id,x.Scope,x.Version,x.EffectiveFrom,scenario=Scenario(x),options=Options(x.Scope),etag=AdministrationRequests.Etag(x)}).ToArray();
        var result=new{families=Families.Concat(counts.Select(x=>x.Kind)).Distinct().Order().Select(kind=>new{kind,queued=counts.Where(x=>x.Kind==kind&&x.State is "pending" or "leased").Sum(x=>x.count),succeeded=counts.Where(x=>x.Kind==kind&&x.State=="succeeded").Sum(x=>x.count),failed=counts.Where(x=>x.Kind==kind&&x.State=="failed").Sum(x=>x.count),lastSuccess=counts.FirstOrDefault(x=>x.Kind==kind&&x.State=="succeeded")?.latest,lastFailure=counts.FirstOrDefault(x=>x.Kind==kind&&x.State=="failed")?.latest}),settings,scenarioEditing=environment.IsDevelopment()};
        await tx.CommitAsync(ct);return result;
    }
    public Task<CommandOutcome> ScenarioAsync(ActorContext actor,IntegrationScenarioEdit input,string etag,string key,CancellationToken ct=default)
        =>commands.ExecuteAuthorizedAsync(new(actor.UserId,"/api/v1/admin/integration-scenarios",key,Guid.NewGuid()),new{input,etag},"administration.integration-command",(db,t)=>AdminAccess.Authorize(db,actor,t),async(db,t)=>
        {
            if(!environment.IsDevelopment())throw new QuoteOperationException(403,"demo-scenarios-disabled");
            AdminAccess.Text(input.Reason,1000);if(!EditableScopes.Contains(input.Scope)||!Options(input.Scope).Contains(input.Scenario))throw new QuoteOperationException(400,"integration-scenario-invalid");
            var source=await db.Set<SettingVersion>().Where(x=>x.Scope==input.Scope).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(t)??throw new QuoteOperationException(409,"integration-not-configured");
            if(AdministrationRequests.Etag(source)!=etag)throw new QuoteOperationException(string.IsNullOrEmpty(etag)?428:412,"administration-version-changed");
            if(source.EffectiveFrom>time.GetUtcNow()||Scenario(source)==null)throw new QuoteOperationException(409,"integration-configuration-invalid");
            var values=System.Text.Json.Nodes.JsonNode.Parse(source.Values)!;values["scenario"]=input.Scenario;
            var row=new SettingVersion{Scope=source.Scope,Version=source.Version+1,EffectiveFrom=time.GetUtcNow(),Values=values.ToJsonString(),CreatedBy=actor.UserId,CreatedAt=time.GetUtcNow()};
            if(Scenario(row)!=input.Scenario)throw new QuoteOperationException(400,"integration-scenario-invalid");
            db.Add(row);AdminAccess.Audit(db,actor,row.Id,"administration.integration-scenario-published",input.Reason,new{source.Scope,source.Version,scenario=Scenario(source)},new{row.Scope,row.Version,scenario=input.Scenario},time.GetUtcNow());
            return new CommandOutcome(row.Id,201,JsonSerializer.Serialize(new{row.Id,row.Scope,row.Version,scenario=input.Scenario,etag=AdministrationRequests.Etag(row)},ProductAdministration.Json));
        },ct,IsolationLevel.Serializable);
    public async Task<object> JobsAsync(ActorContext actor,string? kind,string? state,int offset,DateTimeOffset? asOf,CancellationToken ct=default)
    {
        if(offset is <0 or >10000||kind?.Length>60||state is not(null or "" or "pending" or "leased" or "succeeded" or "failed"))throw new QuoteOperationException(400,"integration-query-invalid");
        var at=asOf??time.GetUtcNow();if(at>time.GetUtcNow().AddSeconds(5))throw new QuoteOperationException(400,"integration-query-invalid");
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdminAccess.Authorize(db,actor,ct);
        var query=db.Set<OutboxWork>().AsNoTracking().Where(x=>x.CreatedAt<=at);if(!string.IsNullOrEmpty(kind))query=query.Where(x=>x.Kind==kind);if(!string.IsNullOrEmpty(state))query=query.Where(x=>x.State==state);
        var total=await query.CountAsync(ct);var rows=await query.OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Skip(offset).Take(25).ToArrayAsync(ct);
        var result=new{items=rows.Select(View),totalCount=total,asOf=at,nextOffset=offset+rows.Length<total&&offset<10000?(int?)(offset+rows.Length):null};await tx.CommitAsync(ct);return result;
    }
    public async Task<object> DetailAsync(ActorContext actor,Guid id,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdminAccess.Authorize(db,actor,ct);
        var row=await db.Set<OutboxWork>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new QuoteOperationException(404,"job-not-found");
        var attempts=await db.Set<AdapterAttempt>().AsNoTracking().Where(x=>x.WorkId==id).OrderBy(x=>x.AttemptNumber).Select(x=>new{x.Id,x.AttemptNumber,x.StartedAt,x.EndedAt,x.Outcome,x.ErrorCode}).ToArrayAsync(ct);
        var operations=await db.Set<DemoProviderOperation>().AsNoTracking().Where(x=>x.Kind==row.Kind&&x.OperationKey==row.OperationKey).Select(x=>new{x.Id,x.CreatedAt}).ToArrayAsync(ct);
        var result=new{job=View(row),attempts,providerOperations=operations};await tx.CommitAsync(ct);return result;
    }
    private static object View(OutboxWork x)=>new{x.Id,x.Kind,x.State,x.Attempts,x.AttemptLimit,x.CreatedAt,x.NextAttemptAt,x.CompletedAt,x.ErrorCode,x.SubjectRecordId,x.ScenarioVersionId,
        retryAllowed=x.Kind==SqlJobLeases.DiagnosticKind&&JobRetryBudget.ExpandedLimit(x.State,x.ErrorCode,x.Attempts,x.AttemptLimit)!=null,etag=AdminAccess.Etag(x.RowVersion)};
    private static string[] Options(string scope)=>scope switch
    {
        "finance-refund-payment-demo" or "finance-bordereau-submission-demo"=>["success","reject","fail-once","timeout-after-success"],
        "operational-claims"=>["success","reject","transient-once","timeout-after-success","retry-required","summary-details"],
        "operational-delivery" or "operational-mid"=>["success","reject","transient-once","timeout-after-success","retry-required"],
        _=>["success","reject","transient-once","timeout-after-success"]
    };
    private static string? Scenario(SettingVersion row)
    {
        try{return row.Scope switch{"quote-delivery"=>QuoteTermsSeed.Scenario(row),"servicing-delivery"=>ServicingTermsSeed.Scenario(row),"operational-delivery"=>OperationalDeliverySeed.Scenario(row),"operational-claims"=>OperationalClaimsSeed.Scenario(row),"operational-mid"=>OperationalMidSeed.Scenario(row),"finance-refund-payment-demo"=>FinancePaymentWorker.Scenario(row),"finance-bordereau-submission-demo"=>FinanceSubmissionWorker.Scenario(row),_=>null};}
        catch(Exception e)when(e is FinancePaymentWorkerException or FinanceSubmissionWorkerException){return null;}
    }
}
