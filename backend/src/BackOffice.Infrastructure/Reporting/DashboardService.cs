using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Reporting;

public sealed record DashboardRow { public Guid Id {get;init;} public string Kind {get;init;}=""; public string Reference {get;init;}=""; public string Label {get;init;}=""; public string State {get;init;}=""; public DateOnly? DueOn {get;init;} public string Href {get;init;}=""; }
public sealed record DashboardQueue(string Code,string Title,string Definition,int? Count,int? Overdue,DashboardRow[] Items,int? NextOffset);
public sealed record OwnNotice(Guid Id,string Kind,string Text,DateTimeOffset CreatedAt,string Href,bool Read);
public sealed class DashboardService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<object> ReadAsync(ActorContext actor,string scope="mine",string? queue=null,int offset=0,CancellationToken token=default)
    {
        if(scope is not ("mine" or "team") || offset is <0 or >10000 || offset>0 && queue==null)throw new QuoteOperationException(400,"dashboard-filter-invalid");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        actor=await ReportingScope.Current(db,actor,token);var now=time.GetUtcNow();var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
        var queues=new List<DashboardQueue>();
        var tasks=TaskDiscovery.Rows(db,actor).Where(x=>scope=="mine"?x.OwnerId==actor.UserId:actor.TeamId!=null&&(x.TeamId==actor.TeamId||db.Set<StaffUser>().Any(u=>u.Id==x.OwnerId&&u.TeamId==actor.TeamId)));
        var open=tasks.Where(x=>x.State!="completed"&&x.State!="cancelled");
        async Task Add(string code,string title,string definition,bool permitted,IQueryable<DashboardRow> rows,bool due=false)
        {
            if(!permitted){queues.Add(new(code,title,definition,null,null,[],null));return;}
            var count=await rows.CountAsync(token);var overdue=due?await rows.CountAsync(x=>x.DueOn<today,token):(int?)null;
            var skip=queue==code?offset:0;var ordered=due?rows.OrderBy(x=>x.DueOn==null).ThenBy(x=>x.DueOn).ThenBy(x=>x.Reference).ThenBy(x=>x.Id):rows.OrderBy(x=>x.Reference).ThenBy(x=>x.Id);var page=await ordered.Skip(skip).Take(25).ToArrayAsync(token);
            queues.Add(new(code,title,definition,count,overdue,page.Select(x=>x with {Href=x.Href.Length>0?x.Href:SearchService.Href(x.Kind,x.Id)}).ToArray(),skip+page.Length<count?skip+page.Length:null));
        }
        await Add("tasks","Open tasks","Incomplete and non-cancelled tasks; selected assignment scope. Overdue means due before today in London.",actor.HasCapability("task-read"),open.Select(x=>new DashboardRow{Id=x.Id,Kind="task",Reference=x.Reference,Label=x.Title,State=x.State,DueOn=x.DueOn}),true);
        var quotes=QuoteDiscovery.Rows(db).Where(x=>db.Set<Quote>().Any(q=>q.Id==x.Id&&(scope=="mine"?q.AssignedUserId==actor.UserId:actor.TeamId!=null&&db.Set<StaffUser>().Any(u=>u.Id==q.AssignedUserId&&u.TeamId==actor.TeamId))));
        await Add("quotes","Quotes awaiting action","Assigned quotes with open capture or a live underwriting cycle; bound and declined quotes excluded.",actor.HasCapability("quote-read"),quotes.Where(x=>x.State!="bound"&&x.State!="declined"&&x.State!="withdrawn"&&x.State!="expired").Select(x=>new DashboardRow{Id=x.Id,Kind="quote",Reference=x.Reference,Label=x.ClientName,State=x.State}));
        var referred=QuoteDiscovery.Rows(db).Where(x=>db.Set<Quote>().Any(q=>q.Id==x.Id&&db.Set<QuoteReferral>().Any(r=>r.QuoteId==q.Id&&r.CycleId==q.CurrentUnderwritingCycleId&&r.State=="open"&&(scope=="mine"?r.AssignedUserId==actor.UserId:actor.TeamId!=null&&db.Set<QuoteSubmission>().Any(s=>s.CycleId==r.CycleId&&s.AssignedTeamId==actor.TeamId)))));
        await Add("referrals","Referrals awaiting decision","Distinct quotes with open referrals in the current underwriting cycle; selected individual or routing-team assignment. No authority eligibility is inferred.",actor.HasCapability("underwriting-read"),referred.Select(x=>new DashboardRow{Id=x.Id,Kind="quote",Reference=x.Reference,Label=x.ClientName,State=x.State}));
        var renewal=PolicyDiscoveryService.Rows(db,now).Where(x=>x.State=="active"&&x.EndsAt>now&&x.EndsAt<=now.AddDays(30)&&!db.Set<PolicyTerm>().Any(t=>t.PolicyId==x.Id&&t.StartsAt==x.EndsAt));
        await Add("renewals","Policies renewing within 30 days","All accessible active policies ending after now and within 30 UTC days; cancelled, expired and already renewed terms excluded.",actor.HasCapability("policy-read"),renewal.Select(x=>new DashboardRow{Id=x.Id,Kind="policy",Reference=x.Reference,Label=x.ClientName,State=x.State}));
        await Add("exceptions","Tasks with changed source","Selected open tasks whose saved source has changed and needs review.",actor.HasCapability("task-read"),open.Where(x=>x.SourceChanged).Select(x=>new DashboardRow{Id=x.Id,Kind="task",Reference=x.Reference,Label=x.Title,State=x.State,DueOn=x.DueOn}));
        await Add("failed-jobs","Failed integration jobs","All saved failed jobs; restricted to integration administrators.",actor.HasCapability("integration-admin"),db.Set<OutboxWork>().Where(x=>x.State=="failed").Select(x=>new DashboardRow{Id=x.Id,Kind="job",Reference=x.Kind,Label=x.Kind,State=x.State,Href="/admin?tab=integrations"}));
        var drafts=db.Set<ServicingDraft>().Where(d=>d.State=="draft"&&db.Set<Policy>().Any(p=>p.Id==d.PolicyId&&db.Set<Quote>().Any(q=>q.Id==p.SourceQuoteId&&q.BoundPolicyId==p.Id&&q.ClientId==p.ClientId&&q.AgencyId==p.AgencyId&&q.RelationshipId==p.RelationshipId)));
        await Add("servicing","Servicing drafts awaiting action","All accessible unissued adjustment, renewal and cancellation drafts; terminal drafts excluded.",actor.HasCapability("policy-read"),drafts.Select(d=>new DashboardRow{Id=d.Id,Kind="draft",Reference=d.Kind,Label=db.Set<Policy>().Where(p=>p.Id==d.PolicyId).Select(p=>p.Reference).First(),State=d.State}));
        var receipts=db.Set<Receipt>().Where(r=>db.Set<Allocation>().Where(a=>a.ReceiptId==r.Id).Sum(a=>(decimal?)(a.ReversalOfId==null?a.Amount:-a.Amount))<r.Amount || !db.Set<Allocation>().Any(a=>a.ReceiptId==r.Id));
        await Add("unmatched-payments","Receipts awaiting allocation","Receipts with remaining unapplied cash; originals less reversals, all authorised finance agencies.",actor.HasCapability("finance-read"),receipts.Select(r=>new DashboardRow{Id=r.Id,Kind="receipt",Reference=r.BankReference,Label="Receipt awaiting allocation",State="unallocated",Href="/accounting?tab=receipts&receiptId="+r.Id}));
        if(queue!=null&&!queues.Any(x=>x.Code==queue))throw new QuoteOperationException(400,"dashboard-queue-invalid");
        var workload=actor.HasCapability("task-read")?await open.GroupBy(x=>x.TypeCode).Select(g=>new{type=g.Key,count=g.Count()}).OrderBy(x=>x.type).ToArrayAsync(token):[];
        var activity=actor.HasCapability("task-read")?await db.Set<OperationalTaskEvent>().Where(x=>x.CreatedBy==actor.UserId&&tasks.Any(t=>t.Id==x.TaskId)).OrderByDescending(x=>x.CreatedAt).Take(20).Select(x=>new{x.Id,x.TaskId,x.Kind,x.CreatedAt}).ToArrayAsync(token):[];
        await tx.CommitAsync(token);return new{asOf=now,scope,queues,workload,activity};
    }
    public async Task<OwnNotice[]> NoticesAsync(ActorContext actor,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);
        var result=await Notices(db,actor,token);await tx.CommitAsync(token);return result;
    }
    private static async Task<OwnNotice[]> Notices(BackOfficeDbContext db,ActorContext actor,CancellationToken token)
    {
        var own=TaskDiscovery.Rows(db,actor).Where(x=>x.OwnerId==actor.UserId&&(x.State!="completed"&&x.State!="cancelled"));
        var tasks=await own.OrderByDescending(x=>x.UpdatedAt).Take(50).Select(x=>new{x.Id,x.Title,x.CreatedAt}).ToArrayAsync(token);
        var events=await db.Set<AuditEvent>().Where(x=>x.SubjectRecordId==actor.UserId&&x.EventType.StartsWith("administration.user")).OrderByDescending(x=>x.OccurredAt).Take(50).Select(x=>new{x.Id,x.EventType,x.OccurredAt}).ToArrayAsync(token);
        var scope="notification-read/"+actor.UserId;var saved=await db.Set<SettingVersion>().Where(x=>x.Scope==scope).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
        var read=saved==null?[]:JsonSerializer.Deserialize<Guid[]>(saved.Values)!;
        return tasks.Select(x=>new OwnNotice(x.Id,"task",x.Title,x.CreatedAt,"/tasks/"+x.Id,read.Contains(x.Id))).Concat(events.Select(x=>new OwnNotice(x.Id,"security",x.EventType,x.OccurredAt,"/account",read.Contains(x.Id)))).OrderByDescending(x=>x.CreatedAt).ToArray();
    }
    public async Task MarkReadAsync(ActorContext actor,Guid id,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);
        var scope="notification-read/"+actor.UserId;
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sp_getapplock @Resource={scope},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Notification lock unavailable',1;",token);
        var notices=await Notices(db,actor,token);if(!notices.Any(x=>x.Id==id))throw new QuoteOperationException(404,"notification-not-found");
        if(notices.Single(x=>x.Id==id).Read){await tx.CommitAsync(token);return;}
        var version=await db.Set<SettingVersion>().Where(x=>x.Scope==scope).Select(x=>(int?)x.Version).MaxAsync(token)??0;
        var previous=await db.Set<SettingVersion>().Where(x=>x.Scope==scope).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);var ids=(previous==null?[]:JsonSerializer.Deserialize<Guid[]>(previous.Values)!).Append(id).Distinct().ToArray();if(ids.Length>10000)throw new QuoteOperationException(422,"notification-history-limit");db.Add(new SettingVersion{Scope=scope,Version=version+1,EffectiveFrom=time.GetUtcNow(),CreatedBy=actor.UserId,Values=JsonSerializer.Serialize(ids)});await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
}
