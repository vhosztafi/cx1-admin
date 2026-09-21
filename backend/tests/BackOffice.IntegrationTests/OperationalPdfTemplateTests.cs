using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalPdfFutureTemplatesAreAdditiveAndLeaveIssuedRequestsUnchanged() => WithDatabase(async (db, password) =>
    {
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260921162248_OperationalFileObjects");
        await migrator.MigrateAsync();
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var requests = await db.Set<PolicyDocumentRequest>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var old = await db.Set<TemplateVersion>().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var productId = (await db.Set<Policy>().AsNoTracking().SingleAsync()).ProductId;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await DocumentTemplateSeed.SeedAsync(db); var count = await db.Set<TemplateVersion>().CountAsync();
            await DocumentTemplateSeed.SeedAsync(db); Assert.Equal(count, await db.Set<TemplateVersion>().CountAsync());
            var after = await db.Set<TemplateVersion>().AsNoTracking().Where(x => x.ProductId == productId && x.EffectiveFrom == DocumentTemplateSeed.EffectiveFrom && x.Kind.StartsWith("policy-")).ToArrayAsync();
            Assert.Equal(3, after.Length); Assert.All(after, x => Assert.Equal(2, x.Version));
            Assert.All(after, next => Assert.Contains(old, previous => previous.ProductId == productId && previous.Kind == next.Kind && previous.Version == 1 && previous.EffectiveFrom < next.EffectiveFrom));
            Assert.All(after, x => Assert.DoesNotContain("pending", x.ContentJson, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(15, await db.Set<TemplateVersion>().CountAsync(x => x.EffectiveFrom == DocumentTemplateSeed.EffectiveFrom));
            var baseline=await db.Set<TemplateVersion>().AsNoTracking().Where(x=>x.Code.StartsWith("demo-operational-start-")).ToArrayAsync();
            Assert.Equal(6,baseline.Length);
            Assert.All(baseline,x=>
            {
                Assert.Contains(x.Kind,new[]{"endorsement","cancellation-notice"});
                Assert.Equal(DocumentTemplateSeed.EffectiveFrom,x.EffectiveTo);
                Assert.True(x.EffectiveFrom<=f.Clock.GetUtcNow());
            });
            await transaction.CommitAsync();
        }
        foreach (var template in old) Assert.Equal(template.ContentJson, (await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x => x.Id == template.Id)).ContentJson);
        foreach (var request in requests)
        {
            var retained = await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x => x.Id == request.Id);
            Assert.Equal(request.TemplateVersionId, retained.TemplateVersionId); Assert.Equal(request.PayloadHash, retained.PayloadHash); Assert.Equal(request.PayloadJson, retained.PayloadJson);
        }
        var original = requests.Single(x => x.Kind == "policy-schedule");
        Assert.True((await new PolicyDocumentRenderService(f.Factory, new PolicyDocumentRenderer()).RenderRetainedRequest(f.Underwriter, original.Id)).PageCount > 1);
        var refused = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync("20260921162248_OperationalFileObjects"));
        Assert.Contains("downgrade would invalidate retained templates", refused.Message);
        Assert.Equal(15, await db.Set<TemplateVersion>().CountAsync(x => x.EffectiveFrom == DocumentTemplateSeed.EffectiveFrom));
        Assert.Contains("20260921180754_OperationalDocumentTemplates", await db.Database.GetAppliedMigrationsAsync());
    });
}
