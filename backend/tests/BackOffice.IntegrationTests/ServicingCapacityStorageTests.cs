using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityCaseStorage(BackOfficeDbContext db, DecisionFixture f, ServicingCycle cycle)
    {
        // Run against an existing issued/rated graph, never the shared demo DB.
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260918025724_ServicingUnderwritingSubmission");
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityCase").SingleAsync());
        var referral = await db.Set<ServicingReferral>().AsNoTracking().FirstAsync(x => x.CycleId == cycle.Id);
        var binder = await db.Set<BinderVersion>().AsNoTracking().SingleAsync(x => x.Id == cycle.BinderVersionId);
        var otherBinder = await db.Set<BinderVersion>().AsNoTracking().FirstAsync(x => x.Id != binder.Id);
        var rating = await db.Set<ServicingRatingResult>().AsNoTracking().SingleAsync(x => x.Id == cycle.CurrentRatingId);
        var now = f.Clock.GetUtcNow(); var id = Guid.NewGuid();
        async Task Insert(Guid row, Guid draft, Guid revision, Guid ownerCycle, Guid ownerRating, Guid ownerReferral,
            Guid provider, Guid ownerBinder, string state = "draft", string reason = "Fictional servicing capacity request", Guid? creator = null) =>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCapacityCase (Id,DraftId,RevisionId,CycleId,RatingId,ReferralId,ProviderId,BinderVersionId,Reason,State,RaisedBy,CreatedAt,CreatedBy,UpdatedAt) VALUES ({row},{draft},{revision},{ownerCycle},{ownerRating},{ownerReferral},{provider},{ownerBinder},{reason},{state},{f.Underwriter.UserId},{now},{creator ?? f.Underwriter.UserId},{now})");
        Task Valid(Guid row) => Insert(row, cycle.DraftId, cycle.RevisionId, cycle.Id, rating.Id, referral.Id, binder.ProviderId, binder.Id);
        await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), Guid.NewGuid(), cycle.RevisionId, cycle.Id, rating.Id, referral.Id, binder.ProviderId, binder.Id));
        await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, Guid.NewGuid(), cycle.Id, rating.Id, referral.Id, binder.ProviderId, binder.Id));
        await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, cycle.RevisionId, cycle.Id, Guid.NewGuid(), referral.Id, binder.ProviderId, binder.Id));
        await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, cycle.RevisionId, cycle.Id, rating.Id, Guid.NewGuid(), binder.ProviderId, binder.Id));
        Assert.Equal(51420, (await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, cycle.RevisionId, cycle.Id, rating.Id, referral.Id, otherBinder.ProviderId, otherBinder.Id))).Number);
        Assert.Equal(51420, (await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, cycle.RevisionId, cycle.Id, rating.Id, referral.Id, binder.ProviderId, binder.Id, "approved"))).Number);
        await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, cycle.RevisionId, cycle.Id, rating.Id, referral.Id, binder.ProviderId, binder.Id, reason: "short"));
        await Assert.ThrowsAsync<SqlException>(() => Insert(Guid.NewGuid(), cycle.DraftId, cycle.RevisionId, cycle.Id, rating.Id, referral.Id, binder.ProviderId, binder.Id, creator: f.Servicing.UserId));
        now = rating.ExpiresAt;
        Assert.Equal(51420, (await Assert.ThrowsAsync<SqlException>(() => Valid(Guid.NewGuid()))).Number);
        now = rating.CompletedAt.AddTicks(-1);
        Assert.Equal(51420, (await Assert.ThrowsAsync<SqlException>(() => Valid(Guid.NewGuid()))).Number);
        now = f.Clock.GetUtcNow();
        await Valid(id);
        await Assert.ThrowsAsync<SqlException>(() => Valid(Guid.NewGuid()));
        Assert.Equal(51421, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET Reason='Rewritten original request reason' WHERE Id={id}"))).Number);
        Assert.Equal(51421, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingCapacityCase WHERE Id={id}"))).Number);
        // This prerequisite migration must not permit fabricated carrier authority.
        Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='approved' WHERE Id={id}"))).Number);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='superseded' WHERE Id={id}");
        Assert.Equal(51422, (await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='draft' WHERE Id={id}"))).Number);
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityCase").SingleAsync());
    }
}
