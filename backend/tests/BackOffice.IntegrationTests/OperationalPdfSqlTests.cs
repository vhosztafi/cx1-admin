using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlOperationalPdfRetainedRequestUsesExactIssuedSourceAndCurrentIdentity()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var request = await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x => x.Kind == "policy-schedule");
            var source = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var template = await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x => x.Id == request.TemplateVersionId);
            var renderer = new PolicyDocumentRenderService(f.Factory, new PolicyDocumentRenderer());
            var pdf = await renderer.RenderRetainedRequest(f.Underwriter, request.Id);
            Assert.True(pdf.Bytes.Length > 5000); Assert.True(pdf.PageCount > 1);
            Assert.Equal(source.SnapshotJson, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).SnapshotJson);
            var retained = await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x => x.Id == request.Id);
            Assert.Equal(request.PayloadJson, retained.PayloadJson); Assert.Equal(request.State, retained.State);
            Assert.Equal(template.ContentJson, (await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x => x.Id == template.Id)).ContentJson);
            await Assert.ThrowsAsync<OperationalAccessException>(() => renderer.RenderRetainedRequest(f.Underwriter, Guid.NewGuid()));
            var user = await db.Set<StaffUser>().SingleAsync(x => x.Id == f.Underwriter.UserId); user.State = "suspended"; await db.SaveChangesAsync();
            await Assert.ThrowsAsync<OperationalAccessException>(() => renderer.RenderRetainedRequest(f.Underwriter, request.Id));
        });
    }
}
