using System.Diagnostics;
using System.Data.Common;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyCommercialRenewalConcurrentReads(IDbContextFactory<BackOfficeDbContext> factory,ActorContext actor,RatingClock clock,Guid draftId)
    {
        await using var source=await factory.CreateDbContextAsync();
        var locks=new CommercialRenewalReadLocks();factory=new CommercialRenewalReadFactory(source.Database.GetConnectionString()!,locks);
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;
        Assert.NotNull(root);
        var path=Path.Combine(root.FullName,".local","commercial-renewal-read-timings-"+Guid.NewGuid().ToString("N")+".jsonl");var gate=new object();
        var preparation=new RenewalPreparationService(factory,clock);
        var reads=new (string Name,Func<Task> Read)[]{
            ("preparation",async()=>Assert.True((await preparation.ReadPreparationAsync(actor,draftId)).Current)),
            ("experience",async()=>Assert.NotNull((await preparation.ReadExperienceAsync(actor,draftId)).CurrentCommercialSubjects)),
            ("requirements",async()=>Assert.True((await new ServicingEvidenceService(factory,clock).RequirementsAsync(actor,draftId)).Applicable)),
            ("rating",async()=>Assert.True(Assert.Single((await new ServicingRatingReadModel(factory,clock).ReadAsync(actor,draftId)).Items).Applicable)),
            ("referrals",async()=>Assert.NotEmpty((await new ServicingReferralService(factory,clock).ReadReferralsAsync(actor,draftId)).Items))};
        async Task Measure(string mode,(string Name,Func<Task> Read) read)
        {
            var watch=Stopwatch.StartNew();var passed=false;
            try{await read.Read();passed=true;}
            finally{lock(gate)File.AppendAllText(path,JsonSerializer.Serialize(new{mode,read.Name,elapsedMs=watch.ElapsedMilliseconds,passed})+Environment.NewLine);}
        }
        foreach(var read in reads)await Measure("sequential",read);
        await Task.WhenAll(reads.Select(read=>Measure("concurrent",read)));
        Assert.Equal(0,locks.UpdateLockCommands);
        await using var db=await factory.CreateDbContextAsync();
        Assert.Equal(1,await db.Set<ServicingCycle>().CountAsync());Assert.Equal(1,await db.Set<RenewalExperienceReview>().CountAsync());Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());
    }

    private sealed class CommercialRenewalReadFactory(string connection,CommercialRenewalReadLocks locks):IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext()=>new(new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseSqlServer(connection,sql=>sql.UseCompatibilityLevel(160)).AddInterceptors(locks).Options);
        public Task<BackOfficeDbContext> CreateDbContextAsync(CancellationToken token=default)=>Task.FromResult(CreateDbContext());
    }
    private sealed class CommercialRenewalReadLocks:DbCommandInterceptor
    {
        private int count;
        public int UpdateLockCommands=>Volatile.Read(ref count);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken token=default)
        {
            if(command.CommandText.Contains("UPDLOCK",StringComparison.OrdinalIgnoreCase))Interlocked.Increment(ref count);
            return ValueTask.FromResult(result);
        }
    }
}
