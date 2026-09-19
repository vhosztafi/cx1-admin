using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    // Corrupt the actual pending EF write after application calculation. SQL,
    // rather than a duplicate application validator, must reject the mutation.
    private sealed class CancellationFaultFactory(string connection,CancellationPostingFault fault):IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext()=>new(new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseSqlServer(connection,sql=>sql.UseCompatibilityLevel(160)).AddInterceptors(fault).Options);
        public Task<BackOfficeDbContext> CreateDbContextAsync(CancellationToken token=default)=>Task.FromResult(CreateDbContext());
    }
    private sealed class CancellationPostingFault(string kind):SaveChangesInterceptor
    {
        public bool Applied {get;private set;}
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            if(Applied)return ValueTask.FromResult(result);
            if(kind=="party")
            {
                var obligation=data.Context!.ChangeTracker.Entries<IssueFinancialObligation>().FirstOrDefault(x=>x.State==EntityState.Added&&x.Entity.Purpose=="cancellation")?.Entity;
                if(obligation is not null){obligation.ProviderId=Guid.NewGuid();Applied=true;}
            }
            else
            {
                var components=data.Context!.ChangeTracker.Entries<IssueFinancialComponent>().Where(x=>x.State==EntityState.Added&&x.Entity.OriginalComponentId!=null).Select(x=>x.Entity).ToArray();
                if(components.Length>0)
                {
                    var component=components.Single(x=>x.Code=="premium");
                    switch(kind)
                    {
                        case "amount":component.Amount+=0.01m;break;
                        case "interval":component.CoverageStartsAt=component.CoverageStartsAt.AddDays(1);break;
                        case "foreign-original":component.OriginalComponentId=Guid.NewGuid();break;
                        case "duplicate-original":component.OriginalComponentId=components.Single(x=>x.Code=="tax").OriginalComponentId;break;
                        default:throw new InvalidOperationException("Unknown test mutation.");
                    }
                    Applied=true;
                }
            }
            return ValueTask.FromResult(result);
        }
    }
}
