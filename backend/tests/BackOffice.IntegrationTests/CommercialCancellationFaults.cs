using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private sealed class CommercialCancellationFaultFactory(string connection,CommercialCancellationFault fault):IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext()=>new(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection,x=>x.UseCompatibilityLevel(160)).AddInterceptors(fault).Options);
        public Task<BackOfficeDbContext> CreateDbContextAsync(CancellationToken token=default)=>Task.FromResult(CreateDbContext());
    }
    private sealed class CommercialCancellationFault(bool corruptRelease):SaveChangesInterceptor
    {
        public bool Applied{get;private set;}
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            if(Applied)return ValueTask.FromResult(result);
            if(corruptRelease)
            {
                var decision=data.Context!.ChangeTracker.Entries<CommercialExposureIssueDecision>().FirstOrDefault(x=>x.State==EntityState.Added)?.Entity;
                if(decision is not null)
                {
                    var json=JsonNode.Parse(decision.DecisionJson)!;json["untrustedBookRelease"]=true;decision.DecisionJson=json.ToJsonString();
                    decision.DecisionHash=SHA256.HashData(Encoding.UTF8.GetBytes(decision.DecisionJson));Applied=true;
                }
            }
            else if(data.Context!.ChangeTracker.Entries<CancellationConsequence>().Any(x=>x.State==EntityState.Added&&x.Entity.Kind=="task-close"))
            {Applied=true;throw new InvalidOperationException("Owned late cancellation failure after credit and zero exposure.");}
            return ValueTask.FromResult(result);
        }
    }
}
