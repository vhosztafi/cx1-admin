using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<string> VerifyRenewalAcceptanceLapseRace(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,
        ServicingAcceptanceInput input,byte[] version,Guid fence)
    {
        var lifecycle=new RenewalLifecycleService(f.Factory,f.Clock);var before=await lifecycle.ReadAsync(f.Underwriter,cycle.BaseTermId);
        var terms=new ServicingTermsService(f.Factory,f.Clock);
        async Task<(CommandOutcome? Result,QuoteOperationException? Error)> Attempt(Func<Task<CommandOutcome>> action)
        {try{return(await action(),null);}catch(QuoteOperationException error){return(null,error);}}
        var accept=Attempt(()=>terms.AcceptAsync(f.Underwriter,cycle.DraftId,version,fence,input,Guid.NewGuid().ToString(),Guid.NewGuid()));
        var lapse=Attempt(()=>lifecycle.LapseAsync(f.Underwriter,cycle.BaseTermId,Convert.FromBase64String(before.Etag.Trim('"')),
            "Fictional concurrent decline of the renewal",Guid.NewGuid().ToString(),Guid.NewGuid()));
        var results=await Task.WhenAll(accept,lapse);var winner=Assert.Single(results,x=>x.Result is not null);
        Assert.Equal(409,Assert.Single(results,x=>x.Error is not null).Error!.Status);
        var state=await lifecycle.ReadAsync(f.Underwriter,cycle.BaseTermId);
        if(accept.Result.Result is not null)
        {Assert.Equal("accepted",state.State);Assert.Equal(1,await db.Set<ServicingAcceptance>().CountAsync());Assert.Empty(await db.Set<RenewalLapseEvent>().ToArrayAsync());}
        else
        {
            Assert.Equal("lapsed",state.State);Assert.Equal(1,await db.Set<RenewalLapseEvent>().CountAsync());Assert.Empty(await db.Set<ServicingAcceptance>().ToArrayAsync());
            var drafts=new ServicingDraftService(f.Factory,f.Clock);var retained=await drafts.ReadAsync(f.Underwriter,cycle.DraftId);
            using var json=System.Text.Json.JsonDocument.Parse(retained.Body);Assert.Equal("lapsed",json.RootElement.GetProperty("state").GetString());
            Assert.False(json.RootElement.GetProperty("lease").GetProperty("active").GetBoolean());
            Assert.Equal("renewal-already-lapsed",(await Assert.ThrowsAsync<QuoteOperationException>(()=>drafts.LeaseAsync(f.Underwriter,cycle.DraftId,
                Convert.FromBase64String(retained.Etag.Trim('"')),"acquire",null,null,Guid.NewGuid().ToString(),Guid.NewGuid()))).Code);
        }
        Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(1,await db.Set<PolicyTransaction>().CountAsync());
        Assert.Equal(cycle.BaseVersionId,(await new PolicyReadService(f.Factory,f.Clock).ReadAsync(f.Underwriter,cycle.PolicyId))["versionId"]);
        return winner.Result!.Etag!;
    }
}
