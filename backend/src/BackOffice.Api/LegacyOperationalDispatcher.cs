using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Api;

public sealed class LegacyOperationalDispatcher(IServiceScopeFactory scopes,ILogger<LegacyOperationalDispatcher> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var offset=0;
        while(!token.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>();
                await using var db=await factory.CreateDbContextAsync(token);
                var ids=await db.Set<MatchInformationRequest>().Where(x=>!db.Set<MatchCorrespondence>().Any(l=>l.InformationRequestId==x.Id))
                    .OrderBy(x=>x.Id).Skip(offset).Take(32).Select(x=>x.Id).ToArrayAsync(token);
                var bridge=scope.ServiceProvider.GetRequiredService<LegacyOperationalBridge>();var unavailable=0;
                foreach(var id in ids)
                {
                    try {await bridge.MatchRequest(id,token);}
                    catch(OperationalAccessException){unavailable++;}
                }
                // Successfully associated rows leave this query. Advance only
                // over unavailable sources so they cannot starve later work.
                offset=ids.Length<32||offset>int.MaxValue-32?0:offset+unavailable;
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Recorded request association was interrupted; original records remain recoverable.");}
            try {await Task.Delay(TimeSpan.FromSeconds(30),token);}
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
        }
    }
}
