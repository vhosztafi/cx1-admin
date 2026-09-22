using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record OperationalSeedIssue(Guid SourceId,string Code);
public sealed record OperationalSeedResult(int MatchRequests,IReadOnlyList<OperationalSeedIssue> Issues);

// Explicit local setup, not a read endpoint and never an automatic resend.
public sealed class OperationalDemoSeed(IDbContextFactory<BackOfficeDbContext> factory,LegacyOperationalBridge bridge)
{
    public async Task<OperationalSeedResult> Initialize(CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var ids=await db.Set<MatchInformationRequest>().OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync(token);
        var count=0;var issues=new List<OperationalSeedIssue>();
        foreach(var id in ids)
        {
            try {await bridge.MatchRequest(id,token);count++;}
            catch(OperationalAccessException error){issues.Add(new(id,error.Code));}
        }
        return new(count,issues);
    }
}
