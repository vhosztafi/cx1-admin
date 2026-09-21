using BackOffice.Application.Operations;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
            var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.QuoteId == f.QuoteId);
            var quoteRenderer = new PolicyDocumentRenderService(f.Factory, new PolicyDocumentRenderer());
            var quotation = await quoteRenderer.RenderQuoteTerms(f.Underwriter, f.QuoteId, terms.Id, "quotation");
            Assert.True(quotation.PageCount > 1);
            SaveQuotationArtifact("real-sql-motor-quotation", quotation, terms);
            Assert.Equal(terms.TermsJson, (await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == terms.Id)).TermsJson);
            await Assert.ThrowsAsync<OperationalAccessException>(() => quoteRenderer.RenderQuoteTerms(f.Underwriter, Guid.NewGuid(), terms.Id, "quotation"));
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
            await Assert.ThrowsAsync<OperationalAccessException>(() => quoteRenderer.RenderQuoteTerms(f.Underwriter, f.QuoteId, terms.Id, "quotation"));
        });
    }

    [Fact]
    public Task RealSqlOperationalPdfCommercialQuotationRetainsProductTerms() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var factory = new RatingFactory(new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(db.Database.GetConnectionString(), x => x.UseCompatibilityLevel(160)).Options);
        var user = await db.Set<StaffUser>().AsNoTracking().SingleAsync(x => x.Id == actorId);
        var actor = new ActorContext(user.Id, user.TeamId, null, new HashSet<string> { "senior-underwriter" });
        var terms = await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.CycleId == cycle.Id);
        var service = new PolicyDocumentRenderService(factory, new PolicyDocumentRenderer());
        var pdf = await service.RenderQuoteTerms(actor, cycle.QuoteId, terms.Id, "quotation");
        Assert.True(pdf.PageCount > 2); SaveQuotationArtifact("real-sql-commercial-quotation", pdf, terms);
        Assert.Equal(terms.TermsHash, (await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x => x.Id == terms.Id)).TermsHash);
        var statement = await service.RenderQuoteTerms(actor, cycle.QuoteId, terms.Id, "statement-of-fact"); Assert.True(statement.PageCount > 2);
        var issue = await CommercialIssueCommand(db, cycle, acceptance, actorId);
        await issue.Service.IssueAsync(issue.Actor, issue.Quote.Id, issue.Quote.RowVersion, issue.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var request = await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x => x.Kind == "policy-schedule");
        Assert.True((await service.RenderRetainedRequest(actor, request.Id)).PageCount > 2);
    }, stopAfterAccepted: true);

    private static void SaveQuotationArtifact(string name, RenderedPolicyDocument pdf, QuoteTermsVersion terms)
    {
        var directory = Path.GetFullPath(Path.Combine(".local", "phase9-06-pdfs")); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".pdf"), pdf.Bytes);
        using var payload = JsonDocument.Parse(terms.TermsJson); var root = payload.RootElement;
        File.WriteAllText(Path.Combine(directory, name + ".expected.json"), JsonSerializer.Serialize(new {
            insuredName = root.GetProperty("insuredName").GetString(), agencyName = root.GetProperty("agencyName").GetString(),
            grossPayable = root.GetProperty("price").GetProperty("grossPayable").GetString(), termsId = terms.Id, termsHash = terms.TermsHash,
            conditions = root.GetProperty("conditions").EnumerateArray().Select(x => x.GetProperty("wording").GetString()).ToArray()
        }));
    }
}
