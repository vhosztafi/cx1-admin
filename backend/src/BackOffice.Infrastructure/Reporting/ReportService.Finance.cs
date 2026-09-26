using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
public sealed partial class ReportService
{
 private static IQueryable<ReportSource> Written(BackOfficeDbContext db,ReportFilters f,DateTimeOffset start,DateTimeOffset end,DateTimeOffset now)
 =>from j in db.Set<Journal>() join o in db.Set<IssueFinancialObligation>() on j.ObligationId equals o.Id
   join t in db.Set<PolicyTransaction>() on j.TransactionId equals t.Id
   join p in db.Set<Policy>() on o.PolicyId equals p.Id
   join term in db.Set<PolicyTerm>() on o.TermId equals term.Id
   join product in db.Set<Product>() on term.ProductId equals product.Id
   where j.PostedAt!=null&&j.PostedAt<=now&&j.TransactionId==o.TransactionId&&
    (f.Basis=="effective"?t.EffectiveAt<end:(j.PostingDate!=null?j.PostingDate<=f.To:j.PostedAt<end))
   let inWindow=f.Basis=="effective"?t.EffectiveAt>=start:(j.PostingDate!=null?j.PostingDate>=f.From:j.PostedAt>=start)
   select new ReportSource{Id=t.Id,Kind="transaction",Reference=p.Reference,Label=inWindow?t.Kind:"Opening balance source: "+t.Kind,State="posted",Href="/accounting?tab=transactions&agencyId="+o.AgencyId+"&transactionId="+t.Id,SourceId=j.Id,AgencyId=o.AgencyId,ProviderId=o.ProviderId,ProductCode=product.Code,At=f.Basis=="effective"?t.EffectiveAt:j.PostedAt!.Value,BasisDate=f.Basis=="processed"?j.PostingDate:null,Count=inWindow?1:0,Premium=inWindow?o.Premium:0,Commission=inWindow?o.Commission:0,Debt=inWindow?o.InvoiceDue:0,ClosingDebt=o.InvoiceDue};
 private static IQueryable<ReportSource> CashMovements(BackOfficeDbContext db,ReportFilters f,DateTimeOffset start,DateTimeOffset end,DateTimeOffset now)
 =>db.Set<FinancePosting>().Where(p=>p.PostedAt<=now&&(f.Basis=="effective"?p.EffectiveAt<end:p.PostingDate<=f.To)).Select(p=>new ReportSource{Id=p.Id,Kind="posting",Reference=p.SourceKind,Label=(f.Basis=="effective"?p.EffectiveAt<start:p.PostingDate<f.From)?"Opening balance source":"Finance movement",State="posted",Href="/accounting?tab=transactions&agencyId="+p.AgencyId,SourceId=p.SourceId,AgencyId=p.AgencyId,At=f.Basis=="effective"?p.EffectiveAt:p.PostedAt,BasisDate=f.Basis=="processed"?p.PostingDate:null,Count=(f.Basis=="effective"?p.EffectiveAt>=start:p.PostingDate>=f.From)?1:0,Cash=(f.Basis=="effective"?p.EffectiveAt>=start:p.PostingDate>=f.From)?p.CashDelta:0,Debt=(f.Basis=="effective"?p.EffectiveAt>=start:p.PostingDate>=f.From)?p.DebtorDelta:0,ClosingDebt=p.DebtorDelta});
 private static IQueryable<ReportSource> Bordereaux(BackOfficeDbContext db,ReportFilters f,DateTimeOffset now)
 =>from batch in db.Set<FinanceBordereauBatch>() join version in db.Set<FinanceBordereauVersion>() on batch.CurrentVersionId equals version.Id
   join m in db.Set<FinanceBordereauMember>() on version.Id equals m.VersionId
   join j in db.Set<Journal>() on m.SourceJournalId equals j.Id
   join o in db.Set<IssueFinancialObligation>() on j.ObligationId equals o.Id
   join pv in db.Set<ProductVersion>() on m.ProductVersionId equals pv.Id
   join product in db.Set<Product>() on pv.ProductId equals product.Id
   where batch.Id==db.Set<FinanceBordereauBatch>().Where(b=>b.ProviderId==batch.ProviderId&&b.AccountingPeriodId==batch.AccountingPeriodId).OrderByDescending(b=>b.CreatedAt).ThenByDescending(b=>b.Id).Select(b=>b.Id).First()&&version.BatchId==batch.Id&&m.ExcludedAt==null&&m.PostingDate>=f.From&&m.PostingDate<=f.To&&m.PostedAt<=now&&j.PostedAt!=null&&j.TransactionId==m.TransactionId
   select new ReportSource{Id=batch.Id,Kind="bordereau",Reference=m.PolicyReference,Label="Batch version "+version.Number,State=version.State,Href="/accounting?tab=bordereaux&batchId="+batch.Id,SourceId=m.Id,SourceRuleId=version.Id,AgencyId=m.AgencyId,ProviderId=batch.ProviderId,ProductCode=product.Code,At=m.PostedAt,BasisDate=m.PostingDate,Premium=m.Premium,SourcePremium=o.Premium,Difference=m.Premium-o.Premium};
}

