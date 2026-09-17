using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class PolicyTemporalTests
{
    private static readonly Guid Policy = Guid.NewGuid(), Term = Guid.NewGuid();
    private static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static PolicyTemporalCandidate Version(int transaction, string effective, string processed, string kind = "adjustment") =>
        new(Policy, Term, Guid.NewGuid(), At("2026-01-01T00:00:00Z"), At("2027-01-01T00:00:00Z"),
            At(effective), At(processed), transaction, 1, kind);

    [Fact]
    public void FutureChangesCannotReplaceCurrentCoverAndKnowledgeCutoffExcludesLaterBackdates()
    {
        var initial=Version(1,"2026-01-01T00:00:00Z","2025-12-20T12:00:00Z","new-business");
        var future=Version(2,"2026-10-01T00:00:00Z","2026-09-01T12:00:00Z");
        var laterBackdate=Version(3,"2026-08-01T00:00:00Z","2026-09-20T12:00:00Z");
        Assert.Equal(initial.VersionId,PolicyTemporalSelector.Select([initial,future,laterBackdate],Policy,At("2026-09-15T00:00:00Z"),At("2026-09-15T00:00:00Z"))!.Candidate.VersionId);
        Assert.Equal(laterBackdate.VersionId,PolicyTemporalSelector.Select([initial,future,laterBackdate],Policy,At("2026-09-15T00:00:00Z"),At("2026-09-21T00:00:00Z"))!.Candidate.VersionId);
    }

    [Fact]
    public void EarlyRenewalDoesNotReplaceTheCurrentTermAndBoundaryIsExclusive()
    {
        var initial=Version(1,"2026-01-01T00:00:00Z","2025-12-20T12:00:00Z","new-business");
        var renewal=Version(2,"2027-01-01T00:00:00Z","2026-12-01T12:00:00Z","renewal") with {TermId=Guid.NewGuid(),StartsAt=At("2027-01-01T00:00:00Z"),EndsAt=At("2028-01-01T00:00:00Z")};
        Assert.Equal(Term,PolicyTemporalSelector.Select([initial,renewal],Policy,At("2026-12-15T00:00:00Z"),At("2026-12-15T00:00:00Z"))!.Candidate.TermId);
        Assert.Equal(renewal.TermId,PolicyTemporalSelector.Select([initial,renewal],Policy,renewal.StartsAt,renewal.StartsAt)!.Candidate.TermId);
        Assert.Equal(initial.VersionId,PolicyTemporalSelector.AtTermEnd([initial,renewal],Policy,Term,initial.EndsAt,renewal.StartsAt)!.VersionId);
    }

    [Fact]
    public void ScheduledCancellationOnlyCancelsAtItsEffectiveInstant()
    {
        var initial=Version(1,"2026-01-01T00:00:00Z","2025-12-20T12:00:00Z","new-business");
        var cancellation=Version(2,"2026-10-01T00:00:00Z","2026-09-01T12:00:00Z","cancellation");
        Assert.Equal("active",PolicyTemporalSelector.Select([initial,cancellation],Policy,At("2026-09-15T00:00:00Z"),At("2026-09-15T00:00:00Z"))!.State);
        Assert.Equal("cancelled",PolicyTemporalSelector.Select([initial,cancellation],Policy,cancellation.EffectiveAt,cancellation.EffectiveAt)!.State);
    }

    [Fact]
    public void SameInstantUsesTransactionThenSliceAndForeignPolicyNeverParticipates()
    {
        var a=Version(1,"2026-01-01T00:00:00Z","2025-12-20T12:00:00Z","new-business");
        var b=a with {VersionId=Guid.NewGuid(),TransactionSequence=2};
        var c=b with {VersionId=Guid.NewGuid(),SliceOrdinal=2};
        var foreign=c with {PolicyId=Guid.NewGuid(),TransactionSequence=99};
        Assert.Equal(c.VersionId,PolicyTemporalSelector.Select([foreign,a,c,b],Policy,a.EffectiveAt,a.EffectiveAt)!.Candidate.VersionId);
        Assert.Null(PolicyTemporalSelector.Select([foreign],Policy,a.EffectiveAt,a.EffectiveAt));
    }

    [Fact]
    public void NotYetKnownHasNoSelectionWhileKnownFutureTermIsClearlyScheduled()
    {
        var initial=Version(1,"2026-01-01T00:00:00Z","2025-12-20T12:00:00Z","new-business");
        Assert.Null(PolicyTemporalSelector.Select([initial],Policy,initial.EffectiveAt,At("2025-12-19T00:00:00Z")));
        Assert.Equal("scheduled",PolicyTemporalSelector.Select([initial],Policy,At("2025-12-25T00:00:00Z"),At("2025-12-25T00:00:00Z"))!.State);
        Assert.Equal("expired",PolicyTemporalSelector.Select([initial],Policy,initial.EndsAt,initial.EndsAt)!.State);
    }
}

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlPolicyTemporalTestsReadKnownIssuedBytesAndRejectForeignTerm()
    {
        await WithDatabase(async (db,password) => {
            var setup=await AcceptedIssue(db,password); var f=setup.Source;
            var beforeIssue=await QuoteDiscovery.ListVersionAsync(db,CancellationToken.None);
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issuedList=await QuoteDiscovery.ListVersionAsync(db,CancellationToken.None);
            Assert.NotEqual(beforeIssue,issuedList);
            var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Id==f.Servicing.UserId);
            db.Add(new UserSession {UserId=user.Id,TokenHash=System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),SecurityStamp=user.SecurityStamp,CreatedAt=f.Clock.GetUtcNow(),ExpiresAt=f.Clock.GetUtcNow().AddHours(1),LastSeenAt=f.Clock.GetUtcNow(),DeviceLabel="Fictional cursor isolation test",TicketCiphertext=[1]});
            await db.SaveChangesAsync();
            Assert.Equal(issuedList,await QuoteDiscovery.ListVersionAsync(db,CancellationToken.None));
            var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
            var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var reader=new PolicyReadService(f.Factory,f.Clock);
            var view=await reader.ReadAtAsync(f.Servicing,policy.Id,version.EffectiveAt,version.ProcessedAt);
            Assert.Equal(version.Id,view["versionId"]);
            Assert.Equal(version.SnapshotJson,((System.Text.Json.JsonElement)view["snapshot"]).GetRawText());
            Assert.Equal("active",view["coverageState"]);
            Assert.Equal(version.EffectiveAt,view["effectiveCutoff"]);
            var unknown=await reader.ReadAtAsync(f.Servicing,policy.Id,version.EffectiveAt,version.ProcessedAt.AddTicks(-1));
            Assert.Equal("not-covered",unknown["coverageState"]);Assert.False(unknown.ContainsKey("snapshot"));
            Assert.Empty(await PolicyDiscoveryService.Rows(db,version.EffectiveAt,version.ProcessedAt.AddTicks(-1)).ToArrayAsync());
            Assert.Equal(version.Id,(await PolicyDiscoveryService.Rows(db,version.EffectiveAt,version.ProcessedAt).SingleAsync()).CurrentVersionId);
            Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>reader.ReadAtAsync(f.Servicing,policy.Id,version.EffectiveAt,version.ProcessedAt,Guid.NewGuid()))).Status);
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>reader.ReadAtAsync(f.Servicing with {AgencyId=policy.AgencyId},policy.Id,version.EffectiveAt,version.ProcessedAt))).Status);
        });
    }
}
