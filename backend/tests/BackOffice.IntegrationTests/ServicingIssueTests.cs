using System.Text.Json;
using System.Data.Common;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingIssue(BackOfficeDbContext db, DecisionFixture f, ServicingCycle cycle,
        ServicingAcceptance acceptance, Guid lease, byte[] version,string password)
    {
        var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.BaseVersionId);
        var originalLines = JsonSerializer.Serialize(await db.Set<JournalLine>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        var input = new ServicingIssueInput(cycle.Id, acceptance.RatingId, acceptance.TermsVersionId, acceptance.Id,
            acceptance.TermsHash, acceptance.AssuranceHash, "Issue the accepted fictional policy adjustment");
        var service = new ServicingIssueService(f.Factory, f.Clock);
        await VerifyServicingIssueHttp(db,f,password,cycle.DraftId,version,lease,input);
        static string Key() => Guid.NewGuid().ToString();
        Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Servicing, cycle.DraftId, version, lease, input, Key(), Guid.NewGuid()))).Status);
        Assert.Equal(412, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, cycle.DraftId, new byte[8], lease, input, Key(), Guid.NewGuid()))).Status);
        await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, cycle.DraftId, version, Guid.NewGuid(), input, Key(), Guid.NewGuid()));
        await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, cycle.DraftId, version, lease, input with { AssuranceHash = new string('f',64) }, Key(), Guid.NewGuid()));
        var boundaries=new[] { "ServicingIssueDecision", "PolicyTransaction", "PolicyVersion", "PolicyRegistration", "IssueFinancialObligation", "IssueFinancialComponent", "Journal", "JournalLine", "PolicyDocumentRequest", "PolicyMidIntent", "OutboxWork", "ClientActivity", "AuditEvent", "IdempotencyRecord" };
        var counts=new Dictionary<string,int>();
        foreach(var table in boundaries) counts[table]=await IssueGraphCount(db,table);
        foreach (var table in boundaries)
        {
            // Inject at the actual EF command boundary. A temporary SQL trigger
            // would invalidate OUTPUT syntax on unrelated infrastructure tables.
            var failure = new ServicingIssueFailure(table);
            var failing = new ServicingIssueService(new ServicingIssueFailureFactory(db.Database.GetConnectionString()!,failure),f.Clock);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => failing.IssueAsync(f.Underwriter, cycle.DraftId, version, lease, input, Key(), Guid.NewGuid()));
            Assert.Equal(table,Assert.IsType<ServicingIssueInjectedFailure>(error.GetBaseException()).Table);
            Assert.Equal(1, await db.Set<PolicyTransaction>().CountAsync()); Assert.Equal(1, await db.Set<PolicyVersion>().CountAsync());
            Assert.Equal(1, await db.Set<Journal>().CountAsync());
            Assert.Equal("draft", await db.Set<ServicingDraft>().Where(x => x.Id == cycle.DraftId).Select(x => x.State).SingleAsync());
            foreach(var graphTable in boundaries)
                Assert.Equal(counts[graphTable],await IssueGraphCount(db,graphTable));
        }
        var competing = await Task.WhenAll(Enumerable.Range(0,2).Select(async _ => {
            var candidate=Key();
            try {return (Key:candidate,Result:(CommandOutcome?)await service.IssueAsync(f.Underwriter,cycle.DraftId,version,lease,input,candidate,Guid.NewGuid()),Error:(QuoteOperationException?)null);}
            catch(QuoteOperationException error) {return (Key:candidate,Result:(CommandOutcome?)null,Error:(QuoteOperationException?)error);}
        }));
        var winner=Assert.Single(competing,x=>x.Result is not null);
        Assert.Equal(409,Assert.Single(competing,x=>x.Error is not null).Error!.Status);
        var key=winner.Key;var result=winner.Result!;
        var replays=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>service.IssueAsync(f.Underwriter,cycle.DraftId,version,lease,input,key,Guid.NewGuid())));
        Assert.All(replays,x=>{Assert.True(x.Replayed);Assert.Equal(result.Body,x.Body);});
        Assert.Equal(201, result.Status);
        var receipt = JsonSerializer.Deserialize<JsonElement>(result.Body);
        var ids = receipt.GetProperty("versionIds").EnumerateArray().Select(x => x.GetGuid()).ToArray();
        Assert.Equal(2, ids.Length); Assert.Equal(ids[0], receipt.GetProperty("versionId").GetGuid());
        Assert.True((await service.IssueAsync(f.Underwriter, cycle.DraftId, version, lease, input, key, Guid.NewGuid())).Replayed);
        await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, cycle.DraftId, version, lease, input, Key(), Guid.NewGuid()));
        Assert.Equal(2, await db.Set<PolicyTransaction>().CountAsync()); Assert.Equal(3, await db.Set<PolicyVersion>().CountAsync());
        var retained = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == basis.Id);
        Assert.Equal(basis.SnapshotJson, retained.SnapshotJson); Assert.Equal(basis.ContentHash, retained.ContentHash);
        Assert.Equal(originalLines, JsonSerializer.Serialize(await db.Set<JournalLine>().AsNoTracking().Where(x => x.TransactionId == basis.TransactionId).OrderBy(x => x.Id).ToArrayAsync()));
        foreach (var id in ids)
        {
            var snapshot = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.Equal("issued-servicing-1", JsonSerializer.Deserialize<JsonElement>(snapshot.SnapshotJson).GetProperty("snapshotFormat").GetString());
            Assert.Equal(3, await db.Set<PolicyDocumentRequest>().CountAsync(x => x.VersionId == id));
            Assert.Single(await db.Set<PolicyMidIntent>().Where(x => x.VersionId == id).ToArrayAsync());
            var selected = await new PolicyReadService(f.Factory,f.Clock).ReadAtAsync(f.Underwriter,basis.PolicyId,snapshot.EffectiveAt,f.Clock.GetUtcNow());
            Assert.Equal(id,selected["versionId"]);
        }
        Assert.Equal("issued", await db.Set<ServicingDraft>().Where(x => x.Id == cycle.DraftId).Select(x => x.State).SingleAsync());
        var current = await new PolicyReadService(f.Factory, f.Clock).ReadAsync(f.Underwriter, basis.PolicyId);
        Assert.Equal(basis.Id, current["versionId"]);
        if(JsonSerializer.Deserialize<JsonElement>(basis.SnapshotJson).GetProperty("productCode").GetString()=="motor-trade-road-risks")
        {
            Assert.Empty(await PolicyDiscoveryService.Search(db,PolicyDiscoveryService.Rows(db,f.Clock.GetUtcNow()),"ZZ10 TST").ToArrayAsync());
            var future=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==ids[0]);
            Assert.Equal(basis.PolicyId,(await PolicyDiscoveryService.Search(db,PolicyDiscoveryService.Rows(db,future.EffectiveAt,f.Clock.GetUtcNow()),"ZZ10 TST").SingleAsync()).Id);
        }
        // Updating a bound quote still validates its original first issue,
        // regardless of the term's pointer to the latest servicing version.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET State=State WHERE Id={f.QuoteId}");
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingDraft SET State='draft',IssuedTransactionId=NULL WHERE Id={cycle.DraftId}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingIssueDecision SET Reason='Mutated issued reason' WHERE DraftId={cycle.DraftId}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PolicyMidIntent WHERE VersionId={ids[0]}"));
        await VerifyServicingIssueHttp(db,f,password,cycle.DraftId,version,lease,input,key,result.Body);
        f.Clock.Current=f.Clock.GetUtcNow().AddDays(2);
        Assert.True((await service.IssueAsync(f.Underwriter,cycle.DraftId,version,lease,input,key,Guid.NewGuid())).Replayed);
        var decision = await db.Set<ServicingIssueDecision>().AsNoTracking().SingleAsync(x => x.DraftId==cycle.DraftId);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Fictional revoked issue authority check' WHERE Id={decision.GrantId}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter,cycle.DraftId,version,lease,input,key,Guid.NewGuid()))).Status);
    }

    private static Task<int> IssueGraphCount(BackOfficeDbContext db,string table)
    {
        // Names come only from the fixed graph table list above. Bind the value
        // through sys.tables rather than constructing a SQL identifier.
        return db.Database.SqlQuery<int>($"SELECT CONVERT(int,SUM(p.rows)) AS [Value] FROM sys.partitions p JOIN sys.tables t ON t.object_id=p.object_id WHERE t.name={table} AND p.index_id IN(0,1)").SingleAsync();
    }

    private sealed class ServicingIssueInjectedFailure(string table) : Exception("Injected servicing issue write failure")
    { public string Table { get; } = table; }

    private sealed class ServicingIssueFailureFactory(string connection,ServicingIssueFailure failure) : IDbContextFactory<BackOfficeDbContext>
    {
        public BackOfficeDbContext CreateDbContext() => new(new DbContextOptionsBuilder<BackOfficeDbContext>()
            .UseSqlServer(connection,sql=>sql.UseCompatibilityLevel(160)).AddInterceptors(failure).Options);
        public Task<BackOfficeDbContext> CreateDbContextAsync(CancellationToken token=default) => Task.FromResult(CreateDbContext());
    }

    private sealed class ServicingIssueFailure(string table) : DbCommandInterceptor
    {
        private void Check(DbCommand command)
        {
            if(command.CommandText.Contains("INSERT INTO ["+table+"]",StringComparison.Ordinal) ||
                command.CommandText.Contains("MERGE ["+table+"]",StringComparison.Ordinal))
                throw new ServicingIssueInjectedFailure(table);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,
            InterceptionResult<DbDataReader> result,CancellationToken token=default)
        { Check(command);return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,
            InterceptionResult<int> result,CancellationToken token=default)
        { Check(command);return ValueTask.FromResult(result); }
    }
}
