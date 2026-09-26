using System.Data;
using System.Text;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Reporting;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReportExportInput(ReportFilters Filters,string Format="csv");
public sealed record ReportCsv(string FileName,byte[] Content);
public sealed class ReportExportService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
 public static string Cell(string? value,bool numeric=false)
 {
  value??="";var trimmed=value.TrimStart();if(!numeric&&(trimmed.Length>0&&"=+-@".Contains(trimmed[0])||value.StartsWith('\t')||value.StartsWith('\r')||value.StartsWith('\n')))value="'"+value;
  return "\""+value.Replace("\"","\"\"")+"\"";
 }
 public async Task<ReportCsv> ExportAsync(ActorContext actor,Guid id,ReportExportInput input,CancellationToken token=default)
 {
  if(input.Format!="csv"||input.Filters==null)throw new QuoteOperationException(400,"report-export-format-invalid");
  await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);actor=await ReportingScope.Current(db,actor,token);var d=ReportCatalogue.Authorize(actor,id);var filters=input.Filters with{Offset=0};ReportService.Validate(d,filters);var now=time.GetUtcNow();var sources=await ReportService.Load(db,actor,d,filters,now,token);var result=ReportService.Result(d,filters,now,sources,true);
  var csv=new StringBuilder();void Line(IEnumerable<string> cells)=>csv.Append(string.Join(',',cells)).Append("\r\n");
  Line(new[]{"Report","Generated at UTC","From","To","Basis","Record ID","Kind","Reference","Label","State","Source ID","Rule / period ID","Agency ID","Product","Source at UTC","Basis date","Record link"}.Concat(d.Measures.Select(x=>x.Label+" ("+x.Unit+")")).Select(x=>Cell(x)));
  foreach(var row in result.Rows)Line(new[]{d.Title,now.ToString("O"),filters.From.ToString("yyyy-MM-dd"),filters.To.ToString("yyyy-MM-dd"),filters.Basis,row.RecordId.ToString(),row.Kind,row.Reference,row.Label,row.State,row.SourceId?.ToString(),row.SourceRuleId?.ToString(),row.AgencyId?.ToString(),row.ProductCode,row.At.ToString("O"),row.BasisDate?.ToString("yyyy-MM-dd"),row.Href}.Select(x=>Cell(x)).Concat(d.Measures.Select(m=>Cell(row.Values[m.Code],true))));
  await tx.CommitAsync(token);return new(d.Code+"-"+now.ToString("yyyyMMdd-HHmmss")+".csv",Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
 }
}
