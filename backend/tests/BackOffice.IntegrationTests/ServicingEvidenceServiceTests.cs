using System.Text;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingEvidenceFilesService(BackOfficeDbContext db, DecisionFixture f,
        ServicingCycle cycle, Guid otherDraftId, Guid fence, string etag)
    {
        var service = new ServicingEvidenceService(f.Factory,f.Clock);
        var version = Convert.FromBase64String(etag.Trim('"')); var key = Guid.NewGuid().ToString();
        var bytes = Encoding.UTF8.GetBytes("Fictional servicing evidence document.");
        var name=new string('a',196)+".txt";
        Task<CommandOutcome> Upload(byte[] expected,Guid lease,string commandKey,byte[]? content=null) => service.UploadAsync(f.Servicing,cycle.DraftId,expected,lease,name,"text/plain",content??bytes,commandKey,Guid.NewGuid());
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Upload(new byte[8],fence,Guid.NewGuid().ToString()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Upload(version,Guid.NewGuid(),Guid.NewGuid().ToString()))).Status);
        await Assert.ThrowsAsync<QuoteInputException>(()=>service.UploadAsync(f.Servicing,cycle.DraftId,version,fence,"fake.pdf","application/pdf",bytes,Guid.NewGuid().ToString(),Guid.NewGuid()));
        await Assert.ThrowsAsync<QuoteInputException>(()=>service.UploadAsync(f.Servicing,cycle.DraftId,version,fence,"a"+name,"text/plain",bytes,Guid.NewGuid().ToString(),Guid.NewGuid()));
        var uploaded = await Upload(version,fence,key); Assert.Equal(201,uploaded.Status); Assert.NotEqual(etag,uploaded.Etag);
        var replay = await Upload(version,fence,key); Assert.True(replay.Replayed); Assert.Equal(uploaded.ResourceId,replay.ResourceId);
        await Assert.ThrowsAsync<CommandKeyConflictException>(()=>Upload(version,fence,key,Encoding.UTF8.GetBytes("Different fictional bytes.")));
        var stored=await service.DownloadAsync(f.Servicing,cycle.DraftId,uploaded.ResourceId);
        Assert.Equal(name,stored.FileName);
        Assert.Equal(bytes,stored.Content); Assert.Equal("accepted",stored.ScreeningState); Assert.Equal("demo-signature-v1",stored.ScreeningMethod);
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DownloadAsync(f.Servicing,otherDraftId,uploaded.ResourceId))).Status);
        var time=f.Clock.Current;f.Clock.Current=(await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId)).ExpiresAt;
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Upload(Convert.FromBase64String(uploaded.Etag!.Trim('"')),fence,Guid.NewGuid().ToString()))).Status);
        Assert.Equal(bytes,(await service.DownloadAsync(f.Servicing,cycle.DraftId,uploaded.ResourceId)).Content);f.Clock.Current=time;
        var role=await db.Set<Role>().SingleAsync(x=>x.Code=="servicing");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId} AND RoleId={role.Id}");db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Upload(version,fence,key))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DownloadAsync(f.Servicing,cycle.DraftId,uploaded.ResourceId))).Status);
        Assert.Equal(1,await db.Set<ServicingEvidenceFile>().CountAsync(x=>x.DraftId==cycle.DraftId));
        Assert.Empty(await db.Set<ServicingEvidenceAssociation>().Where(x=>x.DraftId==cycle.DraftId).ToArrayAsync());
    }
}
