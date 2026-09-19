using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-combined")]
    [InlineData("motor-trade-road-risks")]
    public async Task RealSqlServicingDemoDraftsAreAdditiveAndNeverReactivateEditedRecords(string product)
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password, product); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var issued = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var seed = new ServicingDemoSeed(f.Factory, f.Clock);
            var first = await seed.SeedDraftsAsync(f.Underwriter, issued.PolicyId);
            Assert.Equal(new[] { "editable", "leased" }, first.Select(x => x.Scenario));
            var drafts = new ServicingDraftService(f.Factory, f.Clock);
            var leased = first.Single(x => x.Scenario == "leased");
            var read = await drafts.ReadAsync(f.Underwriter, leased.DraftId);
            using var body = JsonDocument.Parse(read.Body);
            var lease = body.RootElement.GetProperty("lease").GetProperty("leaseToken").GetGuid();
            await drafts.AbandonAsync(f.Underwriter, leased.DraftId, Convert.FromBase64String(read.Etag.Trim('"')), lease,
                "Business user finished this fictional demonstration", Guid.NewGuid().ToString(), Guid.NewGuid());
            var before = await drafts.ReadAsync(f.Underwriter, leased.DraftId);
            var again = await seed.SeedDraftsAsync(f.Underwriter, issued.PolicyId);
            Assert.Equal(first, again);
            var after = await drafts.ReadAsync(f.Underwriter, leased.DraftId);
            Assert.Equal(before, after);
            Assert.Equal(2, await db.Set<ServicingDraft>().CountAsync());
            Assert.Equal(issued.SnapshotJson, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => seed.SeedDraftsAsync(f.Servicing with { Roles = new HashSet<string>() }, issued.PolicyId))).Status);
        });
    }
}
