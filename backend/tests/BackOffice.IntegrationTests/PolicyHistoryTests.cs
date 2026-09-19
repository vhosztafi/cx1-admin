using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlPolicyHistoryTestsAuthorizeBothVersionsBeforeComparing(string product)
    {
        await WithDatabase(async(db,password)=>
        {
            var first=await AcceptedIssue(db,password,product);var f=first.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,first.Version,first.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var second=await AcceptedIssue(db,password,product=="motor-trade-combined"?"motor-trade-road-risks":"motor-trade-combined");
            await new QuoteIssueService(second.Source.Factory,second.Source.Clock).IssueAsync(second.Source.Underwriter,second.Source.QuoteId,second.Version,second.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var foreign=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.PolicyId!=version.PolicyId);
            var history=new PolicyHistoryService(f.Factory,f.Clock);
            var drafts=new ServicingDraftService(f.Factory,f.Clock);
            var draftList=await drafts.ListAsync(f.Underwriter,version.TermId);
            await drafts.CreateAsync(f.Underwriter,version.TermId,Convert.FromBase64String(draftList.Etag.Trim('"')),
                new("adjustment",version.Id,System.Text.Json.JsonSerializer.SerializeToElement(new{localDate="2026-10-01",localTime="00:00",timeZone="Europe/London"}),"Draft excluded from issued policy history"),Guid.NewGuid().ToString(),Guid.NewGuid());
            var same=await history.CompareAsync(f.Underwriter,version.PolicyId,version.Id,version.Id);
            Assert.Empty(same.Changes);Assert.Equal(Convert.ToHexStringLower(version.ContentHash),same.BeforeHash);
            Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>history.CompareAsync(f.Underwriter,version.PolicyId,version.Id,foreign.Id))).Status);
            Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>history.CompareAsync(f.Underwriter,version.PolicyId,foreign.Id,version.Id))).Status);
            var current=await history.ReadAsync(f.Underwriter,version.PolicyId,f.Clock.GetUtcNow(),f.Clock.GetUtcNow());
            Assert.Single(current.Versions);Assert.Equal(version.Id,current.SelectedVersionId);
            var before=await history.ReadAsync(f.Underwriter,version.PolicyId,f.Clock.GetUtcNow(),version.ProcessedAt.AddTicks(-1));
            Assert.Null(before.SelectedVersionId);Assert.Equal("not-yet-known",Assert.Single(before.Versions).Applicability);
            var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==version.PolicyId);
            var sourceQuote=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==f.QuoteId);
            var sourceRevision=await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x=>x.Id==sourceQuote.CurrentRevisionId);
            var expected=Convert.FromBase64String(current.PolicyEtag.Trim('"'));
            var reconstruction=new PolicyReconstructionInput(version.EffectiveAt,f.Clock.GetUtcNow(),version.Id,Convert.ToHexStringLower(version.ContentHash),"Retain selected policy reconstruction");
            var requestKey=Guid.NewGuid().ToString();
            Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>history.ReconstructAsync(f.Underwriter,version.TermId,new byte[8],reconstruction,Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>history.ReconstructAsync(f.Underwriter,version.TermId,expected,reconstruction with{KnownAt=version.ProcessedAt.AddTicks(-1)},Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            var requested=await history.ReconstructAsync(f.Underwriter,version.TermId,expected,reconstruction,requestKey,Guid.NewGuid());
            Assert.Equal(201,requested.Status);
            Assert.Equal(requested.Body,(await history.ReconstructAsync(f.Underwriter,version.TermId,expected,reconstruction,requestKey,Guid.NewGuid())).Body);
            Assert.Single(await db.Set<PolicyReconstructionRequest>().ToArrayAsync());
            var notCovered=await history.ReconstructAsync(f.Underwriter,version.TermId,expected,reconstruction with{KnownAt=version.ProcessedAt.AddTicks(-1),VersionId=null,ContentHash=null},Guid.NewGuid().ToString(),Guid.NewGuid());
            Assert.Equal(201,notCovered.Status);Assert.Equal("not-covered",await db.Set<PolicyReconstructionRequest>().Where(x=>x.Id==notCovered.ResourceId).Select(x=>x.CoverageState).SingleAsync());
            var cloneInput=new PolicyCloneInput(version.Id,policy.RelationshipId,sourceRevision.AgencyTermsVersionId,"Clone policy into fresh incomplete quotation");
            Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>history.CloneAsync(f.Underwriter,policy.Id,expected,cloneInput with{ConfirmedTermsId=Guid.NewGuid()},Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            var foreignRelationship=await db.Set<Policy>().Where(x=>x.Id==foreign.PolicyId).Select(x=>x.RelationshipId).SingleAsync();
            Assert.NotEqual(policy.AgencyId,await db.Set<Policy>().Where(x=>x.Id==foreign.PolicyId).Select(x=>x.AgencyId).SingleAsync());
            Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>history.CloneAsync(f.Underwriter,policy.Id,expected,cloneInput with{RelationshipId=foreignRelationship},Guid.NewGuid().ToString(),Guid.NewGuid()))).Status);
            var cloneKey=Guid.NewGuid().ToString();var cloned=await history.CloneAsync(f.Underwriter,policy.Id,expected,cloneInput,cloneKey,Guid.NewGuid());
            Assert.Equal(201,cloned.Status);Assert.Equal(cloned.Body,(await history.CloneAsync(f.Underwriter,policy.Id,expected,cloneInput,cloneKey,Guid.NewGuid())).Body);
            var clone=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==cloned.ResourceId);
            Assert.Equal("draft",clone.State);Assert.Null(clone.BoundPolicyId);Assert.Null(clone.CurrentUnderwritingCycleId);
            var cloneRevision=await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x=>x.Id==clone.CurrentRevisionId);
            using var cloneJson=System.Text.Json.JsonDocument.Parse(cloneRevision.ProposalJson);
            Assert.False(cloneJson.RootElement.TryGetProperty("termIntent",out _));Assert.False(cloneJson.RootElement.TryGetProperty("premium",out _));
            Assert.Single(await db.Set<PolicyQuoteClone>().ToArrayAsync());
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE PolicyQuoteClone SET Reason='Attempt to rewrite source lineage' WHERE QuoteId={clone.Id}"));
            await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM PolicyQuoteClone WHERE QuoteId={clone.Id}"));
            await VerifyPolicyHistoryHttp(db,f,password,policy,version,sourceRevision.AgencyTermsVersionId,foreign.Id);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State='suspended' WHERE Id={f.Underwriter.UserId}");
            await Assert.ThrowsAsync<QuoteOperationException>(()=>history.CompareAsync(f.Underwriter,version.PolicyId,version.Id,version.Id));
            await Assert.ThrowsAsync<QuoteOperationException>(()=>history.CloneAsync(f.Underwriter,policy.Id,expected,cloneInput,cloneKey,Guid.NewGuid()));
            await Assert.ThrowsAsync<QuoteOperationException>(()=>history.ReconstructAsync(f.Underwriter,version.TermId,expected,reconstruction,requestKey,Guid.NewGuid()));
        });
    }
}
