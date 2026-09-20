using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class CommercialExposureConflictException(CommercialExposureAssessment assessment, Guid bookId, DateTimeOffset assessedAt, decimal authorityLimit) : Exception("The commercial district capacity assessment did not permit issue.")
{
    public CommercialExposureAssessment Assessment { get; } = assessment;
    public Guid BookId { get; } = bookId;
    public DateTimeOffset AssessedAt { get; } = assessedAt;
    public decimal AuthorityLimit { get; } = authorityLimit;
}

internal sealed record CommercialIssuePlan(Guid PolicyId, Guid TermId, Guid VersionId, Guid BookId,
    DateTimeOffset AssessedAt, CommercialExposureAssessment Assessment, EffectiveUnderwritingGrant Grant, decimal ActorDistrictLimit, decimal BinderDistrictLimit);

public static class CommercialExposureService
{
    internal static async Task<CommercialIssuePlan> AssessIssue(BackOfficeDbContext db, UnderwritingDecisionContext held,
        DateTimeOffset now, CancellationToken token)
    {
        await CommercialExposureLock.RequireAsync(db, token);
        if (!held.Input.IsCommercial) throw new InvalidOperationException("Commercial issue assessment requires the typed commercial input.");
        var book = await db.Set<CommercialExposureBinder>().AsNoTracking().Where(x => x.BinderVersionId == held.Cycle.BinderVersionId)
            .Select(x => (Guid?)x.BookId).SingleOrDefaultAsync(token) ?? throw new QuoteOperationException(409, "commercial-exposure-book-unavailable");
        var policyId = Guid.NewGuid(); var termId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
        var proposed = new CommercialExposureSlice(book, policyId, termId, versionId, held.Cycle.StartsAt, held.Cycle.EndsAt,
            held.Cycle.StartsAt, now, 1, 1, "new-business", CommercialExposureProjection.Locations(proposal.RootElement));
        var existing = await CommercialExposureProjection.ReadAsync(db, book, now, token);
        var limits = await CommercialExposureProjection.LimitsAsync(db, book, now, token);
        var assessment = CommercialExposureRules.Assess(existing, [proposed], limits, book, policyId, held.Cycle.StartsAt, held.Cycle.EndsAt, now);
        decimal DistrictLimit(JsonElement definition) => decimal.Parse(definition.GetProperty("limits").GetProperty("districtProperty").GetString()!, CultureInfo.InvariantCulture);
        var binderLimit = DistrictLimit(held.Eligible.Binder);
        // Select one current grant that covers every dimension, including the
        // aggregate. A higher book publication cannot broaden an older binder.
        var conditions = (await UnderwritingEvidenceService.ActiveConditions(db, held.Cycle.Id, token)).Select(x => QuoteReferralService.Parse(x.DefinitionJson, proposal.RootElement)).ToArray();
        EffectiveUnderwritingGrant? selected = null;
        foreach (var candidate in held.Grants.OrderByDescending(x => DistrictLimit(x.Definition)).ThenBy(x => x.Version.Id))
            if (await CapacityAuthority.Allows(db, held, candidate.Definition, conditions, now, token)) { selected = candidate; break; }
        if (selected is null) throw new QuoteOperationException(403, "policy-issue-authority-required");
        var actorLimit = DistrictLimit(selected.Definition);
        assessment = CommercialExposureRules.WithinAuthority(assessment, actorLimit, binderLimit);
        if (!assessment.Allowed) throw new CommercialExposureConflictException(assessment, book, now, Math.Min(actorLimit, binderLimit));
        return new(policyId, termId, versionId, book, now, assessment, selected, actorLimit, binderLimit);
    }

    internal static async Task<Guid> Record(BackOfficeDbContext db, CommercialExposureVersion header, CommercialIssuePlan plan,
        Guid actorId, CancellationToken token)
    {
        await CommercialExposureLock.RequireAsync(db, token);
        if (!plan.Assessment.Allowed || header.BookId != plan.BookId || header.PolicyId != plan.PolicyId ||
            header.VersionId != plan.VersionId || header.TermId != plan.TermId || header.ProcessedAt != plan.AssessedAt)
            throw new InvalidOperationException("Commercial issue must retain its exact assessed source.");
        var json = JsonSerializer.Serialize(new { format = "commercial-exposure-decision-1", bookId = plan.BookId,
            policyId = header.PolicyId, versionId = header.VersionId, sourceHash = Convert.ToHexStringLower(header.SourceHash),
            assessedAt = plan.AssessedAt, from = header.TermStartsAt, to = header.TermEndsAt, authorityGrantId = plan.Grant.Grant.Id,
            authorityVersionId = plan.Grant.Version.Id, actorDistrictLimit = plan.ActorDistrictLimit, binderDistrictLimit = plan.BinderDistrictLimit,
            intervals = plan.Assessment.Intervals }, QuoteRatingService.Json);
        var decision = new CommercialExposureIssueDecision { ExposureVersionId = header.Id, AssessedAt = plan.AssessedAt,
            DecisionJson = json, DecisionHash = SHA256.HashData(Encoding.UTF8.GetBytes(json)), CreatedBy = actorId, CreatedAt = plan.AssessedAt };
        db.Add(decision); await db.SaveChangesAsync(token); return decision.Id;
    }
}
