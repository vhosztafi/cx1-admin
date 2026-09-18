using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<string> VerifyServicingCapacityChase(BackOfficeDbContext db, DecisionFixture f,
        ServicingCapacityCase capacity, Guid submission, Guid lease, string etag)
    {
        var service = new ServicingCapacityService(f.Factory,f.Clock);
        var key = Guid.NewGuid().ToString();
        Task<CommandOutcome> Chase(string command, ActorContext? actor=null, Guid? owner=null, Guid? request=null,
            byte[]? version=null, byte[]? caseVersion=null, Guid? fence=null, string body="Please provide a fictional progress update") =>
            service.ChaseAsync(actor ?? f.Underwriter,capacity.DraftId,capacity.CycleId,owner ?? capacity.Id,request ?? submission,
                version ?? Convert.FromBase64String(etag.Trim('"')),caseVersion ?? capacity.RowVersion,fence ?? lease,body,
                "Chase fictional provider request",command,Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),actor:f.Servicing))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),owner:Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),request:Guid.NewGuid()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),fence:Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),version:new byte[8]))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),caseVersion:new byte[8]))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(Guid.NewGuid().ToString(),body:new string('x',10001)))).Status);
        var chased = await Chase(key); Assert.Equal(201,chased.Status);
        Assert.True((await Chase(key)).Replayed);
        await Assert.ThrowsAsync<CommandKeyConflictException>(()=>Chase(key,body:"Different fictional chase content"));
        var message = await db.Set<ServicingCapacityMessage>().AsNoTracking().SingleAsync(x=>x.Kind=="chase");
        Assert.Equal(chased.ResourceId,message.Id); Assert.Equal(submission,message.SubmissionId); Assert.Equal(2,message.Sequence);
        Assert.Single(await db.Set<ServicingCapacitySubmission>().ToArrayAsync());
        Assert.Equal("queued",await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.State).SingleAsync());
        Assert.Single(await db.Set<AuditEvent>().Where(x=>x.EventType=="servicing.capacity-chased").ToArrayAsync());
        var roles = await db.Set<UserRole>().AsNoTracking().Where(x=>x.UserId==f.Underwriter.UserId).ToArrayAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Underwriter.UserId}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Chase(key))).Status);
        db.ChangeTracker.Clear(); db.AddRange(roles); await db.SaveChangesAsync();
        return chased.Etag!;
    }
}
