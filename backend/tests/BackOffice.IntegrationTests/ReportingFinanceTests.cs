using System.Globalization;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
 [Fact]
 public async Task RealSqlReportingFinanceOperationsReconcileSealedSources()
 {
  await WithDatabase(async(db,password)=>{
   var setup=await AcceptedIssue(db,password);var f=setup.Source;var issued=await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
   var actor=await FinanceLedgerActor(db);var service=new ReportService(f.Factory,f.Clock);Assert.Equal(3,(await service.CatalogueAsync(actor)).Length);
   var o=await db.Set<IssueFinancialObligation>().SingleAsync(x=>x.PolicyId==issued.ResourceId);var j=await db.Set<Journal>().SingleAsync(x=>x.ObligationId==o.Id);
   var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(j.PostedAt!.Value,"Europe/London").DateTime);var filters=new ReportFilters(day,day,AgencyId:o.AgencyId);
   Guid Id(string code)=>ReportCatalogue.All.Single(x=>x.Code==code).Id;
   var result=await service.RunAsync(actor,Id("finance"),filters);Assert.Single(result.Rows);Assert.Equal(o.Premium,decimal.Parse(result.Measures.Single(x=>x.Code=="premium").Value!,CultureInfo.InvariantCulture));Assert.Equal(o.InvoiceDue,decimal.Parse(result.Measures.Single(x=>x.Code=="closingDebt").Value!,CultureInfo.InvariantCulture));
   var opening=await service.RunAsync(actor,Id("finance"),filters with{From=day.AddDays(1),To=day.AddDays(2)});Assert.Equal("0",opening.Measures.Single(x=>x.Code=="debt").Value);Assert.Equal(o.InvoiceDue,decimal.Parse(opening.Measures.Single(x=>x.Code=="closingDebt").Value!,CultureInfo.InvariantCulture));
   Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.RunAsync(f.Underwriter,Id("finance"),filters))).Status);
   foreach(var code in new[]{"agency-conversion","referral-turnaround","compliance"})await service.RunAsync(f.Underwriter,Id(code),filters);
   var period=await db.Set<AccountingPeriod>().SingleAsync(x=>x.Id==j.AccountingPeriodId);var month=new ReportFilters(period.StartsOn,period.EndsOn.AddDays(-1),"effective",o.AgencyId);
   var later=new AdministrationClock(new DateTimeOffset(period.EndsOn.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero).AddDays(2));var earnedService=new ReportService(f.Factory,later);
   var earned=await earnedService.RunAsync(actor,Id("earned-premium"),month);Assert.NotEmpty(earned.Rows);var expected=await db.Set<FinanceEarningSlice>().Where(x=>x.AgencyId==o.AgencyId&&x.MonthStart>=period.StartsOn&&x.MonthStart<period.EndsOn).SumAsync(x=>x.EarnedPence);Assert.Equal(expected/100m,decimal.Parse(earned.Measures.Single(x=>x.Code=="earned").Value!,CultureInfo.InvariantCulture));
   var bord=new FinanceBordereauService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);await bord.GenerateAsync(actor,o.ProviderId,period.Id,Guid.NewGuid().ToString(),Guid.NewGuid());
   var reconciled=await service.RunAsync(actor,Id("bordereau-reconciliation"),month with{Basis="processed"});Assert.NotEmpty(reconciled.Rows);Assert.Equal("0",reconciled.Measures.Single(x=>x.Code=="difference").Value);
   var batch=await db.Set<FinanceBordereauBatch>().SingleAsync();await bord.ValidateAsync(actor,batch.Id,batch.CurrentVersionId,Guid.NewGuid().ToString(),Guid.NewGuid());
   await new FinanceStatementService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock).GenerateAsync(actor,o.AgencyId,period.StartsOn,period.EndsOn,Guid.NewGuid().ToString(),Guid.NewGuid());
   var closer=new FinancePeriodService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);var review=await closer.ReviewAsync(actor,period.Id);Assert.Empty(review.Blockers);await closer.CloseAsync(actor,period.Id,Convert.FromBase64String(review.Etag.Trim('"')),"Reviewed reporting reconciliation and sealed period",Guid.NewGuid().ToString(),Guid.NewGuid());
   var closed=await earnedService.RunAsync(actor,Id("earned-premium"),month);Assert.Equal(earned.Measures.Single(x=>x.Code=="earned").Value,closed.Measures.Single(x=>x.Code=="earned").Value);Assert.All(closed.Rows,x=>Assert.Equal("closed",x.State));
   var receipts=new FinanceReceiptService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock);var receipt=await receipts.RecordAsync(actor,o.AgencyId,"10.00","GBP",day,"Fictional report cash","manual",Guid.NewGuid(),"agency",o.AgencyId,Guid.NewGuid().ToString(),Guid.NewGuid());var receiptView=await receipts.DetailAsync(actor,receipt.ResourceId);await receipts.AllocateAsync(actor,receipt.ResourceId,receiptView.AssignmentId,[new(o.Id,"10.00")],Guid.NewGuid().ToString(),Guid.NewGuid());
   var cash=await service.RunAsync(actor,Id("finance"),filters with{From=receiptView.PostingDate,To=receiptView.PostingDate});Assert.Equal("10",cash.Measures.Single(x=>x.Code=="cash").Value);Assert.Equal(o.InvoiceDue-10m,decimal.Parse(cash.Measures.Single(x=>x.Code=="closingDebt").Value!,CultureInfo.InvariantCulture));Assert.Contains(cash.Rows,x=>x.Values["debt"]=="-10");
   var subject=new OperationalSubject{Kind="agency",AgencyId=o.AgencyId,CreatedBy=f.Underwriter.UserId};db.Add(subject);await db.SaveChangesAsync();var task=new OperationalTask{SubjectId=subject.Id,Reference="TK-RPT-COMPLAINT",TypeCode="complaint",Title="Private complaint narrative",CreatedBy=f.Underwriter.UserId,OwnerId=f.Underwriter.UserId,EventSequence=1,CreatedAt=j.PostedAt.Value};db.Add(task);await db.SaveChangesAsync();
   var compliance=await service.RunAsync(f.Underwriter,Id("compliance"),filters);Assert.Contains(compliance.Rows,x=>x.RecordId==task.Id);Assert.DoesNotContain("Private complaint narrative",System.Text.Json.JsonSerializer.Serialize(compliance));
  });
 }
}

