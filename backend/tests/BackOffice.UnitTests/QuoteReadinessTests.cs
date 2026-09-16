using System.Text.Json;
using BackOffice.Application.Quotes;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class QuoteReadinessTests
{
    [Fact]
    public void IncompleteCaptureReportsTermAndStructureWithoutClaimingReadiness()
    {
        using var proposal = JsonDocument.Parse("{\"schemaVersion\":\"1.0\",\"productCode\":\"motor-trade-road-risks\"}");
        var quote = Guid.NewGuid(); var revision = Guid.NewGuid();
        var result = QuoteReadiness.Assess(quote, revision, proposal.RootElement, QuoteTerm.Assess(), "agency-unavailable");
        Assert.Equal(quote, result.QuoteId); Assert.Equal(revision, result.RevisionId); Assert.False(result.Ready);
        Assert.Contains(result.Issues, x => x.Code == "required-term-field" && x.Path == "/termIntent/localStartDate");
        Assert.Contains(result.Issues, x => x.Code == "schema-required");
        Assert.Contains(result.Issues, x => x.Code == "agency-unavailable" && x.Category == "eligibility");
        Assert.Contains(result.Issues, x => x.Code == "quote-assessment-unavailable" && x.Severity == "error");
    }

    [Fact]
    public void AllSixCompleteSourceFixturesStillRequireUnimplementedSemanticAndEvidenceGates()
    {
        var names = typeof(QuoteReadinessTests).Assembly.GetManifestResourceNames().Where(x => x.StartsWith("QuoteExamples.quote-capture-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, names.Length);
        foreach (var name in names)
        {
            using var stream = typeof(QuoteReadinessTests).Assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            var proposal = document.RootElement.GetProperty("proposal");
            Assert.Empty(QuoteCaptureShape.ValidateCompleteness(proposal));
            var term = QuoteTerm.Assess(proposal.GetProperty("termIntent")); Assert.Empty(term.Issues);
            var result = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), proposal, term, null);
            Assert.False(result.Ready);
            Assert.Equal("quote-assessment-unavailable", Assert.Single(result.Issues).Code);
        }
    }

    [Fact]
    public void BoundedResultsRetainTheProgressionBlocker()
    {
        using var proposal = JsonDocument.Parse("{}");
        var term = new QuoteTermAssessment(null, Enumerable.Range(0, 150).Select(x => new QuoteFieldIssue("required-term-field", "/termIntent/" + x)).ToArray());
        var result = QuoteReadiness.Assess(Guid.NewGuid(), Guid.NewGuid(), proposal.RootElement, term, null);
        Assert.False(result.Ready); Assert.Equal(100, result.Issues.Count);
        Assert.Equal("quote-assessment-unavailable", result.Issues[0].Code);
    }
}
