using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalCancellationExpiredNoticeWithRevokedSenderBecomesOneException()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await VerifyCancellationDocument(db,f.Factory,f.Clock,f.Underwriter,afterIssue:async()=>
        {
            var notice=await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x=>x.Kind=="notice");
            var lease=await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync("cancellation-notice",notice.WorkId);Assert.NotNull(lease);
            var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==f.Underwriter.UserId);user.State="suspended";await db.SaveChangesAsync();
            var worker=new CancellationOperationsWorker(f.Factory,new TaskService(f.Factory,new SqlCommandBoundary(f.Factory,f.Clock),f.Clock),f.Clock);
            await worker.RecordNoticeUnavailable(notice.WorkId);Assert.Equal("leased",await db.Set<OutboxWork>().Where(x=>x.Id==notice.WorkId).Select(x=>x.State).SingleAsync());
            f.Clock.Current+=SqlJobLeases.LeaseDuration+TimeSpan.FromSeconds(1);
            await worker.RecordNoticeUnavailable(notice.WorkId);await worker.RecordNoticeUnavailable(notice.WorkId);
            Assert.Equal("failed",await db.Set<OutboxWork>().Where(x=>x.Id==notice.WorkId).Select(x=>x.State).SingleAsync());
            Assert.Equal(1,await db.Set<JobException>().CountAsync(x=>x.WorkId==notice.WorkId));
            Assert.Equal("rejected",await db.Set<AdapterAttempt>().Where(x=>x.WorkId==notice.WorkId).Select(x=>x.Outcome).SingleAsync());
            Assert.Empty(await db.Set<CancellationOperationalReceipt>().ToArrayAsync());Assert.Empty(await db.Set<OperationalDelivery>().ToArrayAsync());
        });
    });
    [Theory][InlineData(false)][InlineData(true)]
    public Task RealSqlOperationalCancellationCommercialWithdrawsOnlyCoveredEl(bool employersSelected) => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var source = await CommercialIssueCommand(db, cycle, acceptance, actorId);
        await source.Service.IssueAsync(source.Actor, source.Quote.Id, source.Quote.RowVersion, source.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        await using (var tx = await db.Database.BeginTransactionAsync()) { await CommercialUnderwritingCancellationSeed.SeedAsync(db); await tx.CommitAsync(); }
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(); var clock = new RatingClock { Current = term.StartsAt.AddDays(1) };
        await VerifyCancellationDocument(db, source.Factory, clock, source.Actor, afterIssue: async () =>
        {
            var effects = await db.Set<CancellationConsequence>().AsNoTracking().ToArrayAsync(); Assert.DoesNotContain(effects, x => x.Kind == "mid-removal");
            Assert.Equal(employersSelected, effects.Any(x => x.Kind == "certificate-withdrawal"));
            var leases = new SqlJobLeases(source.Factory, clock); var commands = new SqlCommandBoundary(source.Factory, clock);
            var worker = new CancellationOperationsWorker(source.Factory, new TaskService(source.Factory, commands, clock), clock);
            var effective = await db.Set<CancellationIssueDecision>().Select(x => x.EffectiveAt).SingleAsync();
            foreach (var effect in effects.Where(x => x.Kind != "notice")) Assert.Null(await leases.ClaimWorkAsync("cancellation-" + effect.Kind, effect.WorkId));
            clock.Current = effective;
            foreach (var effect in effects.Where(x => x.Kind != "notice"))
            { var lease = await leases.ClaimWorkAsync("cancellation-" + effect.Kind, effect.WorkId); Assert.NotNull(lease); Assert.True(await worker.Apply(lease)); }
            Assert.Equal(employersSelected ? 1 : 0, await db.Set<CertificateWithdrawal>().CountAsync()); Assert.Empty(await db.Set<MidSubmission>().ToArrayAsync());
            Assert.Empty(await db.Set<PolicyMidIntent>().ToArrayAsync());
        });
    }, stopAfterAccepted: true, configureProposal: proposal =>
    {
        proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x => x!["questionId"]!.GetValue<string>() == "prototype.quote.36ef01068295")!["value"] = employersSelected;
        if (!employersSelected) { proposal["risk"]!["liability"]!.AsObject().Remove("employersLimit"); proposal["risk"]!["liability"]!.AsObject().Remove("employersReferenceNumber"); }
    });

    [Fact]
    public Task RealSqlOperationalCancellationRevokedActorCannotWithdrawOrCloseTasks() => WithDatabase(async (db, password) =>
    {
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        await VerifyCancellationDocument(db, f.Factory, f.Clock, f.Underwriter, afterIssue: async () =>
        {
            var effect = await db.Set<CancellationConsequence>().AsNoTracking().SingleAsync(x => x.Kind == "certificate-withdrawal");
            f.Clock.Current = await db.Set<CancellationIssueDecision>().Select(x => x.EffectiveAt).SingleAsync();
            var lease = await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync("cancellation-certificate-withdrawal", effect.WorkId); Assert.NotNull(lease);
            var user = await db.Set<StaffUser>().SingleAsync(x => x.Id == f.Underwriter.UserId); user.State = "suspended"; await db.SaveChangesAsync();
            var worker = new CancellationOperationsWorker(f.Factory, new TaskService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock), f.Clock);
            await Assert.ThrowsAsync<OperationalAccessException>(() => worker.Apply(lease));
            Assert.Empty(await db.Set<CertificateWithdrawal>().ToArrayAsync()); Assert.Empty(await db.Set<CancellationOperationalReceipt>().ToArrayAsync());
        });
    });
}
