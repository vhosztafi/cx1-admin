using System.Globalization;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
 [Fact]
 public async Task RealSqlReportingInsuranceCohortsReconcilePinnedIssueAndRenewalDenominators()
 {
  await WithDatabase(async(db,password)=>{
   var setup=await AcceptedIssue(db,password);var f=setup.Source;var issued=await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
   var service=new ReportService(f.Factory,f.Clock);var definitions=await service.CatalogueAsync(f.Servicing);Assert.Equal(5,definitions.Length);
   var created=await db.Set<Quote>().Where(x=>x.Id==f.QuoteId).Select(x=>x.CreatedAt).SingleAsync();var day=DateOnly.FromDateTime(created.UtcDateTime);
   var filters=new ReportFilters(day.AddDays(-1),day.AddDays(1));var report=await service.RunAsync(f.Servicing,ReportCatalogue.All.Single(x=>x.Code=="underwriting").Id,filters);
   Assert.Contains(report.Rows,x=>x.RecordId==f.QuoteId);Assert.Equal("1",report.Measures.Single(x=>x.Code=="bound").Value);Assert.Equal("100",report.Measures.Single(x=>x.Code=="conversion").Value);
   var premium=await db.Set<QuoteRatingResult>().Where(r=>db.Set<PolicyTransaction>().Any(t=>t.PolicyId==issued.ResourceId&&t.RatingId==r.Id)).Select(x=>x.TermPremium).SingleAsync();Assert.Equal(premium,decimal.Parse(report.Measures.Single(x=>x.Code=="premium").Value!,CultureInfo.InvariantCulture));
   var empty=await service.RunAsync(f.Servicing,report.ReportId,filters with{AgencyId=Guid.NewGuid()});Assert.Empty(empty.Rows);Assert.Null(empty.Measures.Single(x=>x.Code=="conversion").Value);
   var portfolio=await service.RunAsync(f.Servicing,ReportCatalogue.All.Single(x=>x.Code=="portfolio").Id,filters with{Basis="processed"});Assert.Contains(portfolio.Rows,x=>x.RecordId==issued.ResourceId);Assert.Equal("1",portfolio.Measures.Single(x=>x.Code=="newBusiness").Value);
   var active=await service.RunAsync(f.Servicing,ReportCatalogue.All.Single(x=>x.Code=="active-portfolio").Id,filters with{Basis="effective"});Assert.Single(active.Rows);
   await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,includeRenewalLifecycle:true);
   var term=await db.Set<PolicyTerm>().SingleAsync(x=>x.PolicyId==issued.ResourceId);var end=DateOnly.FromDateTime(term.EndsAt.UtcDateTime);var renewalFilters=new ReportFilters(end.AddDays(-1),end.AddDays(1),"effective");
   var renewal=await service.RunAsync(f.Servicing,ReportCatalogue.All.Single(x=>x.Code=="renewal-retention").Id,renewalFilters);Assert.Single(renewal.Rows);Assert.Null(renewal.Measures.Single(x=>x.Code=="retention").Value);
   var due=await service.RunAsync(f.Servicing,ReportCatalogue.All.Single(x=>x.Code=="renewal-invitations").Id,renewalFilters);Assert.Empty(due.Rows);
   var nearExpiry=new ReportService(f.Factory,new AdministrationClock(term.EndsAt.AddDays(-1)));var dueNear=await nearExpiry.RunAsync(f.Servicing,due.ReportId,renewalFilters);Assert.Single(dueNear.Rows);Assert.NotNull(dueNear.Rows[0].SourceRuleId);
   var finance=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="finance@cover.example");var financeActor=new ActorContext(finance.Id,finance.TeamId,null,new HashSet<string>{"finance"});Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.RunAsync(financeActor,report.ReportId,filters))).Status);
   Assert.Equal(400,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.RunAsync(f.Servicing,report.ReportId,filters with{Basis="effective"}))).Status);
  });
 }
}
