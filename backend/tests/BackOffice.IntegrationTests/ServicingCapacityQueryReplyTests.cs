using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityQueryReply(BackOfficeDbContext db,DecisionFixture f,ServicingCapacityCase capacity,
        Guid responseId,Guid editLease,Guid scenarioVersionId)
    {
        var service=new ServicingCapacityService(f.Factory,f.Clock);
        var version=await db.Set<ServicingDraft>().Where(x=>x.Id==capacity.DraftId).Select(x=>x.RowVersion).SingleAsync();
        var child=await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.RowVersion).SingleAsync();
        Task<CommandOutcome> Reply(Guid response,byte[] expected,string key)=>service.ReplyAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,
            capacity.Id,response,expected,child,editLease,"Fictional answer to the retained carrier query","Resubmit with fictional carrier clarification",[],scenarioVersionId,key,Guid.NewGuid());
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Reply(Guid.NewGuid(),version,Guid.NewGuid().ToString()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Reply(responseId,new byte[8],Guid.NewGuid().ToString()))).Status);
        var key=Guid.NewGuid().ToString();var result=await Reply(responseId,version,key);
        Assert.Equal(202,result.Status);Assert.True((await Reply(responseId,version,key)).Replayed);
        var submissions=await db.Set<ServicingCapacitySubmission>().AsNoTracking().Where(x=>x.CaseId==capacity.Id).OrderBy(x=>x.Sequence).ToArrayAsync();
        Assert.Equal(2,submissions.Length);
        var final=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==capacity.Id);
        Assert.Equal("queued",final.State);Assert.Null(final.CurrentResponseId);Assert.Equal(submissions[1].Id,final.CurrentSubmissionId);
        var reply=await db.Set<ServicingCapacityMessage>().AsNoTracking().SingleAsync(x=>x.CaseId==capacity.Id && x.Kind=="query-reply");
        Assert.Equal(submissions[0].Id,reply.SubmissionId);
        Assert.Equal(3,await db.Set<ServicingCapacityMessage>().CountAsync(x=>x.CaseId==capacity.Id));
        Assert.Equal(responseId,await db.Set<ServicingCapacityResponseRecord>().Where(x=>x.CaseId==capacity.Id).Select(x=>x.Id).SingleAsync());
    }
}
