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

internal sealed record CommercialServicingIssuePlan(Guid BookId, DateTimeOffset AssessedAt,
    IReadOnlyList<CommercialExposureSlice> Proposed, CommercialExposureAssessment Assessment,
    EffectiveUnderwritingGrant Grant, decimal ActorDistrictLimit, decimal BinderDistrictLimit);

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

    internal static async Task<CommercialServicingIssuePlan> AssessServicing(BackOfficeDbContext db, ServicingDecisionContext held,
        EffectiveUnderwritingGrant grant, DateTimeOffset now, CancellationToken token)
    {
        await CommercialExposureLock.RequireAsync(db,token);
        if(!held.Input.IsCommercial || held.Scope.Draft.Kind!="adjustment") throw new InvalidOperationException("Commercial adjustment input required.");
        var book=await db.Set<CommercialExposureBinder>().AsNoTracking().Where(x=>x.BinderVersionId==held.Cycle.BinderVersionId)
            .Select(x=>(Guid?)x.BookId).SingleOrDefaultAsync(token)??throw new QuoteOperationException(409,"commercial-exposure-book-unavailable");
        if(!await db.Set<CommercialExposureVersion>().AnyAsync(x=>x.VersionId==held.Cycle.BaseVersionId && x.BookId==book,token))
            throw new QuoteOperationException(409,"commercial-servicing-exposure-base-unavailable");
        var sequence=checked((await db.Set<PolicyTransaction>().Where(x=>x.TermId==held.Cycle.BaseTermId).MaxAsync(x=>(int?)x.Sequence,token)??0)+1);
        var slices=ServicingEvidenceProjection.Slices(held);
        var proposed=slices.Select((x,index)=>new CommercialExposureSlice(book,held.Cycle.PolicyId,held.Cycle.BaseTermId,Guid.NewGuid(),
            held.Input.Term.StartsAt,held.Input.Term.EndsAt,x.EffectiveAt,now,sequence,index+1,"adjustment",CommercialExposureProjection.Locations(x.Proposal))).ToArray();
        var existing=await CommercialExposureProjection.ReadAsync(db,book,now,token);
        var limits=await CommercialExposureProjection.LimitsAsync(db,book,now,token);
        var assessment=CommercialExposureRules.Assess(existing,proposed,limits,book,held.Cycle.PolicyId,proposed[0].EffectiveAt,held.Input.Term.EndsAt,now);
        decimal District(JsonElement definition)=>decimal.Parse(definition.GetProperty("limits").GetProperty("districtProperty").GetString()!,CultureInfo.InvariantCulture);
        var actorLimit=District(grant.Definition);var binderLimit=District(held.Scope.Eligible.Binder);
        assessment=CommercialExposureRules.WithinAuthority(assessment,actorLimit,binderLimit);
        if(!assessment.Allowed)throw new CommercialExposureConflictException(assessment,book,now,Math.Min(actorLimit,binderLimit));
        return new(book,now,proposed,assessment,grant,actorLimit,binderLimit);
    }

    internal static async Task RecordServicing(BackOfficeDbContext db, IReadOnlyList<CommercialExposureVersion> headers,
        CommercialServicingIssuePlan plan, Guid actorId, CancellationToken token)
    {
        await CommercialExposureLock.RequireAsync(db,token);
        if(!plan.Assessment.Allowed || headers.Count!=plan.Proposed.Count || headers.Select(x=>x.TransactionId).Distinct().Count()!=1)
            throw new InvalidOperationException("Commercial adjustment must retain the complete assessed graph.");
        foreach(var header in headers)
        {
            var proposed=plan.Proposed.Single(x=>x.VersionId==header.VersionId);
            if(header.BookId!=plan.BookId || header.PolicyId!=proposed.PolicyId || header.TermId!=proposed.TermId || header.ProcessedAt!=plan.AssessedAt ||
                header.EffectiveAt!=proposed.EffectiveAt || header.TransactionSequence!=proposed.TransactionSequence || header.SliceOrdinal!=proposed.SliceOrdinal)
                throw new InvalidOperationException("Commercial adjustment exposure differs from its held assessment.");
            var json=JsonSerializer.Serialize(new {format="commercial-servicing-exposure-decision-1",bookId=plan.BookId,
                policyId=header.PolicyId,versionId=header.VersionId,sourceHash=Convert.ToHexStringLower(header.SourceHash),transactionId=header.TransactionId,
                assessedAt=plan.AssessedAt,from=plan.Proposed[0].EffectiveAt,to=header.TermEndsAt,authorityGrantId=plan.Grant.Grant.Id,
                authorityVersionId=plan.Grant.Version.Id,actorDistrictLimit=plan.ActorDistrictLimit,binderDistrictLimit=plan.BinderDistrictLimit,
                versions=headers.OrderBy(x=>x.SliceOrdinal).Select(x=>new {versionId=x.VersionId,sourceHash=Convert.ToHexStringLower(x.SourceHash),x.EffectiveAt,x.SliceOrdinal}),
                intervals=plan.Assessment.Intervals},QuoteRatingService.Json);
            db.Add(new CommercialExposureIssueDecision {ExposureVersionId=header.Id,AssessedAt=plan.AssessedAt,DecisionJson=json,
                DecisionHash=SHA256.HashData(Encoding.UTF8.GetBytes(json)),CreatedBy=actorId,CreatedAt=plan.AssessedAt});
        }
        await db.SaveChangesAsync(token);
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
