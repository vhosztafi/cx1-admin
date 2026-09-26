using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Reporting;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
 [Fact]
 public async Task RealSqlReportingDashboardReconcilesOwnQueuesAndPersistsPrivateAlerts()
 {
  await WithDatabase(async(db,password)=>{
   await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeOperationalWorkflows:true);
   var u=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="underwriter@cover.example");var actor=new ActorContext(u.Id,u.TeamId,null,new HashSet<string>{"underwriter"});
   var agency=await db.Set<Agency>().FirstAsync();var subject=new OperationalSubject{Kind="agency",AgencyId=agency.Id,CreatedBy=u.Id};db.Add(subject);await db.SaveChangesAsync();
   var task=new OperationalTask{SubjectId=subject.Id,Reference="TK-RPT-01",TypeCode="underwriting",CreatedBy=u.Id,EventSequence=1,Title="Fictional reporting review",OwnerId=u.Id,DueOn=DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1)};db.Add(task);await db.SaveChangesAsync();
   var service=new DashboardService(new AdministrationFactory(db.Database.GetConnectionString()!),new AdministrationClock(Now));
   var data=JsonSerializer.SerializeToElement(await service.ReadAsync(actor));var queues=data.GetProperty("queues").EnumerateArray().ToArray();var own=queues.Single(x=>x.GetProperty("Code").GetString()=="tasks");
   Assert.True(own.GetProperty("Count").GetInt32()>0);Assert.Contains(own.GetProperty("Items").EnumerateArray(),x=>x.GetProperty("Id").GetGuid()==task.Id);Assert.True(own.GetProperty("Overdue").GetInt32()>0);
   Assert.Equal(JsonValueKind.Null,queues.Single(x=>x.GetProperty("Code").GetString()=="failed-jobs").GetProperty("Count").ValueKind);
   var notice=(await service.NoticesAsync(actor)).Single(x=>x.Id==task.Id);Assert.False(notice.Read);await service.MarkReadAsync(actor,task.Id);Assert.True((await service.NoticesAsync(actor)).Single(x=>x.Id==task.Id).Read);
   var other=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");var foreign=new ActorContext(other.Id,other.TeamId,null,new HashSet<string>{"senior-underwriter"});
   Assert.DoesNotContain(await service.NoticesAsync(foreign),x=>x.Id==task.Id);Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.MarkReadAsync(foreign,task.Id))).Status);
   await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={actor.UserId}");Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReadAsync(actor))).Status);
  });
 }
}
