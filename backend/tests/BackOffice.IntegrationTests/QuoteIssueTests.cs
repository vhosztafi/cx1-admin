using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlQuoteIssueRechecksExactCarrierExtensionBeforeCurrentPriceOrReceipt()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, submission) = await CapacityRequest(db, password, "approve-stock-150000");
            var lease = (await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync(CapacityService.WorkKind, submission.WorkId))!;
            var worker = new CapacityWorker(f.Factory, f.Clock); Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
            var input = new QuoteIssueInput(f.CycleId, cycle.CurrentRatingId!.Value, Guid.NewGuid(), new string('a',64), new string('a',64), "Check exact carrier extension at the issue boundary");
            var service = new QuoteIssueService(f.Factory, f.Clock); var version = await TermsVersion(db, f);
            // Valid stock extension passes authority; this deliberately unaccepted
            // fixture then stops at the separate quote-state gate.
            var current = await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, f.QuoteId, version, input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Equal("quote-issue-state", current.Code);
            f.Clock.Current = cycle.EndsAt.AddSeconds(1);
            var expired = await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, f.QuoteId, version, input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Equal("policy-issue-authority-required", expired.Code); Assert.Empty(await db.Set<Policy>().ToArrayAsync());
        });
    }

    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public async Task RealSqlQuoteIssuePreservesBothProductDeclarationsAndOriginalRiskIdentities(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
            var cycle = await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x => x.Id == f.CycleId);
            var proposal = await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x => x.Id == cycle.QuoteRevisionId);
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            using var source = JsonDocument.Parse(proposal.ProposalJson); using var issued = JsonDocument.Parse(version.SnapshotJson);
            Assert.Equal(product, issued.RootElement.GetProperty("productCode").GetString());
            foreach (var field in source.RootElement.GetProperty("risk").EnumerateObject())
                Assert.True(JsonElement.DeepEquals(field.Value, issued.RootElement.GetProperty("risk").GetProperty(field.Name)), field.Name);
            foreach (var field in source.RootElement.GetProperty("cover").EnumerateObject())
                Assert.True(JsonElement.DeepEquals(field.Value, issued.RootElement.GetProperty("cover").GetProperty(field.Name)), field.Name);
            Assert.Equal(source.RootElement.GetProperty("risk").GetProperty("vehicles").GetArrayLength(), await db.Set<PolicyRegistration>().CountAsync());
            Assert.True(BackOffice.Application.Policies.PolicySnapshotShape.Valid(issued.RootElement));
            var path = Path.GetFullPath(Path.Combine(".local", "phase6-11-issued-" + product + ".json")); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, version.SnapshotJson);
        });
    }

    [Theory]
    [InlineData("quote-version", 412)]
    [InlineData("identity", 409)]
    [InlineData("authority", 403)]
    public async Task RealSqlQuoteIssueWaitsForHeldScopeAndObservesCommittedChanges(string change, int status)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.QuoteId);
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Agency WITH(UPDLOCK,HOLDLOCK) SET UpdatedAt=UpdatedAt WHERE Id={quote.AgencyId}");
            if (change == "quote-version") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quote SET UpdatedAt=DATEADD(second,1,UpdatedAt) WHERE Id={quote.Id}");
            if (change == "identity") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAccount SET IdentityState=N'inactive' WHERE Id={quote.ClientId}");
            if (change == "authority") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Current issue authority revoked' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            var issue = new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            await Task.Delay(200); Assert.False(issue.IsCompleted); await tx.CommitAsync();
            Assert.Equal(status, (await Assert.ThrowsAsync<QuoteOperationException>(() => issue)).Status);
            Assert.Empty(await db.Set<Policy>().ToArrayAsync());
        });
    }

    private sealed record IssueFixture(DecisionFixture Source, QuoteIssueInput Input, byte[] Version);
    private static async Task<IssueFixture> AcceptedIssue(BackOfficeDbContext db, string password, string product = "motor-trade-road-risks")
    {
        var setup = await SignedTerms(db, password, product: product); var f = setup.Fixture;
        var sent = await new QuoteTermsService(f.Factory, f.Clock).SendAsync(f.Servicing, f.QuoteId, setup.TermsId, [setup.ContactId], await TermsVersion(db, f), Guid.NewGuid().ToString(), Guid.NewGuid());
        var delivery = await db.Set<QuoteTermsDelivery>().AsNoTracking().SingleAsync(x => x.Id == sent.ResourceId);
        var lease = (await new SqlJobLeases(f.Factory, f.Clock).ClaimWorkAsync(QuoteTermsService.WorkKind, delivery.WorkId))!;
        var worker = new QuoteDeliveryWorker(f.Factory, f.Clock); Assert.True(await worker.ApplyAsync(lease, await worker.ExecuteProviderAsync(lease)));
        var proof = await TermsProof(db, f, (await new UnderwritingEvidenceService(f.Factory, f.Clock).RequirementsAsync(f.Servicing, f.QuoteId)).Single(x => x.Code == "acceptance-proof"));
        var view = await new QuoteUnderwritingReadModel(f.Factory, f.Clock).AssessmentAsync(f.Servicing, f.QuoteId);
        var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == setup.TermsId);
        var accepted = await new QuoteAcceptanceService(f.Factory, f.Clock).RecordAsync(f.Servicing, f.QuoteId, await TermsVersion(db, f),
            new(f.CycleId, setup.RatingId, terms.Id, terms.TermsHash, (string)view["assuranceHash"], "Fictional issue customer", f.Clock.Current, "written", proof), Guid.NewGuid().ToString(), Guid.NewGuid());
        return new(f, new(f.CycleId, setup.RatingId, accepted.ResourceId, terms.TermsHash, (string)view["assuranceHash"], "Issue accepted fictional cover"), await TermsVersion(db, f));
    }

    [Fact]
    public async Task RealSqlQuoteIssueDifferentKeysAreAtomicAndReplayRequiresCurrentAuthority()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source; var service = new QuoteIssueService(f.Factory, f.Clock);
            var keys = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
            async Task<(int Index, CommandOutcome? Result)> Issue(int i)
            {
                try { return (i, await service.IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, keys[i], Guid.NewGuid())); }
                catch (QuoteOperationException e) { Assert.Contains(e.Status, new[] { 409, 412 }); return (i, null); }
            }
            var results = await Task.WhenAll(Issue(0), Issue(1)); var winner = Assert.Single(results, x => x.Result is not null);
            var result = winner.Result!; var replay = await service.IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, keys[winner.Index], Guid.NewGuid());
            Assert.True(replay.Replayed); Assert.Equal(result.Body, replay.Body);
            var policy = Assert.Single(await db.Set<Policy>().AsNoTracking().ToArrayAsync()); Assert.Equal(result.ResourceId, policy.Id);
            Assert.Single(await db.Set<PolicyTerm>().ToArrayAsync()); Assert.Single(await db.Set<PolicyVersion>().ToArrayAsync()); Assert.Single(await db.Set<PolicyTransaction>().ToArrayAsync());
            Assert.Single(await db.Set<IssueFinancialObligation>().ToArrayAsync()); Assert.Equal(5, await db.Set<IssueFinancialComponent>().CountAsync());
            Assert.Equal(0m, await db.Set<JournalLine>().SumAsync(x => x.Debit - x.Credit)); Assert.NotNull((await db.Set<Journal>().SingleAsync()).PostedAt);
            Assert.Equal(3, await db.Set<PolicyDocumentRequest>().CountAsync()); Assert.Equal(3, await db.Set<OutboxWork>().CountAsync(x => x.Kind == "policy-document"));
            Assert.Equal(policy.Id, await db.Set<Quote>().Where(x => x.Id == f.QuoteId).Select(x => x.BoundPolicyId).SingleAsync());
            var read = await new PolicyReadService(f.Factory).ReadAsync(f.Servicing, policy.Id);
            Assert.Equal(policy.Id, read["id"]);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason=N'Current issue authority revoked' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
            var denied = await Assert.ThrowsAsync<QuoteOperationException>(() => service.IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, keys[winner.Index], Guid.NewGuid()));
            Assert.Equal(403, denied.Status); Assert.Single(await db.Set<Policy>().ToArrayAsync());
        });
    }

    [Fact]
    public async Task RealSqlQuoteIssueLateFailureRollsBackPostingDocumentsAndQuote()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_TestIssueAuditFailure ON AuditEvent AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE EventType='policy.issued') THROW 51299,'Deliberate late issue failure.',1; END;");
            await Assert.ThrowsAsync<DbUpdateException>(() => new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Empty(await db.Set<Policy>().ToArrayAsync()); Assert.Empty(await db.Set<PolicyVersion>().ToArrayAsync()); Assert.Empty(await db.Set<PolicyTransaction>().ToArrayAsync());
            Assert.Empty(await db.Set<IssueFinancialObligation>().ToArrayAsync()); Assert.Empty(await db.Set<Journal>().ToArrayAsync()); Assert.Empty(await db.Set<JournalLine>().ToArrayAsync());
            Assert.Empty(await db.Set<PolicyDocumentRequest>().ToArrayAsync()); Assert.False(await db.Set<OutboxWork>().AnyAsync(x => x.Kind == "policy-document"));
            Assert.Equal("accepted", await db.Set<Quote>().Where(x => x.Id == f.QuoteId).Select(x => x.State).SingleAsync());
            Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Route.EndsWith("/issue")));
            Assert.False(await db.Set<ClientActivity>().AnyAsync(x => x.EventType == "policy.issued"));
        });
    }

    [Theory]
    [InlineData("servicing")]
    [InlineData("expired")]
    [InlineData("stale-assurance")]
    [InlineData("foreign-acceptance")]
    [InlineData("withdrawn-proof")]
    [InlineData("admin")]
    public async Task RealSqlQuoteIssueRejectsUnqualifiedCurrentContext(string variant)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source; var input = setup.Input;
            if (variant == "expired") f.Clock.Current = f.Clock.Current.AddDays(31);
            if (variant == "stale-assurance") input = input with { AssuranceHash = new string('b', 64) };
            if (variant == "foreign-acceptance") input = input with { AcceptanceId = Guid.NewGuid() };
            if (variant == "withdrawn-proof")
            {
                var acceptance = await db.Set<QuoteAcceptance>().SingleAsync(x => x.Id == input.AcceptanceId);
                var proof = await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().SingleAsync(x => x.Id == acceptance.EvidenceAssociationId);
                await new UnderwritingEvidenceService(f.Factory, f.Clock).WithdrawAsync(f.Servicing, f.QuoteId, f.CycleId, proof.Id, setup.Version, proof.RowVersion, "Withdraw accepted proof", Guid.NewGuid().ToString(), Guid.NewGuid());
                setup = setup with { Version = await TermsVersion(db, f) };
            }
            var actor = variant == "servicing" ? f.Servicing : variant == "admin" ? f.Underwriter with { Roles = new HashSet<string> { "system-admin" } } : f.Underwriter;
            await Assert.ThrowsAsync<QuoteOperationException>(() => new QuoteIssueService(f.Factory, f.Clock).IssueAsync(actor, f.QuoteId, setup.Version, input, Guid.NewGuid().ToString(), Guid.NewGuid()));
            Assert.Empty(await db.Set<Policy>().ToArrayAsync()); Assert.Empty(await db.Set<Journal>().ToArrayAsync());
        });
    }
}
