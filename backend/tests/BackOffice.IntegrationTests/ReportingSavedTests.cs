using System.Text;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
 [Fact]
 public async Task RealSqlReportingSavedExportsAreOwnedVersionedCompleteAndCurrentlyAuthorized()
 {
  await WithDatabase(async(db,password)=>{
   await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeOperationalWorkflows:true);
   var u=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="underwriter@cover.example");var actor=new ActorContext(u.Id,u.TeamId,null,new HashSet<string>{"underwriter"});var factory=new AdministrationFactory(db.Database.GetConnectionString()!);var clock=new AdministrationClock(Now);
   var service=new ReportService(factory,clock);var saved=new SavedReportService(factory,clock);var export=new ReportExportService(factory,clock);var id=ReportCatalogue.All.Single(x=>x.Code=="agency-conversion").Id;var day=DateOnly.FromDateTime(Now.UtcDateTime);var filters=new ReportFilters(day.AddDays(-1),day.AddDays(1));
   var agency=await db.Set<Agency>().FirstAsync();var subject=new OperationalSubject{Kind="agency",AgencyId=agency.Id,CreatedBy=u.Id};db.Add(subject);await db.SaveChangesAsync();
   for(var i=0;i<55;i++)db.Add(new OperationalTask{SubjectId=subject.Id,Reference=i==0?"=FORMULA()":"TK-CSV-"+i,Title="Private task narrative",TypeCode="underwriting",CreatedBy=u.Id,OwnerId=u.Id,CreatedAt=Now.AddHours(-6),EventSequence=1});await db.SaveChangesAsync();
   var completed=await db.Set<OperationalTask>().FirstAsync(x=>x.SubjectId==subject.Id);completed.State="completed";completed.CompletionReason="Fictional service completed";completed.UpdatedAt=Now.AddHours(-1);completed.EventSequence=2;db.Add(new OperationalTaskEvent{TaskId=completed.Id,Sequence=2,Kind="task.transitioned",ActorLabel="Demo underwriter",CreatedBy=u.Id,CreatedAt=Now.AddHours(-4),SnapshotJson="{\"state\":\"completed\"}"});await db.SaveChangesAsync();
   var first=await saved.SaveAsync(actor,null,new(0,id,"Agency review",filters));var loaded=await saved.ReadAsync(actor);Assert.Single(loaded.Favourites);Assert.Equal(first.Id,loaded.Favourites[0].Id);
   Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>saved.SaveAsync(actor,first.Id,new(0,id,"Stale change",filters)))).Status);
   var other=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");var foreign=new ActorContext(other.Id,other.TeamId,null,new HashSet<string>{"senior-underwriter"});Assert.Empty((await saved.ReadAsync(foreign)).Favourites);Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>saved.DeleteAsync(foreign,first.Id,loaded.Version))).Status);
   var run=await service.RunAsync(actor,id,filters);Assert.Equal("2",run.Measures.Single(x=>x.Code=="serviceTurnaround").Value);Assert.Equal(50,run.Rows.Length);Assert.True(run.TotalRows>=55);Assert.Single((await saved.ReadAsync(actor)).Recent);
   var csv=Encoding.UTF8.GetString((await export.ExportAsync(actor,id,new(filters with{Offset=50}))).Content);Assert.Equal(run.TotalRows+1,csv.Split("\r\n",StringSplitOptions.RemoveEmptyEntries).Length);Assert.Contains("\"'=FORMULA()\"",csv);Assert.DoesNotContain("Private task narrative",csv);Assert.DoesNotContain("RiskSnapshot",csv);
   await saved.SaveAsync(actor,first.Id,new(loaded.Version,id,"Renamed review",filters));loaded=await saved.ReadAsync(actor);Assert.Equal("Renamed review",loaded.Favourites[0].Name);await saved.DeleteAsync(actor,first.Id,loaded.Version);Assert.Empty((await saved.ReadAsync(actor)).Favourites);
   Assert.Equal(400,(await Assert.ThrowsAsync<QuoteOperationException>(()=>export.ExportAsync(actor,id,new(filters,"pdf")))).Status);
   var finance=await FinanceLedgerActor(db);Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>export.ExportAsync(finance,id,new(filters)))).Status);Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>saved.ReadAsync(actor with{AgencyId=agency.Id}))).Status);
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>export.ExportAsync(actor,id,new(filters)))).Status);Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>saved.ReadAsync(actor))).Status);
  });
 }
 [Theory]
 [InlineData(" =SUM(1)","\"' =SUM(1)\"")]
 [InlineData("\tmalicious","\"'\tmalicious\"")]
 [InlineData("safe,\"quoted\"","\"safe,\"\"quoted\"\"\"")]
 public void ReportingCsvNeutralizesFormulaText(string input,string expected)=>Assert.Equal(expected,ReportExportService.Cell(input));
 [Fact] public void ReportingCsvPreservesSignedNumericMoney()=>Assert.Equal("\"-10.25\"",ReportExportService.Cell("-10.25",true));
}

