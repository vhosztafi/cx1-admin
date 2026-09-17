using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCapacityReadDoesNotUpgradeAgencyLockWhileProviderHoldsUpdateIntent()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, escalation, _) = await CapacityRequest(db, password, "query-proof");
            var agencyId = await db.Set<Quote>().Where(x => x.Id == f.QuoteId).Select(x => x.AgencyId).SingleAsync();
            await using var writer = f.Factory.CreateDbContext();
            await using var transaction = await writer.Database.BeginTransactionAsync();
            _ = await writer.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK,ROWLOCK) WHERE Id={agencyId}").AsNoTracking().SingleAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await new CapacityReadModel(f.Factory, f.Clock).GetAsync(f.Underwriter, escalation.Id, timeout.Token);
            Assert.Equal(escalation.Id, result["id"]);
            Assert.Equal("queued", result["state"]);
            await transaction.RollbackAsync();
        });
    }
}
