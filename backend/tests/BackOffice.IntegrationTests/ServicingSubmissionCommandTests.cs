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
    private static async Task VerifyServicingSubmissionCommand(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,
        ServicingCycle older,Guid fence,string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        var service=new ServicingSubmissionService(f.Factory,f.Clock);var key=Key();
        var empty=await service.ReadAsync(f.Servicing,cycle.DraftId);Assert.Empty(empty.Items);Assert.Null(empty.Current);
        Assert.Equal(400,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReadAsync(f.Servicing,cycle.DraftId,pageSize:51))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReadAsync(f.Servicing,cycle.DraftId,Guid.NewGuid()))).Status);
        const string reason="Submit fictional servicing for review";
        Task<CommandOutcome> Submit(string commandKey,string version,Guid? lease=null,Guid? draft=null,Guid? ownerCycle=null,Guid? revision=null,string why=reason)=>
            service.SubmitAsync(f.Servicing,draft??cycle.DraftId,ownerCycle??cycle.Id,revision??cycle.RevisionId,Version(version),lease??fence,why,commandKey,Guid.NewGuid());
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),etag,draft:Guid.NewGuid()))).Status);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),etag,ownerCycle:Guid.NewGuid()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),etag,ownerCycle:older.Id))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),etag,revision:Guid.NewGuid()))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),"\"AAAAAAAAAAA=\""))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),etag,lease:Guid.NewGuid()))).Status);
        var now=f.Clock.Current;var rating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId);
        f.Clock.Current=rating.ExpiresAt;
        Assert.Equal("servicing-rating-expired",(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),etag))).Code);f.Clock.Current=now;
        var proof=new ServicingEvidenceService(f.Factory,f.Clock);
        var requirements=await proof.RequirementsAsync(f.Servicing,cycle.DraftId);
        Assert.NotEmpty(requirements.Requirements);
        Assert.Empty(await db.Set<ServicingEvidenceAssociation>().Where(x=>x.CycleId==cycle.Id).ToArrayAsync());
        var referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).OrderBy(x=>x.Id).ToArrayAsync();
        var submitted=await Submit(key,etag);
        Assert.Equal(201,submitted.Status);Assert.False(submitted.Replayed);Assert.NotEqual(etag,submitted.Etag);
        var row=await db.Set<ServicingUnderwritingSubmission>().AsNoTracking().SingleAsync(x=>x.Id==submitted.ResourceId);
        Assert.Equal(cycle.Id,row.CycleId);Assert.Equal(cycle.InputHash,row.InputHash);Assert.Equal(f.Servicing.UserId,row.SubmittedBy);
        Assert.Equal(reason,row.Reason);Assert.Equal(cycle.RevisionId,row.RevisionId);Assert.Equal(rating.Id,row.RatingId);
        Assert.Equal(submitted.Etag,JsonSerializer.Deserialize<JsonElement>(submitted.Body).GetProperty("draftEtag").GetString());
        var replay=await Submit(key,etag);Assert.True(replay.Replayed);Assert.Equal(submitted.Body,replay.Body);
        var read=await service.ReadAsync(f.Servicing,cycle.DraftId);Assert.Equal(submitted.ResourceId,read.Current!.Id);
        Assert.True(read.Current.Applicable);Assert.True(Assert.Single(read.Items).Applicable);Assert.Equal(submitted.Etag,read.DraftEtag);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(Key(),submitted.Etag!))).Status);
        await Assert.ThrowsAsync<CommandKeyConflictException>(()=>Submit(key,etag,why:"Changed fictional reason on retry"));
        Assert.Equal(referrals.Select(x=>(x.Id,x.State,x.LatestDecisionId)),
            (await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==cycle.Id).OrderBy(x=>x.Id).ToArrayAsync()).Select(x=>(x.Id,x.State,x.LatestDecisionId)));
        Assert.Empty(await db.Set<ServicingEvidenceAssociation>().Where(x=>x.CycleId==cycle.Id).ToArrayAsync());
        Assert.Single(await db.Set<AuditEvent>().Where(x=>x.EventType=="servicing.submitted").ToArrayAsync());
        var roles=await db.Set<UserRole>().AsNoTracking().Where(x=>x.UserId==f.Servicing.UserId).ToArrayAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(key,etag))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ReadAsync(f.Servicing,cycle.DraftId))).Status);
        db.ChangeTracker.Clear();db.AddRange(roles);await db.SaveChangesAsync();
        // A still-known receipt never bypasses the current editing lease.
        f.Clock.Current=now.AddMinutes(6);
        Assert.Equal("servicing-lease-conflict",(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(key,etag))).Code);f.Clock.Current=now;
        var rerated=await new ServicingRatingService(f.Factory,f.Clock).RateAsync(f.Servicing,cycle.DraftId,cycle.RevisionId,
            Version(submitted.Etag!),fence,"Refresh submitted fictional pricing",Key(),Guid.NewGuid());
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Submit(key,etag))).Status);
        var history=await service.ReadAsync(f.Servicing,cycle.DraftId);Assert.Null(history.Current);Assert.False(Assert.Single(history.Items).Applicable);
        var refreshed=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==rerated.ResourceId);
        var worker=new ServicingRatingWorker(f.Factory,f.Clock);
        var job=Assert.IsType<JobLease>(await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync("servicing-rating",refreshed.WorkId));
        Assert.True(await worker.ApplyAsync(job,await worker.ExecuteProviderAsync(job)));
        var refreshedView=await new ServicingRatingReadModel(f.Factory,f.Clock).ReadAsync(f.Servicing,cycle.DraftId);
        f.Clock.Current=now.AddSeconds(1);
        var second=await Submit(Key(),refreshedView.DraftEtag,ownerCycle:refreshed.Id);
        var page=await service.ReadAsync(f.Servicing,cycle.DraftId,pageSize:1);
        Assert.Equal(second.ResourceId,Assert.Single(page.Items).Id);Assert.Equal(second.ResourceId,page.Current!.Id);
        Assert.True(page.Current.Applicable);Assert.Equal(second.ResourceId,page.NextBeforeId);
        var previous=await service.ReadAsync(f.Servicing,cycle.DraftId,page.NextBeforeId,1);
        Assert.Equal(submitted.ResourceId,Assert.Single(previous.Items).Id);Assert.False(previous.Items[0].Applicable);
        Assert.Equal(second.ResourceId,previous.Current!.Id);Assert.True(previous.Current.Applicable);Assert.Null(previous.NextBeforeId);
        await new ServicingDraftService(f.Factory,f.Clock).AbandonAsync(f.Servicing,cycle.DraftId,Version(second.Etag!),fence,
            "Abandon fictional submitted case",Key(),Guid.NewGuid());
        Assert.Equal(2,await db.Set<ServicingUnderwritingSubmission>().CountAsync(x=>x.DraftId==cycle.DraftId));
        var closed=await service.ReadAsync(f.Servicing,cycle.DraftId);Assert.Null(closed.Current);Assert.Equal(2,closed.Items.Count);
        Assert.All(closed.Items,x=>Assert.False(x.Applicable));
    }
}
