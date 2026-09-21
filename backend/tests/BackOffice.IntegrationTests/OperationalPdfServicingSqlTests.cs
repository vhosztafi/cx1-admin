using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public Task RealSqlOperationalPdfRenewalUsesExactPreparedTerms(string product) => VerifyRenewalLifecycle(product, false, false, async (db, f, cycle, terms) =>
    {
        var service = new PolicyDocumentRenderService(f.Factory, new PolicyDocumentRenderer());
        var original = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var pdf = await service.RenderServicingTerms(f.Underwriter, cycle.DraftId, terms.Id, "renewal-invitation");
        Assert.True(pdf.PageCount > 2);
        SaveServicingPdfArtifact("real-sql-" + product + "-renewal", pdf, terms);
        Assert.Equal(terms.TermsJson, (await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == terms.Id)).TermsJson);
        Assert.Equal(original.ContentHash, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).ContentHash);
        Assert.Equal(1, await db.Set<PolicyTerm>().CountAsync());
        await Assert.ThrowsAsync<OperationalAccessException>(() => service.RenderServicingTerms(f.Underwriter, cycle.DraftId, Guid.NewGuid(), "renewal-invitation"));
        var user = await db.Set<StaffUser>().SingleAsync(x => x.Id == f.Underwriter.UserId); user.State = "suspended"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalAccessException>(() => service.RenderServicingTerms(f.Underwriter, cycle.DraftId, terms.Id, "renewal-invitation"));
    });

    [Fact]
    public Task RealSqlOperationalPdfAdjustmentPreservesEveryEffectiveSlice() => RunServicingRatingRequests("motor-trade-road-risks", "terms-prepare", onPrepared: async (db, f, cycle, terms) =>
    {
        var pdf = await new PolicyDocumentRenderService(f.Factory, new PolicyDocumentRenderer()).RenderServicingTerms(f.Underwriter, cycle.DraftId, terms.Id, "quotation");
        Assert.True(pdf.PageCount > 3);
        using var payload = JsonDocument.Parse(terms.TermsJson); Assert.Equal(2, payload.RootElement.GetProperty("slices").GetArrayLength());
        SaveServicingPdfArtifact("real-sql-motor-adjustment", pdf, terms);
    });

    [Fact]
    public Task RealSqlOperationalPdfCommercialRenewalPreservesProposedRiskAndTerm() => CommercialRenewalScenario(true, onAccepted: async (db, factory, actor, cycle, terms) =>
    {
        var original = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var pdf = await new PolicyDocumentRenderService(factory, new PolicyDocumentRenderer()).RenderServicingTerms(actor, cycle.DraftId, terms.Id, "renewal-invitation");
        Assert.True(pdf.PageCount > 3); SaveServicingPdfArtifact("real-sql-commercial-renewal", pdf, terms);
        Assert.Equal(original.ContentHash, (await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).ContentHash);
        Assert.Equal(1, await db.Set<PolicyTerm>().CountAsync());
    });

    private static void SaveServicingPdfArtifact(string name, RenderedPolicyDocument pdf, ServicingTermsVersion terms)
    {
        var directory = Path.GetFullPath(Path.Combine(".local", "phase9-06-pdfs")); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf.Bytes);
        using var payload = JsonDocument.Parse(terms.TermsJson); var root = payload.RootElement;
        File.WriteAllText(Path.Combine(directory, name + ".expected.json"), JsonSerializer.Serialize(new {
            grossPayable = root.GetProperty("price").GetProperty("grossPayable").GetString(), termsId = terms.Id, termsHash = terms.TermsHash,
            dates = root.GetProperty("effectiveDates").EnumerateArray().Select(x => x.GetDateTimeOffset().ToString("yyyy-MM-dd")).ToArray()
        }));
    }
}
