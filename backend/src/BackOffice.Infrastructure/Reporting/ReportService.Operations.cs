using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
public sealed partial class ReportService
{
 private static IQueryable<ReportSource> Referrals(BackOfficeDbContext db,DateTimeOffset start,DateTimeOffset end,DateTimeOffset now)
 =>from r in db.Set<QuoteReferral>() join q in db.Set<Quote>() on r.QuoteId equals q.Id
   join row in QuoteDiscovery.Rows(db) on q.Id equals row.Id
   join revision in db.Set<QuoteRevision>() on row.RevisionId equals revision.Id
   join pv in db.Set<ProductVersion>() on revision.ProductVersionId equals pv.Id
   from decision in db.Set<QuoteReferralDecision>().Where(d=>d.Id==r.LatestDecisionId&&d.ReferralId==r.Id&&d.CycleId==r.CycleId&&d.DecidedAt<=now).DefaultIfEmpty()
   where r.CycleId==q.CurrentUnderwritingCycleId&&r.CreatedAt>=start&&r.CreatedAt<end
   select new ReportSource{Id=q.Id,Kind="quote",Reference=q.Reference,Label=r.RuleCode,State=r.State,SourceId=r.Id,SourceRuleId=decision==null?null:decision.Id,AgencyId=q.AgencyId,ProviderId=pv.ProviderId,ProductCode=row.ProductCode,UnderwriterId=r.AssignedUserId,At=r.CreatedAt,Decided=decision==null?0:1,Hours=decision==null?0:EF.Functions.DateDiffSecond(r.CreatedAt,decision.DecidedAt)/3600m};
 private static IQueryable<ReportSource> ServiceTasks(BackOfficeDbContext db,ActorContext actor,DateTimeOffset start,DateTimeOffset end,bool exceptions=false)
 =>from task in TaskDiscovery.Rows(db,actor) join s in db.Set<OperationalSubject>() on task.SubjectId equals s.Id
   let policyId=s.PolicyId??db.Set<ServicingDraft>().Where(d=>d.Id==s.ServicingDraftId).Select(d=>(Guid?)d.PolicyId).FirstOrDefault()
   let quoteId=s.QuoteId??db.Set<Policy>().Where(p=>p.Id==policyId).Select(p=>(Guid?)p.SourceQuoteId).FirstOrDefault()
   from q in db.Set<Quote>().Where(q=>q.Id==quoteId).DefaultIfEmpty()
   let agency=s.AgencyId??db.Set<ClientAgencyRelationship>().Where(r=>r.Id==s.RelationshipId).Select(r=>(Guid?)r.AgencyId).FirstOrDefault()??(q==null?null:(Guid?)q.AgencyId)
   where task.CreatedAt>=start&&task.CreatedAt<end&&(!exceptions||task.SourceChanged||task.TypeCode=="complaint")
   select new ReportSource{Id=task.Id,Kind="task",Reference=task.Reference,Label=exceptions?(task.TypeCode=="complaint"?"Recorded complaint task":"Source changed task"):task.TypeCode,State=task.State,SourceId=task.Id,AgencyId=agency,ProductCode=q==null?null:db.Set<Product>().Where(p=>p.Id==q.ProductId).Select(p=>p.Code).FirstOrDefault(),ProviderId=q==null?null:db.Set<QuoteRevision>().Where(r=>r.Id==q.CurrentRevisionId).Select(r=>db.Set<ProductVersion>().Where(v=>v.Id==r.ProductVersionId).Select(v=>(Guid?)v.ProviderId).FirstOrDefault()).FirstOrDefault(),UnderwriterId=task.OwnerId,At=task.CreatedAt,Count=exceptions?1:0,Tasks=1,Completed=task.State=="completed"?1:0,ServiceHours=task.State=="completed"&&task.UpdatedAt>=task.CreatedAt?EF.Functions.DateDiffSecond(task.CreatedAt,task.UpdatedAt)/3600m:0};
 private static IQueryable<ReportSource> SupportReviews(BackOfficeDbContext db,ActorContext actor,DateTimeOffset start,DateTimeOffset end,DateOnly today)
 =>from flag in new SupportFlagScope(actor).InternalFlags(db) join c in db.Set<ClientAccount>() on flag.ClientId equals c.Id
   join relationship in db.Set<ClientAgencyRelationship>() on flag.OriginRelationshipId equals relationship.Id
   where flag.CreatedAt>=start&&flag.CreatedAt<end&&flag.EndedAt==null&&flag.ReviewOn<today
   select new ReportSource{Id=c.Id,Kind="client",Reference=c.Reference,Label="Support review overdue",State="review-overdue",SourceId=flag.Id,AgencyId=relationship.AgencyId,At=flag.CreatedAt};
 private static IQueryable<ReportSource> MidExceptions(BackOfficeDbContext db,DateTimeOffset start,DateTimeOffset end)
 =>from s in db.Set<MidSubmission>() join w in db.Set<OutboxWork>() on s.WorkId equals w.Id
   join p in db.Set<Policy>() on s.PolicyId equals p.Id
   join q in db.Set<Quote>() on p.SourceQuoteId equals q.Id
   where s.CreatedAt>=start&&s.CreatedAt<end&&w.State=="failed"&&q.BoundPolicyId==p.Id&&q.ClientId==p.ClientId&&q.AgencyId==p.AgencyId&&q.RelationshipId==p.RelationshipId
   select new ReportSource{Id=p.Id,Kind="policy",Reference=p.Reference,Label="Failed MID submission",State="failed",SourceId=s.Id,AgencyId=p.AgencyId,At=s.CreatedAt};
}
