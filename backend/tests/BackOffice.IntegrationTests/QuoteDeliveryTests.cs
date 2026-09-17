using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlQuoteDeliveryRecoveryRetainsExactRecipientsAndChecksCurrentPermissionBeforeReplay()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password); var f = setup.Fixture;
            var sent = await new QuoteTermsService(f.Factory, f.Clock).SendAsync(f.Servicing, f.QuoteId, setup.TermsId, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var leases = new SqlJobLeases(f.Factory, f.Clock); var jobs = new QuoteDeliveryJobs(f.Factory, f.Clock);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var lease = (await leases.ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
                Assert.True(await leases.FailAsync(lease, JobFailure.ProviderUnavailable)); f.Clock.Current += TimeSpan.FromHours(1);
            }
            var failed = await jobs.ReadAsync(f.Servicing, delivery.WorkId, default); Assert.False(failed.RetryAllowed);
            await Assert.ThrowsAsync<QuoteOperationException>(() => jobs.RetryAsync(f.Servicing, delivery.WorkId, failed.Work.RowVersion, "Recover original delivery", Guid.NewGuid().ToString(), Guid.NewGuid(), default));
            var role = await db.Set<Role>().SingleAsync(x => x.Code == "system-admin"); var link = new UserRole { UserId = f.Servicing.UserId, RoleId = role.Id }; db.Add(link); await db.SaveChangesAsync();
            var actor = f.Servicing with { Roles = f.Servicing.Roles.Concat(["system-admin"]).ToHashSet() };
            Assert.True((await jobs.ReadAsync(actor, delivery.WorkId, default)).RetryAllowed);
            var key = Guid.NewGuid().ToString(); await jobs.RetryAsync(actor, delivery.WorkId, failed.Work.RowVersion, "Recover original delivery", key, Guid.NewGuid(), default);
            Assert.True((await jobs.RetryAsync(actor, delivery.WorkId, failed.Work.RowVersion, "Recover original delivery", key, Guid.NewGuid(), default)).Replayed);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE Id={link.Id}"); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<QuoteOperationException>(() => jobs.RetryAsync(actor, delivery.WorkId, failed.Work.RowVersion, "Recover original delivery", key, Guid.NewGuid(), default));
            var recovered = (await leases.ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!; var worker = new QuoteDeliveryWorker(f.Factory, f.Clock);
            Assert.True(await worker.ApplyAsync(recovered, await worker.ExecuteProviderAsync(recovered)));
            var after = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == delivery.Id);
            Assert.Equal("delivered", after.State); Assert.Equal(delivery.RecipientSnapshotJson, after.RecipientSnapshotJson); Assert.Equal(delivery.PayloadHash, after.PayloadHash);
            Assert.Equal(7, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == delivery.WorkId)); Assert.Single(await db.Set<QuoteTermsDelivery>().ToArrayAsync());
        });
    }

    private static async Task<(DecisionFixture Fixture, Guid TermsId, Guid ContactId, Guid RatingId)> SignedTerms(BackOfficeDbContext db, string password, bool tradingReferral = false)
    {
        var setup = await TermsFixture(db, password, tradingReferral: tradingReferral); var f = setup.Fixture;
        var terms = await new QuoteTermsService(f.Factory, f.Clock).PrepareAsync(f.Servicing, f.QuoteId, f.CycleId, setup.RatingId, setup.TemplateId, await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
        await TermsProof(db, f, (await new UnderwritingEvidenceService(f.Factory, f.Clock).RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "signed-statement"));
        return (f, terms.ResourceId, setup.ContactId, setup.RatingId);
    }

    [Theory]
    [InlineData("recipient")]
    [InlineData("actor")]
    [InlineData("signature")]
    [InlineData("template")]
    public async Task RealSqlQuoteDeliveryRejectsChangedRecipientActorSignatureOrTemplateAtApplication(string change)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password); var f = setup.Fixture; var service = new QuoteTermsService(f.Factory, f.Clock);
            var sent = await service.SendAsync(f.Servicing, f.QuoteId, setup.TermsId, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var lease = (await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
            var worker = new QuoteDeliveryWorker(f.Factory, f.Clock); var outcome = await worker.ExecuteProviderAsync(lease);
            if (change == "recipient") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Contact SET Email=N'changed@example.invalid' WHERE Id={setup.ContactId}");
            if (change == "actor") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Servicing.UserId}");
            if (change == "template") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE TemplateVersion SET State=N'retired' WHERE Id=(SELECT TemplateVersionId FROM QuoteTermsVersion WHERE Id={setup.TermsId})");
            if (change == "signature")
            {
                var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId && x.RequirementCode == "signed-statement");
                await new UnderwritingEvidenceService(f.Factory, f.Clock).WithdrawAsync(f.Underwriter, f.QuoteId, f.CycleId, proof.Id, await TermsVersion(db, f), proof.RowVersion, "Proof withdrawn before delivery", Guid.NewGuid().ToString(), Guid.NewGuid());
            }
            Assert.True(await worker.ApplyAsync(lease, outcome));
            Assert.Equal("superseded", await db.Set<QuoteTermsDelivery>().AsNoTracking().Where(x => x.Id == delivery.Id).Select(x => x.State).SingleAsync());
            Assert.NotEqual("sent", await db.Set<Quote>().AsNoTracking().Where(x => x.Id == f.QuoteId).Select(x => x.State).SingleAsync());
            Assert.Single(await db.Set<DemoProviderOperation>().Where(x => x.Kind == QuoteTermsService.WorkKind).ToArrayAsync());
        });
    }

    [Theory]
    [InlineData("success")]
    [InlineData("timeout-after-success")]
    [InlineData("transient-once")]
    [InlineData("reject")]
    public async Task RealSqlQuoteDeliveryDurableProviderAndLostLeaseRetainOneOperation(string scenario)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await SignedTerms(db, password); var f = setup.Fixture;
            db.Add(new SettingVersion { Scope = "quote-delivery", Version = 2, EffectiveFrom = f.Clock.Current, Values = JsonSerializer.Serialize(new { demo = true, kind = "quote-delivery", schemaVersion = "1", scenario }) }); await db.SaveChangesAsync();
            var sent = await new QuoteTermsService(f.Factory, f.Clock).SendAsync(f.Servicing, f.QuoteId, setup.TermsId, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
            var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
            var leases = new SqlJobLeases(f.Factory, f.Clock); var worker = new QuoteDeliveryWorker(f.Factory, f.Clock);
            var first = (await leases.ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
            if (scenario is "timeout-after-success" or "transient-once") await Assert.ThrowsAsync<QuoteDeliveryException>(() => worker.ExecuteProviderAsync(first));
            var outcome = await worker.ExecuteProviderAsync(first);
            f.Clock.Current += TimeSpan.FromSeconds(31);
            Assert.False(await worker.ApplyAsync(first, outcome));
            var current = (await leases.ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
            Assert.Equal(outcome, await worker.ExecuteProviderAsync(current)); Assert.True(await worker.ApplyAsync(current, outcome));
            Assert.Equal(scenario == "reject" ? "failed" : "delivered", await db.Set<QuoteTermsDelivery>().AsNoTracking().Where(x => x.Id == delivery.Id).Select(x => x.State).SingleAsync());
            Assert.Single(await db.Set<DemoProviderOperation>().Where(x => x.Kind == QuoteTermsService.WorkKind).ToArrayAsync());
            Assert.Equal(2, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == delivery.WorkId));
            if (scenario == "reject") Assert.Empty(await db.Set<QuoteAcceptance>().ToArrayAsync());
        });
    }
}
