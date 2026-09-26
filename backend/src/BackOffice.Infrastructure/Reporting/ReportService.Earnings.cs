using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
public sealed partial class ReportService
{
 private static async Task<ReportSource[]> Earned(BackOfficeDbContext db,ReportFilters f,DateTimeOffset now,CancellationToken token)
 {
  var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"Europe/London").DateTime);
  if(f.To>=new DateOnly(today.Year,today.Month,1))throw new QuoteOperationException(400,"earned-report-month-not-complete");
  var start=Utc(f.From);var end=Utc(f.To.AddDays(1));
  var selected=await (from component in db.Set<IssueFinancialComponent>()
   join o in db.Set<IssueFinancialObligation>() on component.ObligationId equals o.Id
   join j in db.Set<Journal>() on o.Id equals j.ObligationId
   join p in db.Set<Policy>() on o.PolicyId equals p.Id
   join term in db.Set<PolicyTerm>() on o.TermId equals term.Id
   join product in db.Set<Product>() on term.ProductId equals product.Id
   where component.Code=="premium"&&component.TransactionId==o.TransactionId&&j.TransactionId==component.TransactionId&&component.CoverageStartsAt<end&&component.CoverageEndsAt>start&&j.PostedAt!=null&&j.PostedAt<=now&&
    (f.AgencyId==null||o.AgencyId==f.AgencyId)&&(f.ProviderId==null||o.ProviderId==f.ProviderId)&&(f.ProductCode==null||product.Code==f.ProductCode)
   orderby component.Id
   select new {Component=component,Obligation=o,PostedAt=j.PostedAt!.Value,PolicyReference=p.Reference,ProductCode=product.Code}).Take(10001).ToArrayAsync(token);
  if(selected.Length>10000)throw new QuoteOperationException(422,"report-too-large-narrow-filters");
  var ids=selected.Select(x=>x.Component.Id).ToArray();
  var saved=await db.Set<FinanceEarningSlice>().Where(x=>ids.Contains(x.SourceComponentId)).OrderBy(x=>x.MonthStart).Take(200001).ToArrayAsync(token);
  if(saved.Length>200000)throw new QuoteOperationException(422,"report-too-large-narrow-filters");
  var periods=await db.Set<AccountingPeriod>().Where(x=>x.StartsOn<=f.To&&x.EndsOn>f.From).ToArrayAsync(token);
  var bySource=saved.GroupBy(x=>x.SourceComponentId).ToDictionary(g=>g.Key,g=>g.ToArray());var result=new List<ReportSource>();
  foreach(var source in selected)
  {
   var component=source.Component;var expected=FinanceEarningMath.Allocate(FinanceLedgerMath.Pence(component.Amount),component.CoverageStartsAt,component.CoverageEndsAt);var hash=FinanceEarningMath.SourceHash(component,source.PostedAt);
   if(!bySource.TryGetValue(component.Id,out var slices)||slices.Length!=expected.Count||slices.Where((s,i)=>s.MonthStart!=expected[i].MonthStart||s.EarnedPence!=expected[i].EarnedPence||s.PremiumPence!=FinanceLedgerMath.Pence(component.Amount)||s.ObligationId!=source.Obligation.Id||s.TransactionId!=component.TransactionId||s.AgencyId!=source.Obligation.AgencyId||s.PolicyId!=source.Obligation.PolicyId||s.CoverageStartsAt!=component.CoverageStartsAt||s.CoverageEndsAt!=component.CoverageEndsAt||s.SourcePostedAt!=source.PostedAt||s.AlgorithmVersion!=1||!s.SourceHash.SequenceEqual(hash)).Any())throw new QuoteOperationException(409,"finance-earning-incomplete");
   foreach(var slice in slices.Where(s=>s.MonthStart>=f.From&&s.MonthStart<=f.To))
   {
    var period=periods.SingleOrDefault(p=>p.StartsOn<=slice.MonthStart&&p.EndsOn>=slice.MonthStart.AddMonths(1))??throw new QuoteOperationException(409,"earned-report-month-period-unavailable");
    var cutoff=period.State=="closed"?period.SourceCutoff??throw new QuoteOperationException(409,"finance-earning-cutoff-missing"):now;if(source.PostedAt>cutoff)continue;
    result.Add(new ReportSource{Id=slice.TransactionId,Kind="transaction",Reference=source.PolicyReference,Label=$"{slice.MonthStart:yyyy-MM} · earning algorithm {slice.AlgorithmVersion}",State=period.State,Href=$"/accounting?tab=transactions&agencyId={source.Obligation.AgencyId}&transactionId={slice.TransactionId}",SourceId=slice.Id,SourceRuleId=period.Id,AgencyId=source.Obligation.AgencyId,ProviderId=source.Obligation.ProviderId,ProductCode=source.ProductCode,At=slice.SourcePostedAt,BasisDate=slice.MonthStart,Earned=slice.EarnedPence/100m});
    if(result.Count>10000)throw new QuoteOperationException(422,"report-too-large-narrow-filters");
   }
  }
  return result.OrderBy(x=>x.BasisDate).ThenBy(x=>x.Id).ThenBy(x=>x.SourceId).ToArray();
 }
}


