using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class ServicingRatingService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    public const string WorkKind = "servicing-rating";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SqlCommandBoundary commands = new(factory, time);
    private readonly ServicingDraftService drafts = new(factory, time);

    public Task<CommandOutcome> RateAsync(ActorContext actor, Guid draftId, Guid revisionId, byte[] version,
        Guid leaseToken, string reason, string key, Guid correlation, CancellationToken token = default)
    {
        if (version.Length != 8 || revisionId == Guid.Empty || leaseToken == Guid.Empty) throw new QuoteOperationException(400, "servicing-rating-input-invalid");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10 || reason.Length > 2000 || reason.Any(char.IsControl))
            throw new QuoteOperationException(422, "servicing-rating-reason-required");
        reason = reason.Trim(); HeldServicingRating? held = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/drafts/{draftId:D}/rate", key, correlation),
            new { draftId, revisionId, version = Convert.ToBase64String(version), leaseToken, reason }, "servicing.rating-requested",
            async (db, ct) => { held = await ServicingRatingScope.HoldAsync(db, actor, draftId, time.GetUtcNow(), ct); },
            async (db, ct) =>
            {
                var context = held!; var draft = context.Draft; var now = time.GetUtcNow();
                if (draft.Kind != "adjustment" || draft.State != "draft") throw new QuoteOperationException(409, "servicing-rating-state");
                if (draft.CurrentRevisionId != revisionId || !CryptographicOperations.FixedTimeEquals(draft.RowVersion, version))
                    throw new QuoteOperationException(412, "servicing-version-conflict");
                await drafts.DemandLease(db, draftId, context.Source.Scope.Actor.UserId, leaseToken, ct);
                var latest = await db.Set<PolicyVersion>().Where(x => x.PolicyId == draft.PolicyId && x.TermId == draft.BaseTermId)
                    .OrderByDescending(x => x.EffectiveAt).ThenByDescending(x => x.Sequence).Select(x => x.Id).FirstAsync(ct);
                if (latest != draft.BaseVersionId) throw new QuoteOperationException(409, "servicing-base-stale");
                var proposal = ServicingProposalInput.Parse(context.Revision.ProposalJson, draft.BaseVersionId);
                var assessment = await drafts.Assess(db, context.Source.Scope.Actor, draft, proposal, ct);
                // A later slice may restore original cover after a genuine
                // temporary change. Materiality belongs to the entire schedule.
                if (assessment.Slices.Count == 0 || assessment.Slices.All(slice =>
                    QuoteRevisionDiff.Compare(assessment.Base, slice.Proposed).All(change => change.Kind == "reordered")))
                    throw new QuoteOperationException(422, "servicing-no-material-change");
                if (assessment.ReadinessIssues.Count != 0) throw new QuoteValidationException(assessment.ReadinessIssues);
                using var baseJson = JsonDocument.Parse(context.Base.SnapshotJson);
                if (!PolicySnapshotShape.Valid(baseJson.RootElement)) throw new QuoteOperationException(409, "servicing-base-format-unavailable");
                var annual = decimal.Parse(baseJson.RootElement.GetProperty("premium").GetProperty("annualPremium").GetString()!, CultureInfo.InvariantCulture);
                var cumulative = new List<Guid>(); var slices = new List<ServicingRatingSliceInput>();
                foreach (var slice in assessment.Slices.OrderBy(x => x.EffectiveAt))
                {
                    cumulative.AddRange(slice.ChangeIds);
                    slices.Add(new(slice.EffectiveAt, cumulative.Order().ToArray(), QuoteUnderwritingInput.Project(slice.Proposed, context.Eligible.Rating)));
                }
                var input = new ServicingRatingRequestInput {
                    Format = "servicing-rating-input-1", DraftId = draft.Id, RevisionId = revisionId, PolicyId = draft.PolicyId,
                    BaseTermId = draft.BaseTermId, BaseVersionId = draft.BaseVersionId, ProductVersionId = context.Term.ProductVersionId,
                    AgencyTermsVersionId = context.Eligible.Capture.Terms.Id, RatingRuleVersionId = context.Eligible.RatingVersion.Id,
                    BinderVersionId = context.Eligible.BinderVersion.Id, AuthorityVersionId = context.Eligible.AuthorityVersion.Id,
                    RuntimeVersionId = context.Eligible.RuntimeVersion.Id, ScenarioVersionId = context.Eligible.ScenarioVersion.Id,
                    ServicingSettingVersionId = context.Setting.Id, RequestedBy = context.Source.Scope.Actor.UserId, RequestedAt = now,
                    BaseContentHash = Convert.ToHexStringLower(context.Base.ContentHash), RevisionContentHash = Convert.ToHexStringLower(context.Revision.ContentHash),
                    Term = context.ResolvedTerm, BaseAnnualPremium = annual, CommissionBasisPoints = context.Eligible.CommissionBasisPoints,
                    MinimumPremium = context.Eligible.MinimumPremium, Fee = context.Settings.AdjustmentFee, RatingDefinition = context.Eligible.Rating, Slices = slices.AsReadOnly()
                };
                var encoded = ServicingRatingInput.Encode(input);
                var cycle = new ServicingCycle {
                    DraftId = draftId, PolicyId = draft.PolicyId, BaseTermId = draft.BaseTermId, BaseVersionId = draft.BaseVersionId, RevisionId = revisionId,
                    ProductId = context.Term.ProductId, ProductVersionId = input.ProductVersionId, AgencyTermsVersionId = input.AgencyTermsVersionId,
                    RatingRuleVersionId = input.RatingRuleVersionId, BinderVersionId = input.BinderVersionId, AuthorityVersionId = input.AuthorityVersionId,
                    RuntimeVersionId = input.RuntimeVersionId, ScenarioVersionId = input.ScenarioVersionId, ServicingSettingVersionId = input.ServicingSettingVersionId,
                    Sequence = checked((await db.Set<ServicingCycle>().Where(x => x.DraftId == draftId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                    InputHash = encoded.ContentHash, InputJson = encoded.Json, RequestedBy = input.RequestedBy, CreatedBy = input.RequestedBy, CreatedAt = now, UpdatedAt = now
                };
                var work = new OutboxWork { Kind = WorkKind, SubjectRecordId = cycle.Id, OperationKey = $"servicing-rating/{cycle.Id:N}",
                    ScenarioVersionId = input.ScenarioVersionId, Payload = JsonSerializer.Serialize(new { cycleId = cycle.Id, draftId }, Json),
                    NextAttemptAt = now, CorrelationId = correlation, CreatedBy = input.RequestedBy, CreatedAt = now, UpdatedAt = now };
                db.Add(work); await db.SaveChangesAsync(ct); cycle.WorkId = work.Id; db.Add(cycle); await db.SaveChangesAsync(ct);
                await InvalidateAsync(db, draft, reason, now, ct);
                draft.CurrentCycleId = cycle.Id; draft.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                var etag = "\"" + Convert.ToBase64String(draft.RowVersion) + "\"";
                return new CommandOutcome(cycle.Id, 202, JsonSerializer.Serialize(new { id = cycle.Id, draftId, revisionId, jobId = work.Id, state = "queued", draftEtag = etag }, Json), Etag: etag);
            }, token);
    }

    internal static async Task InvalidateAsync(BackOfficeDbContext db, ServicingDraft draft, string reason, DateTimeOffset now, CancellationToken token)
    {
        if (draft.CurrentCycleId is not { } id) return;
        var previous = await db.Set<ServicingCycle>().FromSqlInterpolated($"SELECT * FROM ServicingCycle WITH(UPDLOCK,HOLDLOCK) WHERE Id={id} AND DraftId={draft.Id}").SingleAsync(token);
        if (previous.State != "superseded")
        { previous.State = "superseded"; previous.SupersededAt = now; previous.SupersededReason = reason; previous.UpdatedAt = now; }
        draft.CurrentCycleId = null;
    }
}
