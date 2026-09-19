using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyCancellationIssueRace(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,ServicingAcceptance acceptance,Guid fence,string etag)
    {
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==cycle.BaseVersionId);
        await VerifyCancellationOfAdjustedLedger(db,f,basis.Id,false,async cancellation=>
        {
            var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<(CommandOutcome? Result,QuoteOperationException? Error)> Attempt(Func<Task<CommandOutcome>> action)
            {
                await gate.Task;
                try{return(await action(),null);}catch(QuoteOperationException error){return(null,error);}
            }
            var adjustment=Attempt(()=>new ServicingIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,cycle.DraftId,Convert.FromBase64String(etag.Trim('"')),fence,
                new(cycle.Id,acceptance.RatingId,acceptance.TermsVersionId,acceptance.Id,acceptance.TermsHash,acceptance.AssuranceHash,"Issue accepted adjustment racing cancellation"),Guid.NewGuid().ToString(),Guid.NewGuid()));
            var cancel=Attempt(()=>cancellation.Service.IssueAsync(f.Underwriter,cancellation.DraftId,cancellation.Version,cancellation.Lease,cancellation.Input,Guid.NewGuid().ToString(),Guid.NewGuid()));
            gate.SetResult();var outcomes=await Task.WhenAll(adjustment,cancel);
            Assert.Equal(201,Assert.Single(outcomes,x=>x.Result is not null).Result!.Status);
            Assert.Contains(Assert.Single(outcomes,x=>x.Error is not null).Error!.Status,new[]{409,412});
            Assert.Equal(2,await db.Set<PolicyTransaction>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync(x=>x.PostedAt!=null));
            Assert.Equal(basis.ContentHash,await db.Set<PolicyVersion>().Where(x=>x.Id==basis.Id).Select(x=>x.ContentHash).SingleAsync());
            var cancelled=(await cancel).Result is not null;
            Assert.Equal(cancelled?4:0,await db.Set<CancellationConsequence>().CountAsync());
            if(cancelled)Assert.Equal("abandoned",await db.Set<ServicingDraft>().Where(x=>x.Id==cycle.DraftId).Select(x=>x.State).SingleAsync());
            else Assert.Contains("servicing-base-stale",(await cancellation.Service.ReadAsync(f.Underwriter,cancellation.DraftId)).Blockers);
        });
    }
}
