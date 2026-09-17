using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyTemporalCandidate(Guid PolicyId, Guid TermId, Guid VersionId,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset EffectiveAt,
    DateTimeOffset ProcessedAt, int TransactionSequence, int SliceOrdinal, string Kind);
public sealed record PolicyTemporalSelection(PolicyTemporalCandidate Candidate, string State);

// Shared policy chronology. Scope must be held before querying. No mutable
// current pointer is evidence of cover applicability or processing-time knowledge.
public static class PolicyTemporalSelector
{
    public static IQueryable<PolicyDiscoveryRow> DiscoveryRows(BackOfficeDbContext db, DateTimeOffset effectiveAt, DateTimeOffset knownAt)
        => db.Database.SqlQuery<PolicyDiscoveryRow>($"""
        SELECT p.Id,p.Reference,p.ClientId,p.AgencyId,p.RelationshipId,c.LegalName AS ClientName,c.Reference AS ClientReference,
            a.LegalName AS AgencyName,product.Code AS ProductCode,
            CASE WHEN {effectiveAt}<t.StartsAt THEN 'scheduled' WHEN v.Kind='cancellation' THEN 'cancelled'
                 WHEN {effectiveAt}>=t.EndsAt THEN 'expired' ELSE 'active' END AS State,
            t.Id AS CurrentTermId,v.Id AS CurrentVersionId,t.StartsAt,t.EndsAt,v.ProcessedAt AS IssuedAt,
            CONVERT(date,JSON_VALUE(t.LocalTermIntentJson,'$.localStartDate')) AS InceptionDate
        FROM Policy p
        CROSS APPLY (
            SELECT TOP(1) term.* FROM PolicyTerm term WHERE term.PolicyId=p.Id AND EXISTS (
                SELECT 1 FROM PolicyVersion pv JOIN PolicyTransaction pt ON pt.Id=pv.TransactionId AND pt.PolicyId=pv.PolicyId AND pt.TermId=pv.TermId
                WHERE pv.TermId=term.Id AND pv.PolicyId=p.Id AND pv.ProcessedAt<={knownAt} AND pt.ProcessedAt<={knownAt}
                    AND pt.Kind IN ('new-business','adjustment','renewal','cancellation'))
            ORDER BY CASE WHEN term.StartsAt<={effectiveAt} AND {effectiveAt}<term.EndsAt THEN 0 WHEN term.EndsAt<={effectiveAt} THEN 1 ELSE 2 END,
                CASE WHEN term.StartsAt<={effectiveAt} THEN term.StartsAt END DESC,
                CASE WHEN term.StartsAt>{effectiveAt} THEN term.StartsAt END,term.Id
        ) t
        CROSS APPLY (
            SELECT TOP(1) pv.Id,pv.ProcessedAt,pt.Kind FROM PolicyVersion pv
            JOIN PolicyTransaction pt ON pt.Id=pv.TransactionId AND pt.PolicyId=pv.PolicyId AND pt.TermId=pv.TermId
            WHERE pv.PolicyId=p.Id AND pv.TermId=t.Id AND pv.ProcessedAt<={knownAt} AND pt.ProcessedAt<={knownAt}
                AND pt.Kind IN ('new-business','adjustment','renewal','cancellation') AND pv.EffectiveAt<t.EndsAt
                AND (pv.EffectiveAt<={effectiveAt} OR ({effectiveAt}<t.StartsAt AND pt.Kind IN ('new-business','renewal')))
            ORDER BY CASE WHEN pv.EffectiveAt<={effectiveAt} THEN pv.EffectiveAt END DESC,
                CASE WHEN pv.EffectiveAt>{effectiveAt} THEN pv.EffectiveAt END,
                pt.Sequence DESC,pv.SliceOrdinal DESC,pv.Id
        ) v
        JOIN ClientAccount c ON c.Id=p.ClientId JOIN Agency a ON a.Id=p.AgencyId
        JOIN ClientAgencyRelationship rel ON rel.Id=p.RelationshipId AND rel.ClientId=p.ClientId AND rel.AgencyId=p.AgencyId
        JOIN Product product ON product.Id=p.ProductId
        """);

    public static IQueryable<PolicyTemporalCandidate> Candidates(BackOfficeDbContext db, Guid policyId, DateTimeOffset knownAt) =>
        from version in db.Set<PolicyVersion>().AsNoTracking()
        join term in db.Set<PolicyTerm>().AsNoTracking() on new { version.TermId, version.PolicyId } equals new { TermId = term.Id, term.PolicyId }
        join transaction in db.Set<PolicyTransaction>().AsNoTracking() on new { version.TransactionId, version.TermId, version.PolicyId } equals new { TransactionId = transaction.Id, transaction.TermId, transaction.PolicyId }
        where version.PolicyId == policyId && version.ProcessedAt <= knownAt && transaction.ProcessedAt <= knownAt
        select new PolicyTemporalCandidate(version.PolicyId, term.Id, version.Id, term.StartsAt, term.EndsAt,
            version.EffectiveAt, version.ProcessedAt > transaction.ProcessedAt ? version.ProcessedAt : transaction.ProcessedAt,
            transaction.Sequence, version.SliceOrdinal, transaction.Kind);

    public static PolicyTemporalSelection? Select(IEnumerable<PolicyTemporalCandidate> candidates, Guid policyId,
        DateTimeOffset effectiveAt, DateTimeOffset knownAt, Guid? termId = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var visible = candidates.Where(x => x.PolicyId == policyId && (termId == null || x.TermId == termId)
            && x.ProcessedAt <= knownAt && x.StartsAt < x.EndsAt
            && x.Kind is "new-business" or "adjustment" or "renewal" or "cancellation").ToArray();
        var terms = visible.GroupBy(x => x.TermId).Select(x => x.First()).ToArray();
        var term = terms.Where(x => x.StartsAt <= effectiveAt && effectiveAt < x.EndsAt)
            .OrderByDescending(x => x.StartsAt).ThenBy(x => x.TermId).FirstOrDefault()
            ?? terms.Where(x => x.EndsAt <= effectiveAt).OrderByDescending(x => x.EndsAt).ThenBy(x => x.TermId).FirstOrDefault()
            ?? terms.Where(x => x.StartsAt > effectiveAt).OrderBy(x => x.StartsAt).ThenBy(x => x.TermId).FirstOrDefault();
        if (term is null) return null;
        var versions = visible.Where(x => x.TermId == term.TermId);
        var selected = Latest(versions.Where(x => x.EffectiveAt <= effectiveAt && x.EffectiveAt < x.EndsAt));
        // A known future first issue may be displayed as scheduled. Never use a
        // future adjustment/cancellation as the fallback for a missing base.
        if (selected is null && effectiveAt < term.StartsAt)
            selected = versions.Where(x => x.Kind is "new-business" or "renewal")
                .OrderBy(x => x.EffectiveAt).ThenBy(x => x.TransactionSequence).ThenBy(x => x.SliceOrdinal).ThenBy(x => x.VersionId).FirstOrDefault();
        if (selected is null) return null;
        var state = effectiveAt < term.StartsAt ? "scheduled" : selected.Kind == "cancellation" ? "cancelled"
            : effectiveAt >= term.EndsAt ? "expired" : "active";
        return new(selected, state);
    }

    public static PolicyTemporalCandidate? AtTermEnd(IEnumerable<PolicyTemporalCandidate> candidates, Guid policyId,
        Guid termId, DateTimeOffset exclusiveEnd, DateTimeOffset knownAt) => Latest(candidates.Where(x =>
            x.PolicyId == policyId && x.TermId == termId && x.ProcessedAt <= knownAt && x.EffectiveAt < exclusiveEnd
            && x.Kind is "new-business" or "adjustment" or "renewal" or "cancellation"));

    private static PolicyTemporalCandidate? Latest(IEnumerable<PolicyTemporalCandidate> candidates) => candidates
        .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.TransactionSequence)
        .ThenByDescending(x => x.SliceOrdinal).ThenBy(x => x.VersionId).FirstOrDefault();
}
