using System.Data;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlPolicyDiscoveryTestsSearchCurrentIssuedRegistrationsAndPreserveOwnedScope()
    {
        await WithDatabase(async (db, password) => {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            var receipt = await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new PolicyDiscoveryService().AuthorizeAsync(db, f.Servicing);
            var all = PolicyDiscoveryService.Rows(db, f.Clock.GetUtcNow());
            var row = await all.SingleAsync(); Assert.Equal(receipt.ResourceId, row.Id); Assert.Equal("active", row.State);
            Assert.Equal("scheduled", (await PolicyDiscoveryService.Rows(db, row.StartsAt.AddTicks(-1)).SingleAsync()).State);
            Assert.Equal("active", (await PolicyDiscoveryService.Rows(db, row.StartsAt).SingleAsync()).State);
            Assert.Equal("expired", (await PolicyDiscoveryService.Rows(db, row.EndsAt).SingleAsync()).State);
            var registration = await db.Set<PolicyRegistration>().Select(x => x.NormalizedRegistration).FirstAsync();
            Assert.Equal(row.Id, (await PolicyDiscoveryService.Search(db, all, registration).SingleAsync()).Id);
            Assert.Empty(await PolicyDiscoveryService.Search(db, all, "FOREIGN-SECRET").ToArrayAsync());
            Assert.Empty(await all.Where(x => x.ClientId == Guid.Empty).ToArrayAsync());
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => new PolicyDiscoveryService().AuthorizeAsync(db, f.Servicing with { AgencyId = row.AgencyId }))).Status);
            await tx.CommitAsync();
        });
    }
}
