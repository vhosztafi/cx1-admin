using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyRiskHistoryEntry(Guid VersionId, Guid TermId, int VersionSequence, DateTimeOffset EffectiveAt,
    DateTimeOffset ProcessedAt, string ContentHash, JsonElement? Item);
public sealed record PolicyRiskHistory(Guid PolicyId, string Kind, Guid ItemId, IReadOnlyList<PolicyRiskHistoryEntry> Versions);

public sealed partial class PolicyHistoryService
{
    public async Task<PolicyRiskHistory> RiskHistoryAsync(ActorContext actor, Guid policyId, string kind, Guid itemId, CancellationToken token = default)
    {
        if (kind is not ("drivers" or "vehicles") || itemId == Guid.Empty) throw new QuoteOperationException(400, "policy-risk-kind-invalid");
        await using var db = await factory.CreateDbContextAsync(token);
        await using var tx = await db.Database.BeginTransactionAsync(token);
        await PolicyScope.Hold(db, actor, policyId, token);
        var versions = await db.Set<PolicyVersion>().AsNoTracking().Where(x => x.PolicyId == policyId)
            .OrderByDescending(x => x.ProcessedAt).ThenByDescending(x => x.Sequence).ThenBy(x => x.Id).ToArrayAsync(token);
        var entries = new List<PolicyRiskHistoryEntry>(); var found = false;
        foreach (var version in versions)
        {
            using var snapshot = JsonDocument.Parse(version.SnapshotJson);
            var declarations = PolicyHistoryRules.Declarations(snapshot.RootElement);
            JsonElement? item = null;
            if (declarations.GetProperty("risk").TryGetProperty(kind, out var items))
                foreach (var candidate in items.EnumerateArray())
                    if (candidate.TryGetProperty("id", out var id) && id.GetGuid() == itemId) { item = candidate.Clone(); found = true; break; }
            entries.Add(new(version.Id, version.TermId, version.Sequence, version.EffectiveAt, version.ProcessedAt,
                Convert.ToHexStringLower(version.ContentHash), item));
        }
        if (!found) throw new QuoteOperationException(404, "policy-risk-item-not-found");
        await tx.CommitAsync(token); return new(policyId, kind, itemId, entries);
    }
}
