using BackOffice.Application.Policies;
using System.Data;
using System.Globalization;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
public sealed record ReportFilters(DateOnly From,DateOnly To,string Basis="processed",Guid? AgencyId=null,Guid? ProviderId=null,string? ProductCode=null,Guid? UnderwriterId=null,int Offset=0);
public sealed record ReportRow(Guid RecordId,string Kind,string Reference,string Label,string State,string Href,Guid? SourceId,Guid? SourceRuleId,Guid? AgencyId,string? ProductCode,DateTimeOffset At,DateOnly? BasisDate,Dictionary<string,string?> Values);
public sealed record ReportValue(string Code,string? Value,string? Denominator);
public sealed record ReportResult(Guid ReportId,DateTimeOffset GeneratedAt,ReportFilters Filters,string Definition,int TotalRows,ReportValue[] Measures,ReportRow[] Rows,int? NextOffset,object[] Groups);
internal sealed record ReportSource
{
 public Guid Id {get;init;} public string Kind {get;init;}="";public string Reference {get;init;}="";public string Label {get;init;}="";public string State {get;init;}="";public string Href {get;init;}="";public Guid? SourceId {get;init;}public Guid? SourceRuleId {get;init;}public Guid? AgencyId {get;init;}public Guid? ProviderId {get;init;}public string? ProductCode {get;init;}public Guid? UnderwriterId {get;init;}public DateTimeOffset At {get;init;}public DateOnly? BasisDate {get;init;}
 public decimal Count {get;init;}=1;public decimal Bound {get;init;}public decimal Referred {get;init;}public decimal Declined {get;init;}public decimal Premium {get;init;}public decimal Commission {get;init;}public decimal Cash {get;init;}public decimal Debt {get;init;}public decimal Earned {get;init;}public decimal ClosingDebt {get;init;}public decimal Tasks {get;init;}public decimal Completed {get;init;}public decimal ServiceHours {get;init;}public decimal NewBusiness {get;init;}public decimal Cancelled {get;init;}public decimal Invited {get;init;}public decimal Renewed {get;init;}public decimal Lapsed {get;init;}public decimal Expired {get;init;}public decimal RenewedExpired {get;init;}public decimal Decided {get;init;}public decimal Hours {get;init;}public decimal SourcePremium {get;init;}public decimal Difference {get;init;}
 public Dictionary<string,decimal> Values()=>new(){["count"]=Count,["bound"]=Bound,["referred"]=Referred,["declined"]=Declined,["premium"]=Premium,["commission"]=Commission,["cash"]=Cash,["debt"]=Debt,["earned"]=Earned,["closingDebt"]=ClosingDebt,["tasks"]=Tasks,["completed"]=Completed,["serviceHours"]=ServiceHours,["newBusiness"]=NewBusiness,["cancelled"]=Cancelled,["invited"]=Invited,["renewed"]=Renewed,["lapsed"]=Lapsed,["expired"]=Expired,["renewedExpired"]=RenewedExpired,["decided"]=Decided,["hours"]=Hours,["sourcePremium"]=SourcePremium,["difference"]=Difference};
}
public sealed partial class ReportService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
 public async Task<ReportDefinition[]> CatalogueAsync(ActorContext actor,CancellationToken token=default){await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);var result=ReportCatalogue.All.Where(d=>actor.HasCapability(d.Capability)).ToArray();await tx.CommitAsync(token);return result;}
 public async Task<object> OptionsAsync(ActorContext actor,Guid id,CancellationToken token=default)
 {
  await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);actor=await ReportingScope.Current(db,actor,token);var d=ReportCatalogue.Authorize(actor,id);
  var products=d.Filters.Contains("productCode")?await db.Set<Product>().OrderBy(x=>x.Name).Select(x=>new{x.Code,x.Name}).ToArrayAsync(token):[];
  var agencies=d.Filters.Contains("agencyId")?await db.Set<Agency>().OrderBy(x=>x.LegalName).Select(x=>new{x.Id,Name=x.LegalName}).ToArrayAsync(token):[];
  var providers=d.Filters.Contains("providerId")?await db.Set<CapacityProvider>().OrderBy(x=>x.Name).Select(x=>new{x.Id,x.Name}).ToArrayAsync(token):[];
  var underwriters=d.Filters.Contains("underwriterId")?await db.Set<StaffUser>().Where(x=>db.Set<Quote>().Any(q=>q.AssignedUserId==x.Id)).OrderBy(x=>x.DisplayName).Select(x=>new{x.Id,Name=x.DisplayName}).ToArrayAsync(token):[];
  await tx.CommitAsync(token);return new{products,agencies,providers,underwriters};
 }
 public async Task<ReportResult> RunAsync(ActorContext actor,Guid id,ReportFilters filters,CancellationToken token=default)
 {
  await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);actor=await ReportingScope.Current(db,actor,token);
  var d=ReportCatalogue.Authorize(actor,id);Validate(d,filters);var now=time.GetUtcNow();var sources=await Load(db,actor,d,filters,now,token);var result=Result(d,filters,now,sources,false);await tx.CommitAsync(token);return result;
 }
 internal static void Validate(ReportDefinition d,ReportFilters f)
 {
  if(f.From==default||f.To==default||f.To==DateOnly.MaxValue||f.From>f.To||f.To.DayNumber-f.From.DayNumber>366*5||f.Offset is <0 or >10000||!d.Bases.Contains(f.Basis)||f.ProductCode?.Length>60)throw new QuoteOperationException(400,"report-filter-invalid");
  if(f.AgencyId!=null&&!d.Filters.Contains("agencyId")||f.ProviderId!=null&&!d.Filters.Contains("providerId")||f.ProductCode!=null&&!d.Filters.Contains("productCode")||f.UnderwriterId!=null&&!d.Filters.Contains("underwriterId"))throw new QuoteOperationException(400,"report-filter-not-applicable");
  if(d.Code=="earned-premium"&&(f.From.Day!=1||f.To.Day!=DateTime.DaysInMonth(f.To.Year,f.To.Month)))throw new QuoteOperationException(400,"earned-report-requires-complete-months");
 }
 internal static DateTimeOffset Utc(DateOnly day)=>new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue,DateTimeKind.Unspecified),TimeZoneInfo.FindSystemTimeZoneById("Europe/London")),TimeSpan.Zero);
 internal static async Task<ReportSource[]> Load(BackOfficeDbContext db,ActorContext actor,ReportDefinition d,ReportFilters f,DateTimeOffset now,CancellationToken token)
 {
  var from=Utc(f.From);var to=Utc(f.To.AddDays(1));
  if(d.Code=="earned-premium")return await Earned(db,f,now,token);
  var query=d.Code switch{
   "underwriting" or "agency-conversion"=>InsuranceQuotes(db,from,to),
   "portfolio"=>Portfolio(db,f,from,to,now),
   "active-portfolio"=>ActivePortfolio(db,from,to,now),
   "finance"=>Written(db,f,from,to,now),
   "bordereau-reconciliation"=>Bordereaux(db,f,now),
   "referral-turnaround"=>Referrals(db,from,to,now),
   "compliance"=>ServiceTasks(db,actor,from,to,true),
   "renewal-retention" or "renewal-invitations"=>Renewals(db,d.Code,from,to,now),
   _=>throw new QuoteOperationException(409,"report-not-yet-implemented")
  };
  var rows=await ReadSources(query,f,token);
  if(d.Code=="finance")rows=rows.Concat(await ReadSources(CashMovements(db,f,from,to,now),f,token)).ToArray();
  if(d.Code=="agency-conversion")rows=rows.Concat(await ReadSources(ServiceTasks(db,actor,from,to),f,token)).ToArray();
  if(d.Code=="compliance")rows=rows.Concat(await ReadSources(SupportReviews(db,actor,from,to,DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"Europe/London").DateTime)),f,token)).Concat(await ReadSources(MidExceptions(db,from,to),f,token)).ToArray();
  if(rows.Length>10000)throw new QuoteOperationException(422,"report-too-large-narrow-filters");
  rows=rows.OrderBy(x=>x.At).ThenBy(x=>x.Id).ThenBy(x=>x.SourceId).ToArray();
  if(d.Code=="renewal-invitations")
  {
   var due=new List<ReportSource>();
   foreach(var group in rows.GroupBy(x=>x.ProductCode=="commercial-combined"?RenewalConfiguration.CommercialScope:RenewalConfiguration.Scope))
   {
    var setting=await db.Set<SettingVersion>().Where(x=>x.Scope==group.Key&&x.EffectiveFrom<=now).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(token);
    var rule=setting==null?null:RenewalConfiguration.Parse(setting.Values,group.Key);if(rule==null)throw new QuoteOperationException(503,"renewal-configuration-unavailable");
    foreach(var row in group){try{if(now>=RenewalLifecycleRules.Timeline(row.At,rule.InvitationDaysBeforeExpiry,rule.LapseDaysAfterExpiry).InvitationDueAt)due.Add(row with{SourceRuleId=setting!.Id});}catch(ArgumentException){throw new QuoteOperationException(409,"renewal-timeline-ambiguous");}}
   }
   rows=due.ToArray();
  }
  return rows;

 }
 private static async Task<ReportSource[]> ReadSources(IQueryable<ReportSource> query,ReportFilters f,CancellationToken token)
 {
  if(f.AgencyId!=null)query=query.Where(x=>x.AgencyId==f.AgencyId);if(f.ProviderId!=null)query=query.Where(x=>x.ProviderId==f.ProviderId);if(f.ProductCode!=null)query=query.Where(x=>x.ProductCode==f.ProductCode);if(f.UnderwriterId!=null)query=query.Where(x=>x.UnderwriterId==f.UnderwriterId);
  var rows=await query.OrderBy(x=>x.At).ThenBy(x=>x.Id).ThenBy(x=>x.SourceId).Take(10001).ToArrayAsync(token);if(rows.Length>10000)throw new QuoteOperationException(422,"report-too-large-narrow-filters");return rows;
 }
 internal static string Number(decimal value)=>value.ToString("0.############################",CultureInfo.InvariantCulture);
 internal static ReportValue[] Measures(ReportDefinition d,IEnumerable<ReportSource> rows)
 {
  var totals=new Dictionary<string,decimal>();foreach(var row in rows)foreach(var pair in row.Values())totals[pair.Key]=totals.GetValueOrDefault(pair.Key)+pair.Value;
  return d.Measures.Select(m=>m.Denominator is {} denominator?new ReportValue(m.Code,totals.GetValueOrDefault(denominator)==0?null:Number(decimal.Round(totals.GetValueOrDefault(m.Numerator!)/totals[denominator]*(m.Unit=="percent"?100:1),4)),Number(totals.GetValueOrDefault(denominator))):new ReportValue(m.Code,Number(totals.GetValueOrDefault(m.Code)),null)).ToArray();
 }
 internal static ReportResult Result(ReportDefinition d,ReportFilters f,DateTimeOffset now,ReportSource[] source,bool all)
 {
  var page=all?source:source.Skip(f.Offset).Take(50);
  var rows=page.Select(x=>new ReportRow(x.Id,x.Kind,x.Reference,x.Label,x.State,x.Href.Length>0?x.Href:SearchService.Href(x.Kind,x.Id),x.SourceId,x.SourceRuleId,x.AgencyId,x.ProductCode,x.At,x.BasisDate,Measures(d,[x]).ToDictionary(m=>m.Code,m=>m.Value))).ToArray();
  object[] groups=d.Code is "underwriting" or "agency-conversion"?source.GroupBy(x=>new{x.AgencyId,Month=d.Code=="agency-conversion"?"All periods":TimeZoneInfo.ConvertTime(x.At,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).ToString("yyyy-MM",CultureInfo.InvariantCulture),ProductCode=d.Code=="agency-conversion"?null:x.ProductCode}).OrderByDescending(g=>d.Code=="agency-conversion"&&g.Sum(x=>x.Count)>0?g.Sum(x=>x.Bound)/g.Sum(x=>x.Count):-1).ThenBy(g=>g.Key.Month).ThenBy(g=>g.Key.AgencyId).Select(g=>(object)new{g.Key.AgencyId,g.Key.Month,g.Key.ProductCode,measures=Measures(d,g)}).ToArray():[];
  return new(d.Id,now,f,d.Description,source.Length,Measures(d,source),rows,!all&&f.Offset+rows.Length<source.Length?f.Offset+rows.Length:null,groups);
 }
}


