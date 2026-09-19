using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyReconstructionView(Guid Id, Guid PolicyId, Guid TermId, Guid? VersionId, string? ContentHash,
    DateTimeOffset EffectiveAt, DateTimeOffset KnownAt, string CoverageState, string ManifestHash,
    Guid ActorId, string Reason, DateTimeOffset CreatedAt, string State);

public sealed partial class PolicyHistoryService
{
    public async Task<PolicyHistoryView> TermHistoryAsync(ActorContext actor, Guid termId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var policyId = await db.Set<PolicyTerm>().Where(x => x.Id == termId).Select(x => (Guid?)x.PolicyId).SingleOrDefaultAsync(token)
            ?? throw new BackOffice.Infrastructure.Quotes.QuoteOperationException(404, "policy-term-not-found");
        return await ReadAsync(actor, policyId, token: token, termId: termId);
    }

    public async Task<IReadOnlyList<PolicyReconstructionView>> ReconstructionsAsync(ActorContext actor, Guid policyId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await PolicyScope.Hold(db, actor, policyId, token);
        var rows = await (from request in db.Set<PolicyReconstructionRequest>().AsNoTracking()
            join work in db.Set<OutboxWork>() on request.WorkId equals work.Id
            where request.PolicyId == policyId
            orderby request.CreatedAt descending, request.Id
            select new { request.Id, request.PolicyId, request.TermId, request.VersionId, request.VersionHash,
                request.EffectiveAt, request.KnownAt, request.CoverageState, request.ManifestHash,
                request.ActorId, request.Reason, request.CreatedAt, work.State }).ToArrayAsync(token);
        var result = rows.Select(x => new PolicyReconstructionView(x.Id, x.PolicyId, x.TermId, x.VersionId,
            x.VersionHash is null ? null : Convert.ToHexStringLower(x.VersionHash), x.EffectiveAt, x.KnownAt,
            x.CoverageState, Convert.ToHexStringLower(x.ManifestHash), x.ActorId, x.Reason, x.CreatedAt, x.State)).ToArray();
        await tx.CommitAsync(token); return result;
    }
}
