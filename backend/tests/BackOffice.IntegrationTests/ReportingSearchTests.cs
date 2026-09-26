using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlReportingSearchScopesCountsRegistrationsFiltersAndRevocation()
    {
        await WithDatabase(async (db, password) => {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            var receipt = await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var service = new SearchService(f.Factory, f.Clock);
            var all = await service.SearchAsync(f.Servicing, new());
            Assert.NotEmpty(all.Items); Assert.DoesNotContain("agency", all.AvailableKinds); Assert.DoesNotContain("match", all.AvailableKinds);
            var policy = (await service.SearchAsync(f.Servicing, new(Kind: "policy"))).Items.Single(); Assert.Equal(receipt.ResourceId, policy.Id);
            var registration = await db.Set<PolicyRegistration>().Select(x => x.NormalizedRegistration).FirstAsync();
            Assert.Contains((await service.SearchAsync(f.Servicing, new(Q: registration, Kind: "policy"))).Items, x => x.Id == policy.Id);
            Assert.Empty((await service.SearchAsync(f.Servicing, new(Kind: "policy", AgencyId: Guid.NewGuid()))).Items);
            Assert.Equal(400, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SearchAsync(f.Servicing, new(Kind: "client", ProductCode: "commercial-combined")))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SearchAsync(f.Servicing, new(Kind: "agency")))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SearchAsync(f.Servicing with { AgencyId = Guid.NewGuid() }, new()))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SearchAsync(f.Servicing, new(Version: "old")))).Status);
            var clients = Enumerable.Range(0, 30).Select(i => new ClientAccount { Reference = $"SRCH{i:000}", LegalName = $"Search pagination {i:000}", NormalizedName = $"SEARCH PAGINATION {i:000}", EntityType = "limited-company", IdentityState = "active", Address = "{}" }).ToArray();
            db.AddRange(clients); await db.SaveChangesAsync();
            var first = await service.SearchAsync(f.Servicing, new(Q: "Search pagination", Kind: "client"));
            Assert.Equal(30, first.Total); Assert.Equal(25, first.Items.Length);
            var next = await service.SearchAsync(f.Servicing, new(Q: "Search pagination", Kind: "client", Offset: first.NextOffset!.Value, Version: first.Version));
            Assert.Equal(5, next.Items.Length); Assert.Empty(first.Items.Select(x => x.Id).Intersect(next.Items.Select(x => x.Id)));
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Servicing.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.SearchAsync(f.Servicing, new()))).Status);
        });
    }
}
