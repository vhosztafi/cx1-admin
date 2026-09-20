using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed partial class CapacityService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public const string WorkKind = "capacity-escalation";
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> CreateAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid referralId, byte[] version,
        byte[] referralVersion, Guid providerId, string reason, string key, Guid correlationId, CancellationToken token = default)
    {
        reason = QuoteRatingService.Reason(reason); UnderwritingDecisionContext? held = null; QuoteReferral? referral = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/referrals/{referralId:D}/escalations", key, correlationId),
            new { quoteId, cycleId, referralId, version = Convert.ToBase64String(version), referralVersion = Convert.ToBase64String(referralVersion), providerId, reason }, "capacity.created",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-escalate", time.GetUtcNow(), true, ct);
                await Provider(db, held, providerId, ct);
                referral = await db.Set<QuoteReferral>().FromSqlInterpolated($"SELECT * FROM QuoteReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={referralId} AND CycleId={cycleId} AND QuoteId={quoteId}").SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404, "referral-not-found");
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                UnderwritingDecisionContext.CheckVersion(referral!.RowVersion, referralVersion, "stale-referral");
                if (referral.State is "superseded" or "declined") throw new QuoteOperationException(409, "referral-reopen-required");
                if (await db.Set<CapacityEscalation>().AnyAsync(x => x.ReferralId == referralId, ct)) throw new QuoteOperationException(409, "capacity-escalation-exists");
                var row = new CapacityEscalation { QuoteId = quoteId, CycleId = cycleId, ReferralId = referralId, ProviderId = providerId,
                    BinderVersionId = held.Cycle.BinderVersionId, RaisedBy = actor.UserId, Reason = reason, CreatedAt = now, CreatedBy = actor.UserId, UpdatedAt = now };
                db.Add(row); await db.SaveChangesAsync(ct); await QuoteReferralService.RefreshState(db, held, now, ct);
                return await held.Receipt(db, row.Id, 201, "capacity.created", now, ct);
            }, token);
    }

    public Task<CommandOutcome> SendAsync(ActorContext actor, Guid quoteId, Guid cycleId, Guid escalationId, byte[] version,
        byte[] escalationVersion, string body, IReadOnlyList<Guid> evidenceAssociationIds, Guid scenarioVersionId, string key, Guid correlationId, CancellationToken token = default)
    {
        body = Correspondence(body);
        if (evidenceAssociationIds.Count > 20 || evidenceAssociationIds.Contains(Guid.Empty) || evidenceAssociationIds.Distinct().Count() != evidenceAssociationIds.Count)
            throw new QuoteOperationException(422, "capacity-evidence-invalid");
        var selectedIds = evidenceAssociationIds.Order().ToArray();
        UnderwritingDecisionContext? held = null; CapacityEscalation? escalation = null; QuoteReferral? referral = null; SettingVersion? scenario = null; int? dueHours = null; int? dueWorkingDays = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/escalations/{escalationId:D}/send", key, correlationId),
            new { quoteId, cycleId, escalationId, version = Convert.ToBase64String(version), escalationVersion = Convert.ToBase64String(escalationVersion), body, evidenceAssociationIds = selectedIds, scenarioVersionId }, "capacity.submitted",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-escalate", time.GetUtcNow(), true, ct);
                escalation = await HoldEscalation(db, held, escalationId, ct);
                referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == escalation.ReferralId && x.CycleId == cycleId, ct);
                scenario = await db.Set<SettingVersion>().FromSqlInterpolated($"SELECT * FROM SettingVersion WITH(HOLDLOCK) WHERE Id={scenarioVersionId}").AsNoTracking().SingleOrDefaultAsync(ct);
                var configuration = scenario is null ? null : CapacitySeed.Parse(scenario);
                if (configuration is null || !CapacitySeed.ForProduct(configuration.Value.Scenario, held.Input.IsCommercial) || scenario!.EffectiveFrom > time.GetUtcNow() || await db.Set<SettingVersion>().AnyAsync(x => x.Scope == scenario.Scope && x.Version > scenario.Version && x.EffectiveFrom <= time.GetUtcNow(), ct))
                    throw new QuoteOperationException(409, "capacity-scenario-unavailable");
                dueHours = configuration.Value.ResponseDueHours;
                dueWorkingDays = configuration.Value.ResponseDueWorkingDays;
                foreach (var id in selectedIds)
                    if (!await db.Set<UnderwritingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM UnderwritingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={id} AND QuoteId={quoteId} AND CycleId={cycleId}").AsNoTracking().AnyAsync(ct))
                        throw new QuoteOperationException(404, "underwriting-evidence-not-found");
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                UnderwritingDecisionContext.CheckVersion(escalation!.RowVersion, escalationVersion, "stale-capacity-escalation");
                if (escalation.State is "queued" or "superseded" || referral!.State is "superseded" or "declined") throw new QuoteOperationException(409, "capacity-submission-state");
                var purposes = await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, ct);
                foreach (var proof in await db.Set<UnderwritingEvidenceAssociation>().AsNoTracking().Where(x => selectedIds.Contains(x.Id)).ToArrayAsync(ct))
                    if (proof.WithdrawnEventId is not null || !purposes.Any(x => x.Code == proof.RequirementCode && x.InputFingerprint == proof.InputFingerprint) ||
                        !await db.Set<QuoteEvidenceFile>().AnyAsync(x => x.Id == proof.FileId && x.ScreeningState == "accepted", ct)) throw new QuoteOperationException(409, "capacity-evidence-unavailable");
                // Explicit provider projection: no full proposal, private support notes,
                // client contact details or unselected evidence bodies leave this boundary.
                using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
                var conditionTargetIds = held.Input.IsCommercial
                    ? (referral.RiskItemId is Guid locationId ? new[] { locationId } : Array.Empty<Guid>())
                    : proposal.RootElement.GetProperty("cover").GetProperty("requestedSections").EnumerateArray()
                    .Where(x => x.GetProperty("selected").GetBoolean() && x.GetProperty("code").GetString() == "premises")
                    .SelectMany(x => x.GetProperty("premisesIds").EnumerateArray().Select(p => p.GetGuid())).Distinct().Order().ToArray();
                var context = JsonSerializer.Serialize(new { format = "capacity-submission-1", productCode = held.Eligible.Capture.Product.Code, quoteId, cycleId, referralId = referral.Id,
                    revisionId = held.Revision.Id, pricingInputHash = Convert.ToHexStringLower(held.Cycle.PricingInputHash),
                    providerId = escalation.ProviderId, binderVersionId = held.Cycle.BinderVersionId, productVersionId = held.Cycle.ProductVersionId,
                    startsAt = held.Cycle.StartsAt, endsAt = held.Cycle.EndsAt, ruleCode = referral.RuleCode, dimension = referral.Dimension,
                    targetId = referral.RiskItemId, conditionTargetIds, requiredAuthority = JsonSerializer.Deserialize<JsonElement>(referral.RequiredAuthorityJson),
                    body, evidenceAssociationIds = selectedIds, scenarioVersionId });
                var hash = QuoteCanonicalJson.Create(context, QuoteService.Pins(held.Revision)).ContentHash;
                var submission = new CapacitySubmission { QuoteId = quoteId, CycleId = cycleId, EscalationId = escalationId,
                    Sequence = checked((await db.Set<CapacitySubmission>().Where(x => x.EscalationId == escalationId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    Body = body, ContextJson = context, ContextHash = hash, ScenarioVersionId = scenario!.Id, SubmittedBy = actor.UserId,
                    SubmittedAt = now, ResponseDueAt = dueWorkingDays is int days ? BackOffice.Application.Underwriting.CapacityRules.ResponseDue(now, days) : now.AddHours(dueHours!.Value), CreatedAt = now, CreatedBy = actor.UserId };
                var work = new OutboxWork { Kind = WorkKind, SubjectRecordId = submission.Id, OperationKey = $"capacity/{submission.Id:N}", ScenarioVersionId = scenario.Id,
                    Payload = JsonSerializer.Serialize(new { quoteId, cycleId, escalationId, submissionId = submission.Id }), NextAttemptAt = now,
                    CorrelationId = correlationId, CreatedAt = now, CreatedBy = actor.UserId, UpdatedAt = now };
                db.Add(work); await db.SaveChangesAsync(ct); submission.WorkId = work.Id; db.Add(submission); await db.SaveChangesAsync(ct);
                foreach (var id in selectedIds) db.Add(new CapacitySubmissionEvidence { QuoteId = quoteId, CycleId = cycleId, SubmissionId = submission.Id, EvidenceAssociationId = id, CreatedAt = now, CreatedBy = actor.UserId });
                db.Add(new CapacityMessage { QuoteId = quoteId, CycleId = cycleId, EscalationId = escalationId, SubmissionId = submission.Id, ReferralId = referral.Id,
                    ProviderId = escalation.ProviderId, Sequence = await NextMessage(db, escalationId, ct), Body = body, DefinitionJson = context,
                    ContentHash = Convert.FromHexString(hash), RecordedAt = now, RecordedBy = actor.UserId, CreatedAt = now, CreatedBy = actor.UserId });
                escalation.CurrentSubmissionId = submission.Id; escalation.CurrentResponseId = null; escalation.State = "queued"; escalation.UpdatedAt = now;
                await db.SaveChangesAsync(ct); await QuoteReferralService.RefreshState(db, held, now, ct);
                var receipt = await held.Receipt(db, escalationId, 202, "capacity.submitted", now, ct);
                return receipt with { Body = JsonSerializer.Serialize(new { id = escalationId, quoteId, quoteEtag = receipt.Etag, jobId = work.Id, state = "queued" }) };
            }, token);
    }

    internal static async Task<CapacityEscalation> HoldEscalation(BackOfficeDbContext db, UnderwritingDecisionContext held, Guid id, CancellationToken token)
    {
        var quoteId = held.Cycle.QuoteId; var cycleId = held.Cycle.Id;
        var row = await db.Set<CapacityEscalation>().FromSqlInterpolated($"SELECT * FROM CapacityEscalation WITH(UPDLOCK,HOLDLOCK) WHERE Id={id} AND QuoteId={quoteId} AND CycleId={cycleId}").SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "capacity-escalation-not-found");
        await Provider(db, held, row.ProviderId, token); return row;
    }
    private static async Task Provider(BackOfficeDbContext db, UnderwritingDecisionContext held, Guid id, CancellationToken token)
    {
        if (held.Eligible.BinderVersion.ProviderId != id || !await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={id} AND State=N'active'").AsNoTracking().AnyAsync(token))
            throw new QuoteOperationException(409, "capacity-provider-unavailable");
    }
    internal static async Task<int> NextMessage(BackOfficeDbContext db, Guid id, CancellationToken token) =>
        checked((await db.Set<CapacityMessage>().Where(x => x.EscalationId == id).MaxAsync(x => (int?)x.Sequence, token) ?? 0) + 1);
    public static string Correspondence(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 8000 || body.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new QuoteOperationException(422, "capacity-body-required");
        return body.Trim();
    }
}
