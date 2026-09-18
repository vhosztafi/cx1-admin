using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingSubmissionStorage(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,ServicingCycle older)
    {
        // Exercise additive down/up against a pre-existing issued/rated graph.
        var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20260918002325_ServicingWarrantyEvidence");await migrator.MigrateAsync();
        var now=f.Clock.GetUtcNow();var id=Guid.NewGuid();
        var olderRating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.CycleId==older.Id);
        var currentRating=await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentRatingId);
        async Task Insert(Guid submission,Guid draft,Guid ownerCycle,Guid revision,Guid rating,byte[] hash,Guid actor,Guid creator,string reason="Fictional underwriting handoff")=>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingUnderwritingSubmission (Id,DraftId,CycleId,RevisionId,RatingId,InputHash,Reason,SubmittedBy,SubmittedAt,CreatedBy,CreatedAt) VALUES ({submission},{draft},{ownerCycle},{revision},{rating},{hash},{reason},{actor},{now},{creator},{now})");
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),Guid.NewGuid(),cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,Guid.NewGuid(),cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,olderRating.Id,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId));
        Assert.Equal(51410,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,new byte[32],f.Servicing.UserId,f.Servicing.UserId))).Number);
        Assert.Equal(51410,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,older.Id,older.RevisionId,olderRating.Id,older.InputHash,f.Servicing.UserId,f.Servicing.UserId))).Number);
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Underwriter.UserId));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId,"short"));
        now=currentRating.CompletedAt.AddTicks(-1);
        Assert.Equal(51410,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId))).Number);
        now=currentRating.ExpiresAt;
        Assert.Equal(51410,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId))).Number);
        now=f.Clock.GetUtcNow();
        await Insert(id,cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId);
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),cycle.DraftId,cycle.Id,cycle.RevisionId,cycle.CurrentRatingId!.Value,cycle.InputHash,f.Servicing.UserId,f.Servicing.UserId));
        Assert.Equal(51411,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingUnderwritingSubmission SET Reason='Rewrite immutable submission' WHERE Id={id}"))).Number);
        Assert.Equal(51411,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingUnderwritingSubmission WHERE Id={id}"))).Number);
        Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingUnderwritingSubmission").SingleAsync());
    }
}
