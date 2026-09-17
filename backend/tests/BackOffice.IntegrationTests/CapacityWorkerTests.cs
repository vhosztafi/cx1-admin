using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<(DecisionFixture Fixture, CapacityEscalation Escalation, CapacitySubmission Submission)> CapacityRequest(BackOfficeDbContext db, string password, string scenario, bool includeProof = false)
    {
        var f = await ReadyUnderwriting(db, password, productCode: "motor-trade-combined");
        var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId && x.RuleCode == "stock-limit");
        var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
        var provider = await db.Set<BinderVersion>().Where(x => x.Id == cycle.BinderVersionId).Select(x => x.ProviderId).SingleAsync();
        var quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Underwriter, f.QuoteId); var service = new CapacityService(f.Factory, f.Clock);
        var created = await service.CreateAsync(f.Underwriter, f.QuoteId, f.CycleId, referral.Id, quote.Quote.RowVersion, referral.RowVersion, provider, "Fictional stock capacity request", Guid.NewGuid().ToString(), Guid.NewGuid());
        var escalation = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == created.ResourceId);
        var setting = await db.Set<SettingVersion>().SingleAsync(x => x.Scope == "capacity-escalation/" + scenario);
        quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Underwriter, f.QuoteId);
        var selected = new List<Guid>();
        if (includeProof)
        {
            var evidence = new UnderwritingEvidenceService(f.Factory, f.Clock);
            var purpose = (await evidence.RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "motor-trader-proof");
            var upload = await evidence.UploadAsync(f.Servicing, f.QuoteId, quote.Quote.RowVersion, "trading.txt", "text/plain", System.Text.Encoding.UTF8.GetBytes("Fictional selected trading proof"), Guid.NewGuid().ToString(), Guid.NewGuid());
            quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Underwriter, f.QuoteId);
            var attached = await evidence.AttachAsync(f.Servicing, f.QuoteId, f.CycleId, quote.Quote.RowVersion, upload.ResourceId, purpose.Code, null, null, null,
                purpose.InputFingerprint, "Selected source evidence", Guid.NewGuid().ToString(), Guid.NewGuid()); selected.Add(attached.ResourceId);
            quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Underwriter, f.QuoteId);
        }
        await service.SendAsync(f.Underwriter, f.QuoteId, f.CycleId, escalation.Id, quote.Quote.RowVersion, escalation.RowVersion, "Review fictional stock cover of GBP 150000.\nExact selected evidence accompanies this request.", selected, setting.Id, Guid.NewGuid().ToString(), Guid.NewGuid());
        return (f, escalation, await db.Set<CapacitySubmission>().AsNoTracking().SingleAsync(x => x.EscalationId == escalation.Id));
    }

    [Theory]
    [InlineData("approve-stock-150000", "approved")]
    [InlineData("conditional-security", "conditional")]
    [InlineData("query-proof", "queried")]
    [InlineData("decline-trade", "declined")]
    [InlineData("transient-then-approve", "approved")]
    [InlineData("conflicting-duplicate", "approved")]
    public async Task RealSqlCapacityWorkerRetainsDeterministicOutcomesAcrossCrashAndDuplicate(string scenario, string expected)
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, scenario);
            var leases = new SqlJobLeases(f.Factory, f.Clock); var worker = new CapacityWorker(f.Factory, f.Clock);
            var lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
            if (scenario == "transient-then-approve")
            {
                var failure = await Assert.ThrowsAsync<CapacityProviderException>(() => worker.ExecuteProviderAsync(lease));
                Assert.Equal(JobFailure.ProviderUnavailable, failure.Failure); await leases.FailAsync(lease, failure.Failure);
                f.Clock.Current = f.Clock.Current.AddHours(1); lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
            }
            var outcome = await worker.ExecuteProviderAsync(lease);
            Assert.Equal(outcome, await worker.ExecuteProviderAsync(lease));
            f.Clock.Current = f.Clock.Current.AddMinutes(1); // Provider committed; application process lost its lease.
            Assert.False(await worker.ApplyAsync(lease, outcome));
            lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
            Assert.Equal(outcome, await worker.ExecuteProviderAsync(lease));
            Assert.True(await worker.ApplyAsync(lease, outcome));
            Assert.False(await worker.ApplyAsync(lease, outcome));
            var stored = await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id);
            Assert.Equal(expected, stored.State); Assert.NotNull(stored.CurrentResponseId);
            Assert.Single(await db.Set<DemoProviderOperation>().Where(x => x.OperationKey == lease.OperationKey).ToArrayAsync());
            var inbound = await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.EscalationId == escalation.Id && x.Direction == "inbound");
            Assert.Equal("demo-provider", inbound.Provenance); Assert.Equal("applied", inbound.ApplicationState);
            if (scenario == "conditional-security") Assert.NotNull(inbound.DecisionId);
            if (scenario == "approve-stock-150000")
            {
                var referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == escalation.ReferralId);
                var quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Underwriter, f.QuoteId);
                await new QuoteReferralService(f.Factory, f.Clock).DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, quote.Quote.RowVersion,
                    [new(referral.Id, referral.RowVersion, "approve", "Exact carrier stock extension reviewed", [])], Guid.NewGuid().ToString(), Guid.NewGuid());
                Assert.Equal("approved", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id)).State);
                Assert.Equal("open", (await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.CycleId == f.CycleId && x.RuleCode == "cover-stock-custody")).State);
            }
            if (scenario == "conflicting-duplicate")
            {
                Assert.False(await worker.ApplyAsync(lease, outcome with { Body = "Conflicting duplicate content" }));
                Assert.Single(await db.Set<AdapterQuarantine>().Where(x => x.InboxId == inbound.InboxId).ToArrayAsync());
                Assert.Equal(outcome.Body, (await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.Id == inbound.Id)).Body);
            }
        });
    }

    [Fact]
    public async Task RealSqlCapacityWorkerRetainsStaleCycleResponseWithoutApplyingIt()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "approve-stock-150000");
            var leases = new SqlJobLeases(f.Factory, f.Clock); var worker = new CapacityWorker(f.Factory, f.Clock);
            var lease = (await leases.ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
            var outcome = await worker.ExecuteProviderAsync(lease);
            var quote = await new QuoteService(f.Factory, f.Clock).GetAsync(f.Servicing, f.QuoteId);
            await new QuoteUnderwritingLifecycle(f.Factory, f.Clock).ReturnToDraftAsync(f.Servicing, f.QuoteId, f.CycleId, quote.Quote.RowVersion, "Revise the requested risk", Guid.NewGuid().ToString(), Guid.NewGuid());
            Assert.True(await worker.ApplyAsync(lease, outcome));
            Assert.Null((await db.Set<CapacityEscalation>().AsNoTracking().SingleAsync(x => x.Id == escalation.Id)).CurrentResponseId);
            Assert.Equal("superseded", (await db.Set<CapacityMessage>().AsNoTracking().SingleAsync(x => x.SubmissionId == submission.Id && x.Direction == "inbound")).ApplicationState);
            Assert.Equal("draft", (await new QuoteService(f.Factory, f.Clock).GetAsync(f.Servicing, f.QuoteId)).Quote.State);
        });
    }
}
