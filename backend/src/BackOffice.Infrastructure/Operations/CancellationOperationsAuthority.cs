using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal static class CancellationOperationsAuthority
{
    internal sealed record Source(CancellationConsequence Consequence, Policy Policy, PolicyVersion Version,
        CancellationIssueDecision Decision, ActorContext Actor);

    internal static async Task<Source> Hold(BackOfficeDbContext db, Guid workId, CancellationToken token)
    {
        var intent = await db.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkId == workId, token) ?? throw Invalid();
        var actor = await MidAuthority.Sender(db, intent.CreatedBy, token);
        await OperationalScope.HoldParents(db, actor, [new("policy", intent.PolicyId)], intent.Kind == "task-close" ? "task-write" : "document-generate", token);
        var notice = await db.Set<CancellationConsequence>().AsNoTracking().SingleOrDefaultAsync(x => x.TransactionId == intent.TransactionId && x.Kind == "notice", token) ?? throw Invalid();
        var source = await PolicyDocumentRenderService.LoadCancellation(db, notice.Id, intent.PolicyId, actor.UserId, token);
        var decision = await db.Set<CancellationIssueDecision>().AsNoTracking().SingleAsync(x => x.Id == notice.DecisionId, token);
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == workId, token);
        if (intent.VersionId != notice.VersionId || intent.TermId != notice.TermId || intent.DecisionId != notice.DecisionId || intent.CreatedBy != notice.CreatedBy ||
            work.CreatedBy != actor.UserId || work.SubjectRecordId != intent.Id || work.Kind != "cancellation-" + intent.Kind ||
            work.OperationKey != $"cancellation-{intent.Kind}/{intent.TransactionId:N}" || work.Payload != intent.PayloadJson ||
            DeliverySnapshots.Hash(intent.PayloadJson) != Convert.ToHexStringLower(intent.PayloadHash) ||
            !await db.Set<CancellationPreview>().AnyAsync(x => x.Id == decision.PreviewId && x.RuleSettingVersionId == work.ScenarioVersionId, token)) throw Invalid();
        using var payload = JsonDocument.Parse(intent.PayloadJson);
        using var original = JsonDocument.Parse(notice.PayloadJson);
        var fields = payload.RootElement.EnumerateObject().ToArray();
        if (fields.Length != original.RootElement.EnumerateObject().Count() || fields.Select(x => x.Name).Distinct().Count() != fields.Length ||
            payload.RootElement.GetProperty("intentId").GetGuid() != intent.Id || payload.RootElement.GetProperty("kind").GetString() != intent.Kind ||
            fields.Where(x => x.Name is not ("intentId" or "kind")).Any(x => !original.RootElement.TryGetProperty(x.Name, out var value) || value.GetRawText() != x.Value.GetRawText())) throw Invalid();
        using var snapshot = JsonDocument.Parse(source.Version.SnapshotJson);
        var product = snapshot.RootElement.GetProperty("productCode").GetString();
        var allowed = product == "commercial-combined"
            ? CommercialDocumentSelection.CancellationKinds(snapshot.RootElement.GetProperty("cover").GetProperty("sections").EnumerateArray().Any(x => x.GetProperty("code").GetString() == "employers-liability"))
            : product is "motor-trade-road-risks" or "motor-trade-combined" ? ["notice", "certificate-withdrawal", "task-close", "mid-removal"] : Array.Empty<string>();
        if (!allowed.Contains(intent.Kind)) throw Invalid();
        return new(intent, source.Policy, source.Version, decision, actor);
    }

    internal static bool Matches(Source source, OutboxWork work, JobLease lease)
        => work.Id == source.Consequence.WorkId && work.Kind == lease.Kind && work.Payload == lease.Payload &&
            work.Payload == source.Consequence.PayloadJson && work.OperationKey == lease.OperationKey && work.ScenarioVersionId == lease.ScenarioVersionId;
    internal static OperationalAccessException Invalid() => new(409, "cancellation-context-unavailable");
}
