using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public sealed record QuoteEvidenceFileView(Guid Id, Guid QuoteId, string FileName, string ContentType, int Length,
    string Sha256, DateTimeOffset UploadedAt, string ScreeningState, string ScreeningMethod);
public sealed record QuoteEvidenceItem(Guid Id, Guid QuoteId, Guid RevisionId, string RequirementCode, Guid? RiskItemId,
    Guid FileId, string InputFingerprint, string Reason, string State, DateTimeOffset CreatedAt, string CreatedByLabel,
    string Etag, QuoteEvidenceFileView File, DateTimeOffset? WithdrawnAt, string? WithdrawalReason);
public sealed record QuoteEvidenceRequirementView(string Code, string Path, Guid? RiskItemId, string Label,
    string InputFingerprint, string State, Guid? EvidenceId);
public sealed record QuoteEvidenceSnapshot(Guid RevisionId, QuoteEvidenceRequirementView[] Requirements, QuoteEvidenceItem[] Items);

public static class QuoteEvidenceReadModel
{
    // Caller retains quote read authority through all materialization. No bytes
    // are selected here, and no stored received/verified flag is trusted.
    public static async Task<QuoteEvidenceSnapshot> AssessAsync(BackOfficeDbContext db, QuoteRevision revision, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Evidence reads require held quote scope.");
        using var proposal = JsonDocument.Parse(revision.ProposalJson); var pins = QuoteService.Pins(revision);
        var requirements = QuoteEvidenceRequirements.ForProposal(proposal.RootElement)
            .Select(x => QuoteEvidenceRules.Prepare(proposal.RootElement, x.Code, x.RiskItemId, pins)).ToArray();
        var rows = await (from evidence in db.Set<QuoteCaptureEvidence>().AsNoTracking()
            join source in db.Set<QuoteRevision>().AsNoTracking() on evidence.RevisionId equals source.Id
            join file in db.Set<QuoteEvidenceFile>().AsNoTracking() on evidence.FileId equals file.Id
            join actor in db.Set<StaffUser>().AsNoTracking() on evidence.ActorId equals actor.Id
            where evidence.QuoteId == revision.QuoteId && source.Number <= revision.Number
            orderby evidence.CreatedAt descending, evidence.Id descending
            select new { Evidence = evidence, Actor = actor.DisplayName,
                File = new QuoteEvidenceFileView(file.Id, file.QuoteId, file.FileName, file.ContentType, file.ByteLength,
                    file.Sha256, file.CreatedAt, file.ScreeningState, file.ScreeningMethod) }).ToArrayAsync(token);
        var withdrawn = await db.Set<QuoteEvidenceWithdrawal>().AsNoTracking().Where(x => x.QuoteId == revision.QuoteId)
            .ToDictionaryAsync(x => x.EvidenceId, token);
        var items = rows.Select(row =>
        {
            var evidence = row.Evidence; withdrawn.TryGetValue(evidence.Id, out var withdrawal);
            var current = requirements.SingleOrDefault(x => x.Requirement.Code == evidence.RequirementCode && x.Requirement.RiskItemId == evidence.RiskItemId);
            var state = withdrawal is not null ? "withdrawn" : row.File.ScreeningState == "accepted" && current is not null &&
                QuoteEvidenceRules.Matches(current, evidence.InputFingerprint) ? "current" : "stale";
            return new QuoteEvidenceItem(evidence.Id, evidence.QuoteId, evidence.RevisionId, evidence.RequirementCode, evidence.RiskItemId,
                evidence.FileId, evidence.InputFingerprint, evidence.Reason, state, evidence.CreatedAt, row.Actor,
                "\"" + Convert.ToBase64String(evidence.RowVersion) + "\"", row.File, withdrawal?.CreatedAt, withdrawal?.Reason);
        }).ToArray();
        return new(revision.Id, requirements.Select(input =>
        {
            var current = items.FirstOrDefault(x => x.State == "current" && x.RequirementCode == input.Requirement.Code && x.RiskItemId == input.Requirement.RiskItemId);
            return new QuoteEvidenceRequirementView(input.Requirement.Code, input.Requirement.Path, input.Requirement.RiskItemId,
                input.Requirement.Label, input.InputFingerprint, current is null ? "missing" : "current", current?.Id);
        }).ToArray(), items);
    }
}
