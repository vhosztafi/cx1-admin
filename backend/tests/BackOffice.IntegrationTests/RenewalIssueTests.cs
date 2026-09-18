using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyRenewalIssue(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,ServicingAcceptance acceptance,
        Guid fence,string etag,PolicyVersion basis,string password)
    {
        var issue=new ServicingIssueService(f.Factory,f.Clock);
        var input=new ServicingIssueInput(cycle.Id,cycle.CurrentRatingId!.Value,acceptance.TermsVersionId,acceptance.Id,acceptance.TermsHash,acceptance.AssuranceHash,"Issue the evidenced fictional renewal");
        var version=Convert.FromBase64String(etag.Trim('"'));var key=Guid.NewGuid().ToString();
        var originalTerm=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==basis.TermId);
        await VerifyServicingIssueHttp(db,f,password,cycle.DraftId,version,fence,input);
        static string Key()=>Guid.NewGuid().ToString();
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Servicing,cycle.DraftId,version,fence,input,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Underwriter,cycle.DraftId,new byte[8],fence,input,Key(),Guid.NewGuid()))).Status);
        await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Underwriter,cycle.DraftId,version,Guid.NewGuid(),input,Key(),Guid.NewGuid()));
        await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input with { AssuranceHash=new string('f',64) },Key(),Guid.NewGuid()));
        var now=f.Clock.Current;
        try
        {
            f.Clock.Current=originalTerm.EndsAt.AddTicks(1);
            Assert.Equal("renewal-late-issue-unsupported",(await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input,Key(),Guid.NewGuid()))).Code);
        }
        finally { f.Clock.Current=now; }
        var boundaries=new[]{"ServicingIssueDecision","PolicyTerm","PolicyTransaction","PolicyVersion","IssueFinancialObligation","Journal","JournalLine","PolicyDocumentRequest","PolicyMidIntent","OutboxWork","ClientActivity","AuditEvent","IdempotencyRecord"};
        var counts=new Dictionary<string,int>();
        foreach(var table in boundaries)counts[table]=await IssueGraphCount(db,table);
        foreach(var table in boundaries)
        {
            var failing=new ServicingIssueService(new ServicingIssueFailureFactory(db.Database.GetConnectionString()!,new ServicingIssueFailure(table)),f.Clock);
            var failure=await Assert.ThrowsAnyAsync<Exception>(()=>failing.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input,Key(),Guid.NewGuid()));
            Assert.Equal(table,Assert.IsType<ServicingIssueInjectedFailure>(failure.GetBaseException()).Table);
            foreach(var graphTable in boundaries)Assert.Equal(counts[graphTable],await IssueGraphCount(db,graphTable));
            Assert.Equal("draft",await db.Set<ServicingDraft>().Where(x=>x.Id==cycle.DraftId).Select(x=>x.State).SingleAsync());
        }
        var competing=await Task.WhenAll(Enumerable.Range(0,2).Select(async _=>{
            var candidate=Key();
            try{return(Key:candidate,Result:(CommandOutcome?)await issue.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input,candidate,Guid.NewGuid()),Error:(QuoteOperationException?)null);}
            catch(QuoteOperationException error){return(Key:candidate,Result:(CommandOutcome?)null,Error:(QuoteOperationException?)error);}
        }));
        var winner=Assert.Single(competing,x=>x.Result is not null);key=winner.Key;var issued=winner.Result!;
        Assert.Equal("servicing-already-issued",Assert.Single(competing,x=>x.Error is not null).Error!.Code);
        Assert.Equal(201,issued.Status);
        Assert.True((await issue.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input,key,Guid.NewGuid())).Replayed);
        Assert.Equal("servicing-already-issued",(await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input,Guid.NewGuid().ToString(),Guid.NewGuid()))).Code);
        using var receipt=JsonDocument.Parse(issued.Body);var nextTermId=receipt.RootElement.GetProperty("termId").GetGuid();var versionId=receipt.RootElement.GetProperty("versionId").GetGuid();
        Assert.NotEqual(basis.TermId,nextTermId);Assert.Equal(2,await db.Set<PolicyTerm>().CountAsync());
        var next=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==nextTermId);
        Assert.Equal(originalTerm.EndsAt,next.StartsAt);Assert.Equal(originalTerm.Number+1,next.Number);Assert.Equal(cycle.ProductVersionId,next.ProductVersionId);
        var transaction=await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x=>x.TermId==next.Id);Assert.Equal("renewal",transaction.Kind);Assert.Equal(1,transaction.Sequence);
        var decision=await db.Set<ServicingIssueDecision>().AsNoTracking().SingleAsync(x=>x.Id==transaction.ServicingIssueDecisionId);Assert.Equal(originalTerm.Id,decision.BaseTermId);
        var snapshot=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==versionId);using var json=JsonDocument.Parse(snapshot.SnapshotJson);
        var rating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId);
        Assert.Equal(rating.Premium,decimal.Parse(json.RootElement.GetProperty("premium").GetProperty("termPremium").GetString()!,System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(next.StartsAt,json.RootElement.GetProperty("term").GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(3,await db.Set<PolicyDocumentRequest>().CountAsync(x=>x.VersionId==versionId && x.Purpose=="renewal"));
        Assert.Equal(1,await db.Set<PolicyMidIntent>().CountAsync(x=>x.VersionId==versionId && x.Purpose=="renewal"));
        var obligation=await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x=>x.TransactionId==transaction.Id);
        Assert.Equal("renewal",obligation.Purpose);Assert.Equal(rating.Premium,obligation.Premium);Assert.Equal(rating.Fee,obligation.Fee);
        Assert.Equal(basis.Id,(await new PolicyReadService(f.Factory,f.Clock).ReadAsync(f.Underwriter,basis.PolicyId))["versionId"]);
        Assert.Equal(versionId,(await new PolicyReadService(f.Factory,f.Clock).ReadAtAsync(f.Underwriter,basis.PolicyId,next.StartsAt,f.Clock.GetUtcNow()))["versionId"]);
        var retained=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==basis.Id);Assert.Equal(basis.SnapshotJson,retained.SnapshotJson);Assert.Equal(basis.ContentHash,retained.ContentHash);
        await VerifyServicingIssueHttp(db,f,password,cycle.DraftId,version,fence,input,key,issued.Body);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Fictional revoked renewal issue authority check' WHERE Id={decision.GrantId}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issue.IssueAsync(f.Underwriter,cycle.DraftId,version,fence,input,key,Guid.NewGuid()))).Status);
    }
}
