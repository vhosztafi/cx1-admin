using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
public sealed partial class ReportService
{
 private static IQueryable<ReportSource> InsuranceQuotes(BackOfficeDbContext db,DateTimeOffset start,DateTimeOffset to)
 =>from row in QuoteDiscovery.Rows(db)
   join q in db.Set<Quote>() on row.Id equals q.Id
   join revision in db.Set<QuoteRevision>() on row.RevisionId equals revision.Id
   join version in db.Set<ProductVersion>() on revision.ProductVersionId equals version.Id
   where q.CreatedAt>=start&&q.CreatedAt<to
   select new ReportSource{Id=q.Id,Kind="quote",Reference=q.Reference,Label=row.ClientName,State=q.State,SourceId=revision.Id,AgencyId=q.AgencyId,ProviderId=version.ProviderId,ProductCode=row.ProductCode,UnderwriterId=q.AssignedUserId,At=q.CreatedAt,
    Bound=db.Set<PolicyTransaction>().Any(t=>t.SourceQuoteId==q.Id&&t.Kind=="new-business")?1:0,
    Referred=db.Set<QuoteReferral>().Any(r=>r.QuoteId==q.Id&&r.CycleId==q.CurrentUnderwritingCycleId)?1:0,
    Declined=q.State=="declined"?1:0,
    Premium=db.Set<QuoteRatingResult>().Where(r=>db.Set<PolicyTransaction>().Any(t=>t.SourceQuoteId==q.Id&&t.Kind=="new-business"&&t.RatingId==r.Id)).Sum(r=>(decimal?)r.TermPremium)??0};
 private static IQueryable<ReportSource> Portfolio(BackOfficeDbContext db,ReportFilters f,DateTimeOffset start,DateTimeOffset to,DateTimeOffset now)
 {
  var cutoff=to<now?to.AddTicks(-1):now;
  var selected=PolicyDiscoveryService.Rows(db,cutoff,now);
  return from t in db.Set<PolicyTransaction>() join p in db.Set<Policy>() on t.PolicyId equals p.Id
   join term in db.Set<PolicyTerm>() on t.TermId equals term.Id
   join product in db.Set<Product>() on term.ProductId equals product.Id
   join version in db.Set<ProductVersion>() on term.ProductVersionId equals version.Id
   join q in db.Set<Quote>() on p.SourceQuoteId equals q.Id
   where q.BoundPolicyId==p.Id&&q.ClientId==p.ClientId&&q.RelationshipId==p.RelationshipId&&q.AgencyId==p.AgencyId&&t.ProcessedAt<=now&&
    (f.Basis=="effective"?t.EffectiveAt>=start&&t.EffectiveAt<to:t.ProcessedAt>=start&&t.ProcessedAt<to)
   select new ReportSource{Id=p.Id,Kind="policy",Reference=p.Reference,Label=t.Kind,State=selected.Where(x=>x.Id==p.Id).Select(x=>x.State).FirstOrDefault()??"unavailable",SourceId=t.Id,AgencyId=p.AgencyId,ProviderId=version.ProviderId,ProductCode=product.Code,UnderwriterId=q.AssignedUserId,At=f.Basis=="effective"?t.EffectiveAt:t.ProcessedAt,NewBusiness=t.Kind=="new-business"?1:0,Cancelled=t.Kind=="cancellation"?1:0,
    Premium=db.Set<IssueFinancialObligation>().Where(o=>o.TransactionId==t.Id&&db.Set<Journal>().Any(j=>j.ObligationId==o.Id&&j.PostedAt!=null&&j.PostedAt<=now)).Sum(o=>(decimal?)o.Premium)??0};
 }
 private static IQueryable<ReportSource> ActivePortfolio(BackOfficeDbContext db,DateTimeOffset start,DateTimeOffset end,DateTimeOffset now)
 =>from row in PolicyDiscoveryService.Rows(db,end.AddTicks(-1),now)
   join p in db.Set<Policy>() on row.Id equals p.Id
   join q in db.Set<Quote>() on p.SourceQuoteId equals q.Id
   join term in db.Set<PolicyTerm>() on row.CurrentTermId equals term.Id
   join version in db.Set<ProductVersion>() on term.ProductVersionId equals version.Id
   where row.State=="active"&&row.EndsAt>start&&q.BoundPolicyId==p.Id&&q.ClientId==p.ClientId&&q.AgencyId==p.AgencyId&&q.RelationshipId==p.RelationshipId
   select new ReportSource{Id=p.Id,Kind="policy",Reference=p.Reference,Label=row.ClientName,State=row.State,SourceId=row.CurrentVersionId,AgencyId=p.AgencyId,ProviderId=version.ProviderId,ProductCode=row.ProductCode,UnderwriterId=q.AssignedUserId,At=end.AddTicks(-1)};
 private static IQueryable<ReportSource> Renewals(BackOfficeDbContext db,string code,DateTimeOffset start,DateTimeOffset to,DateTimeOffset now)
 {
  var terms=db.Set<PolicyTerm>().Where(t=>t.EndsAt>=start&&t.EndsAt<to&&db.Set<PolicyTransaction>().Any(x=>x.TermId==t.Id&&x.PolicyId==t.PolicyId&&x.ProcessedAt<=now&&(x.Kind=="new-business"||x.Kind=="renewal"))&&!db.Set<PolicyTransaction>().Any(x=>x.TermId==t.Id&&x.Kind=="cancellation"&&x.ProcessedAt<=now&&x.EffectiveAt<t.EndsAt));
  var query=from term in terms join p in db.Set<Policy>() on term.PolicyId equals p.Id
   join q in db.Set<Quote>() on p.SourceQuoteId equals q.Id
   join product in db.Set<Product>() on term.ProductId equals product.Id
   join version in db.Set<ProductVersion>() on term.ProductVersionId equals version.Id
   where q.BoundPolicyId==p.Id&&q.ClientId==p.ClientId&&q.AgencyId==p.AgencyId&&q.RelationshipId==p.RelationshipId
   let renewed=db.Set<PolicyTerm>().Any(n=>n.PolicyId==p.Id&&n.StartsAt==term.EndsAt&&db.Set<PolicyTransaction>().Any(t=>t.TermId==n.Id&&t.Kind=="renewal"&&t.ProcessedAt<=now))
   let invited=db.Set<ServicingDraft>().Any(d=>d.PolicyId==p.Id&&d.BaseTermId==term.Id&&d.Kind=="renewal"&&d.State!="abandoned"&&db.Set<ServicingTermsDelivery>().Any(v=>v.DraftId==d.Id&&v.CycleId==d.CurrentCycleId&&v.State=="delivered"&&v.CompletedAt<=now))
   let lapsed=db.Set<RenewalLapseEvent>().Any(l=>l.TermId==term.Id&&l.CreatedAt<=now)
   where code!="renewal-invitations"||term.EndsAt>now&&!renewed&&!invited&&!lapsed&&!db.Set<ServicingDraft>().Any(d=>d.BaseTermId==term.Id&&d.Kind=="renewal"&&d.State=="draft"&&db.Set<ServicingCycle>().Any(c=>c.Id==d.CurrentCycleId&&c.CurrentAcceptanceId!=null))
   select new ReportSource{Id=p.Id,Kind="policy",Reference=p.Reference,Label="Term "+term.Number,State=renewed?"renewed":lapsed?"lapsed":invited?"invited":"not-invited",SourceId=term.Id,AgencyId=p.AgencyId,ProviderId=version.ProviderId,ProductCode=product.Code,UnderwriterId=q.AssignedUserId,At=term.EndsAt,Renewed=renewed?1:0,Invited=invited?1:0,Lapsed=lapsed?1:0,Expired=term.EndsAt<=now?1:0,RenewedExpired=term.EndsAt<=now&&renewed?1:0};
  return query;
 }
}
