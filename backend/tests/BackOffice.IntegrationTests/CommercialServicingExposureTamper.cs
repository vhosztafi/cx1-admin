using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    private sealed class CommercialExposureTamperFactory(string connection,CommercialExposureTamper fault):IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext()=>new(new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseSqlServer(connection,sql=>sql.UseCompatibilityLevel(160)).AddInterceptors(fault).Options);
        public Task<BackOfficeDbContext> CreateDbContextAsync(CancellationToken token=default)=>Task.FromResult(CreateDbContext());
    }
    private sealed class CommercialExposureTamper(string kind):SaveChangesInterceptor
    {
        public bool Applied {get;private set;}
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            var decision=data.Context!.ChangeTracker.Entries<CommercialExposureIssueDecision>().FirstOrDefault(x=>x.State==EntityState.Added)?.Entity;
            if(Applied||decision is null)return ValueTask.FromResult(result);
            var value=JsonNode.Parse(decision.DecisionJson)!;var rows=value["intervals"]!.AsArray();
            if(kind=="district")
            {
                var district=rows[0]!["district"]!.GetValue<string>();
                foreach(var row in rows.Where(x=>x!["district"]!.GetValue<string>()==district).ToArray())rows.Remove(row);
            }
            else if(kind=="boundary")
            {
                foreach(var group in rows.ToArray().GroupBy(x=>x!["district"]!.GetValue<string>()))
                {var first=group.First()!;first["to"]=group.Last()!["to"]!.DeepClone();foreach(var row in group.Skip(1))rows.Remove(row);}
            }
            else throw new InvalidOperationException("Unknown commercial decision tamper.");
            decision.DecisionJson=value.ToJsonString();decision.DecisionHash=SHA256.HashData(Encoding.UTF8.GetBytes(decision.DecisionJson));Applied=true;
            return ValueTask.FromResult(result);
        }
    }
}
