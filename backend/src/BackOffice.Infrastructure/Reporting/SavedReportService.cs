using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
public sealed record SavedReport(Guid Id,Guid ReportId,string Name,ReportFilters Filters);
public sealed record RecentReport(Guid ReportId,ReportFilters Filters,DateTimeOffset RanAt,int TotalRows);
public sealed record ReportPreferences(int Version,SavedReport[] Favourites,RecentReport[] Recent);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveReportInput(int Version,Guid ReportId,string Name,ReportFilters Filters);
public sealed class SavedReportService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
 internal static string Scope(ActorContext actor,string kind)=>"reports/"+kind+"/"+actor.UserId;
 internal static async Task Lock(BackOfficeDbContext db,string scope,CancellationToken token)=>await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sp_getapplock @Resource={scope},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Report preferences lock unavailable',1;",token);
 internal static Task<SettingVersion?> Latest(BackOfficeDbContext db,string scope,CancellationToken token)=>db.Set<SettingVersion>().Where(x=>x.Scope==scope).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
 private static T[] Items<T>(SettingVersion? row)=>row==null?[]:JsonSerializer.Deserialize<T[]>(row.Values)!;
 private static void Append<T>(BackOfficeDbContext db,ActorContext actor,string scope,SettingVersion? previous,T[] items,DateTimeOffset now)=>db.Add(new SettingVersion{Scope=scope,Version=(previous?.Version??0)+1,EffectiveFrom=now,CreatedBy=actor.UserId,Values=JsonSerializer.Serialize(items)});
 public async Task<ReportPreferences> ReadAsync(ActorContext actor,CancellationToken token=default)
 {
  await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);
  var favourites=await Latest(db,Scope(actor,"favourites"),token);var recent=await Latest(db,Scope(actor,"recent"),token);
  bool Allowed(Guid id)=>ReportCatalogue.All.Any(d=>d.Id==id&&actor.HasCapability(d.Capability));
  var result=new ReportPreferences(favourites?.Version??0,Items<SavedReport>(favourites).Where(x=>Allowed(x.ReportId)).ToArray(),Items<RecentReport>(recent).Where(x=>Allowed(x.ReportId)).ToArray());await tx.CommitAsync(token);return result;
 }
 public async Task<SavedReport> SaveAsync(ActorContext actor,Guid? id,SaveReportInput input,CancellationToken token=default)
 {
  if(input.Name==null||string.IsNullOrWhiteSpace(input.Name)||input.Name.Trim().Length>100||input.Filters==null||input.Version<0)throw new QuoteOperationException(400,"saved-report-invalid");
  await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);var d=ReportCatalogue.Authorize(actor,input.ReportId);ReportService.Validate(d,input.Filters);
  var scope=Scope(actor,"favourites");await Lock(db,scope,token);var previous=await Latest(db,scope,token);var list=Items<SavedReport>(previous).ToList();
  if(id!=null){var existing=list.SingleOrDefault(x=>x.Id==id)??throw new QuoteOperationException(404,"saved-report-not-found");ReportCatalogue.Authorize(actor,existing.ReportId);}if(input.Version!=(previous?.Version??0))throw new QuoteOperationException(412,"saved-report-version-changed");
  var saved=new SavedReport(id??Guid.NewGuid(),input.ReportId,input.Name.Trim(),input.Filters with{Offset=0});list.RemoveAll(x=>x.Id==saved.Id);list.Add(saved);if(list.Count>100)throw new QuoteOperationException(422,"saved-report-limit");
  Append(db,actor,scope,previous,list.ToArray(),time.GetUtcNow());await db.SaveChangesAsync(token);await tx.CommitAsync(token);return saved;
 }
 public async Task DeleteAsync(ActorContext actor,Guid id,int version,CancellationToken token=default)
 {
  await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);var scope=Scope(actor,"favourites");await Lock(db,scope,token);var previous=await Latest(db,scope,token);var list=Items<SavedReport>(previous);var saved=list.SingleOrDefault(x=>x.Id==id)??throw new QuoteOperationException(404,"saved-report-not-found");ReportCatalogue.Authorize(actor,saved.ReportId);
  if(version!=(previous?.Version??0))throw new QuoteOperationException(412,"saved-report-version-changed");Append(db,actor,scope,previous,list.Where(x=>x.Id!=id).ToArray(),time.GetUtcNow());await db.SaveChangesAsync(token);await tx.CommitAsync(token);
 }
 internal static async Task RecordRun(BackOfficeDbContext db,ActorContext actor,ReportResult result,CancellationToken token)
 {
  var scope=Scope(actor,"recent");await Lock(db,scope,token);var previous=await Latest(db,scope,token);var filters=result.Filters with{Offset=0};var recent=new RecentReport(result.ReportId,filters,result.GeneratedAt,result.TotalRows);
  Append(db,actor,scope,previous,new[]{recent}.Concat(Items<RecentReport>(previous).Where(x=>x.ReportId!=recent.ReportId||x.Filters!=filters)).Take(20).ToArray(),result.GeneratedAt);await db.SaveChangesAsync(token);
 }
}
