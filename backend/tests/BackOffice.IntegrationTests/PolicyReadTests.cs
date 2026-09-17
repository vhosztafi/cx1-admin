using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlPolicyReadTestsKeepIssuedBytesAndDenyForeignChildrenAndAgencyIdentities()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            var issued = await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(); var version = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(); var service = new PolicyReadService(f.Factory);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClientAccount SET LegalName=N'Changed after issue' WHERE Id={policy.ClientId}");
            var view = await service.ReadAsync(f.Servicing, policy.Id); Assert.Equal(version.SnapshotJson, ((JsonElement)view["snapshot"]).GetRawText());
            foreach (var actor in new[] { f.Servicing with { AgencyId = policy.AgencyId }, f.Underwriter with { Roles = new HashSet<string> { "system-admin" } } })
                Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadAsync(actor, issued.ResourceId))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadAsync(f.Servicing, policy.Id, termId: Guid.NewGuid()))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadAsync(f.Servicing, policy.Id, versionId: Guid.NewGuid()))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadAsync(f.Servicing, policy.Id, transactionId: Guid.NewGuid()))).Status);
            Assert.Equal(404, (await Assert.ThrowsAsync<QuoteOperationException>(() => service.ReadAsync(f.Servicing, policy.Id, obligationId: Guid.NewGuid()))).Status);
        });
    }
}
