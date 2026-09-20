using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    private async Task VerifyCommercialServicingRace(BackOfficeDbContext db,Guid actorId,RatingClock clock,PolicyVersion basis,Func<Task<CommandOutcome>> adjustment)
    {
        await CommercialTermsScenario(async(_,cycle,acceptance,otherActor,_)=>
        {
            var other=await CommercialIssueCommand(db,cycle,acceptance,otherActor);
            using var firstJson=JsonDocument.Parse(basis.SnapshotJson);
            var owned=CommercialExposureProjection.Locations(firstJson.RootElement);
            // The second fixture has the same independent commercial risk,
            // agency and client scope differ; each command alone fits.
            var district=owned[0].District;var original=owned.Where(x=>x.District==district).Sum(x=>x.SumInsured);
            await PublishCommercialTestLimit(db,actorId,clock.GetUtcNow(),original*2+50000m);
            async Task<object> Attempt(Func<Task<CommandOutcome>> command)
            {try{return await command();}catch(CommercialExposureConflictException error){return error;}catch(QuoteOperationException error) when(error.Code=="commercial-exposure-busy"){return error;}}
            var key=Guid.NewGuid().ToString();
            Func<Task<CommandOutcome>>[] commands=[adjustment,()=>other.Service.IssueAsync(other.Actor,other.Quote.Id,other.Quote.RowVersion,other.Input,key,Guid.NewGuid())];
            var results=await Task.WhenAll(commands.Select(Attempt));
            // A bounded fence timeout is safe contention, not an approval. Once
            // the competing command finishes, the unchanged request must still
            // be reassessed against its committed exposure.
            for(var index=0;index<results.Length;index++)if(results[index] is QuoteOperationException {Code:"commercial-exposure-busy"})results[index]=await Attempt(commands[index]);
            Assert.Single(results,x=>x is CommandOutcome {Status:201});
            var conflict=Assert.IsType<CommercialExposureConflictException>(Assert.Single(results,x=>x is CommercialExposureConflictException));
            Assert.Contains(conflict.Assessment.Intervals,x=>x.Blocker=="commercial-district-capacity-exceeded");
            Assert.Equal(2,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());
            Assert.Equal(2,await db.Set<CommercialExposureIssueDecision>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync());
            Assert.False(await db.Set<PolicyMidIntent>().AnyAsync());
        },stopAfterAccepted:true,existingDb:db);
    }
}
