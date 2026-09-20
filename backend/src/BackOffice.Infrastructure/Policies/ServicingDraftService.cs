using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingDraftRead(string Body, string Etag);
public sealed record ServicingDraftCreate(string Kind, Guid BaseVersionId, JsonElement CommonEffectiveIntent, string Reason);

public sealed class ServicingDraftService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Etag(byte[] value) => "\"" + Convert.ToBase64String(value) + "\"";
    private static void Current(byte[] actual, byte[] expected)
    { if (expected.Length != 8 || !actual.SequenceEqual(expected)) throw new QuoteOperationException(412, "servicing-version-conflict"); }
    private static void Active(ServicingDraft draft)
    { if (draft.State != "draft") throw new QuoteOperationException(409, "servicing-draft-closed"); }

    public async Task<ServicingDraftRead> ListAsync(ActorContext actor, Guid termId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var term = await HoldTerm(db, actor, termId, false, token);
        var drafts = await db.Set<ServicingDraft>().AsNoTracking().Where(x => x.BaseTermId == term.Id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Kind, State=x.Kind=="renewal" && x.State=="draft" && db.Set<RenewalLapseEvent>().Any(l=>l.TermId==x.BaseTermId)?"lapsed":x.State, x.CurrentRevisionId, x.BaseVersionId, x.UpdatedAt }).ToArrayAsync(token);
        var result = new ServicingDraftRead(JsonSerializer.Serialize(new { termId, policyId = term.PolicyId, items = drafts }, Json), Etag(term.RowVersion));
        await tx.CommitAsync(token); return result;
    }

    public async Task<ServicingDraftRead> ReadAsync(ActorContext actor, Guid draftId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var draft = await HoldDraft(db, actor, draftId, false, token);
        var result = await View(db, draft, token); await tx.CommitAsync(token); return result;
    }

    public async Task<ServicingDraftRead> ReadEditorAsync(ActorContext actor, Guid draftId, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var draft = await HoldDraft(db, actor, draftId, false, token);
        var revision = await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x => x.Id == draft.CurrentRevisionId, token);
        var assessment = await Assess(db, actor, draft, ServicingProposalInput.Parse(revision.ProposalJson, draft.BaseVersionId), token);
        if(draft.Kind=="renewal" && assessment.Slices.Count==0)
        {
            var prepared=await db.Set<RenewalPreparationVersion>().AsNoTracking().Where(x=>x.DraftId==draft.Id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(token);
            if(prepared is not null)assessment=assessment with{Slices=[new(prepared.StartsAt,assessment.Proposed,[])]};
        }
        var policy = await db.Set<Policy>().AsNoTracking().SingleAsync(x => x.Id == draft.PolicyId, token);
        var commercial = assessment.Base.GetProperty("productCode").GetString() == CommercialCaptureRules.ProductCode;
        if (commercial && !await db.Set<PolicyTerm>().AnyAsync(x => x.Id == draft.BaseTermId && x.CurrentVersionId == draft.BaseVersionId, token))
            assessment = assessment with { ReadinessIssues = assessment.ReadinessIssues.Concat([new QuoteFieldIssue("servicing-base-stale", "/baseVersionId")]).ToArray() };
        var result = new ServicingDraftRead(JsonSerializer.Serialize(new { draftId, revisionId = revision.Id, policy.ClientId,
            captureVersions = new { schemaVersion = "1.0", questionSetVersion = commercial ? CommercialCaptureRules.QuestionVersion : QuoteCatalogueIdentity.Version,
                referenceDataVersion = commercial ? CommercialCaptureRules.ReferenceVersion : QuoteCatalogueIdentity.Version },
            assessment = new { assessment.Base, assessment.Proposed, assessment.Changes, assessment.Slices,
                readinessIssues = assessment.ReadinessIssues.Select(issue => new { issue.Code, issue.Path, questionId = issue.QuestionId }) } }, Json), Etag(draft.RowVersion));
        await tx.CommitAsync(token); return result;
    }

    public Task<CommandOutcome> CreateAsync(ActorContext actor, Guid termId, byte[] version, ServicingDraftCreate input, string key, Guid correlation, CancellationToken token = default)
    {
        if (input.Kind is not ("adjustment" or "renewal" or "cancellation")) throw new QuoteInputException("servicing-invalid-kind");
        var proposal = ServicingProposalInput.Parse(JsonSerializer.Serialize(new
        { schemaVersion = "1.0", baseVersionId = input.BaseVersionId, reason = input.Reason, requestedBy = new { kind = "internal" }, commonEffectiveIntent = input.CommonEffectiveIntent, changes = Array.Empty<object>() }, Json), input.BaseVersionId);
        PolicyTerm? term = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/terms/{termId:D}/drafts", key, correlation),
            new { termId, version, input.Kind, proposal.Json }, "servicing.draft-created",
            async (db, ct) => { term = await HoldTerm(db, actor, termId, true, ct); },
            async (db, ct) =>
            {
                Current(term!.RowVersion, version);
                if(input.Kind=="renewal" && await db.Set<RenewalLapseEvent>().AnyAsync(x=>x.TermId==termId,ct))
                    throw new QuoteOperationException(409,"renewal-already-lapsed");
                var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.BaseVersionId && x.TermId == termId && x.PolicyId == term.PolicyId, ct)
                    ?? throw new QuoteOperationException(422, "servicing-base-mismatch");
                using var snapshot = JsonDocument.Parse(basis.SnapshotJson);
                if (snapshot.RootElement.GetProperty("productCode").GetString() == CommercialCaptureRules.ProductCode)
                {
                    if(input.Kind=="cancellation")throw new QuoteOperationException(409,"commercial-servicing-kind-unavailable");
                    if(input.Kind=="adjustment"&&term.CurrentVersionId!=basis.Id)throw new QuoteOperationException(409,"servicing-base-stale");
                    if(input.Kind=="renewal")
                    {
                        var quoteId=await db.Set<Policy>().Where(x=>x.Id==term.PolicyId).Select(x=>x.SourceQuoteId).SingleAsync(ct);
                        var source=await QuoteScope.ForQuoteAsync(db,actor,quoteId,QuoteAccess.Read,ct);
                        var renewal=await RenewalPreparationService.ResolveEligibility(db,source,term.Id,null,null,time.GetUtcNow(),ct);
                        if(renewal.Basis.Id!=basis.Id)throw new QuoteOperationException(409,"renewal-base-stale");
                    }
                }
                if (input.Kind != "cancellation" && await db.Set<ServicingDraft>().AnyAsync(x => x.BaseTermId == termId && x.Kind == input.Kind && x.State == "draft", ct))
                    throw new QuoteOperationException(409, "servicing-active-draft-exists");
                var draft = new ServicingDraft { PolicyId = term.PolicyId, BaseTermId = termId, BaseVersionId = input.BaseVersionId, Kind = input.Kind,
                    CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow(), UpdatedAt = time.GetUtcNow() };
                db.Add(draft); await db.SaveChangesAsync(ct);
                await Append(db, draft, proposal, actor.UserId, ct);
                term.UpdatedAt = time.GetUtcNow(); db.Entry(term).Property(x => x.UpdatedAt).IsModified = true;
                await db.SaveChangesAsync(ct); return await Receipt(db, draft, 201, ct);
            }, token);
    }

    public Task<CommandOutcome> SaveAsync(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken, string json, string key, Guid correlation, CancellationToken token = default)
        => Mutate(actor, draftId, version, new { leaseToken, json }, "save", key, correlation, async (db, draft, ct) =>
        {
            await DemandLease(db, draft.Id, actor.UserId, leaseToken, ct);
            var proposal = ServicingProposalInput.Parse(json, draft.BaseVersionId);
            var assessment = await Assess(db, actor, draft, proposal, ct);
            if (assessment.Base.GetProperty("productCode").GetString() == CommercialCaptureRules.ProductCode &&
                !await db.Set<PolicyTerm>().AnyAsync(x => x.Id == draft.BaseTermId && x.CurrentVersionId == draft.BaseVersionId, ct))
                throw new QuoteOperationException(409, "servicing-base-stale");
            await ServicingRatingService.InvalidateAsync(db, draft, "Proposal revision changed", time.GetUtcNow(), ct);
            await Append(db, draft, proposal, actor.UserId, ct);
        }, token);

    public Task<CommandOutcome> AbandonAsync(ActorContext actor, Guid draftId, byte[] version, Guid leaseToken, string reason, string key, Guid correlation, CancellationToken token = default)
    {
        reason = Reason(reason);
        return Mutate(actor, draftId, version, new { leaseToken, reason }, "abandon", key, correlation, async (db, draft, ct) =>
        {
            var lease = await DemandLease(db, draft.Id, actor.UserId, leaseToken, ct);
            await ServicingRatingService.InvalidateAsync(db, draft, reason, time.GetUtcNow(), ct);
            draft.State = "abandoned"; lease.Active = false; lease.ExpiresAt = time.GetUtcNow();
            Audit(db, draft.Id, actor.UserId, "servicing.abandon-reason", reason, new { draft.State });
        }, token);
    }

    public Task<CommandOutcome> LeaseAsync(ActorContext actor, Guid draftId, byte[] version, string action, Guid? leaseToken, string? reason, string key, Guid correlation, CancellationToken token = default)
    {
        if (action is not ("acquire" or "takeover" or "renew" or "release")) throw new QuoteInputException("servicing-invalid-lease-action");
        if (action == "takeover") reason = Reason(reason);
        return Mutate(actor, draftId, version, new { action, leaseToken, reason }, "lease/" + action, key, correlation, async (db, draft, ct) =>
        {
            var lease = await Lease(db, draft.Id, ct); var before = lease is null ? null : LeaseState(lease);
            ServicingLeaseState after;
            try
            {
                after = action switch
                {
                    "acquire" or "takeover" => ServicingLeaseRules.Acquire(before, actor.UserId, time.GetUtcNow(), action == "takeover", actor.HasCapability("policy-draft-takeover"), reason),
                    "renew" when before is not null => ServicingLeaseRules.Renew(before, actor.UserId, leaseToken ?? Guid.Empty, time.GetUtcNow()),
                    "release" when before is not null => ServicingLeaseRules.Release(before, actor.UserId, leaseToken ?? Guid.Empty, time.GetUtcNow()),
                    _ => throw new ArgumentException("No current lease.")
                };
            }
            catch (ArgumentException) { throw new QuoteOperationException(409, "servicing-lease-conflict"); }
            if (lease is null) { lease = new() { DraftId = draft.Id, CreatedBy = actor.UserId, CreatedAt = time.GetUtcNow() }; db.Add(lease); }
            lease.HolderId = after.HolderId; lease.Token = after.Token; lease.Generation = after.Generation; lease.ExpiresAt = after.ExpiresAt; lease.Active = after.Active;
            Audit(db, draft.Id, actor.UserId, "servicing.lease-ownership", reason, new { beforeHolderId = before?.HolderId, after.HolderId, after.Generation, after.ExpiresAt, after.Active });
        }, token, action == "takeover");
    }

    private Task<CommandOutcome> Mutate<T>(ActorContext actor, Guid id, byte[] version, T input, string action, string key, Guid correlation,
        Func<BackOfficeDbContext, ServicingDraft, CancellationToken, Task> handler, CancellationToken token, bool takeover = false)
    {
        ServicingDraft? draft = null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, $"/api/v1/drafts/{id:D}/{action}", key, correlation), new { id, version, input }, "servicing." + action.Replace('/', '-'),
            async (db, ct) =>
            {
                draft = await HoldDraft(db, actor, id, true, ct);
                if (takeover && !actor.HasCapability("policy-draft-takeover")) throw new QuoteOperationException(403, "servicing-takeover-denied");
            }, async (db, ct) =>
            {
                Active(draft!); Current(draft!.RowVersion, version); await handler(db, draft, ct);
                draft.UpdatedAt = time.GetUtcNow(); db.Entry(draft).Property(x => x.UpdatedAt).IsModified = true;
                await db.SaveChangesAsync(ct); return await Receipt(db, draft, 200, ct);
            }, token);
    }

    private static async Task<PolicyTerm> HoldTerm(BackOfficeDbContext db, ActorContext actor, Guid id, bool write, CancellationToken ct)
    {
        var owner = await db.Set<PolicyTerm>().AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.PolicyId).SingleOrDefaultAsync(ct)
            ?? throw new QuoteOperationException(404, "policy-term-not-found");
        await PolicyScope.Hold(db, actor, owner, ct, write);
        return write ? await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleAsync(ct)
            : await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(HOLDLOCK) WHERE Id={id}").SingleAsync(ct);
    }

    internal static async Task<ServicingDraft> HoldDraft(BackOfficeDbContext db, ActorContext actor, Guid id, bool write, CancellationToken ct)
    {
        var term = await db.Set<ServicingDraft>().AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.BaseTermId).SingleOrDefaultAsync(ct)
            ?? throw new QuoteOperationException(404, "servicing-draft-not-found");
        await HoldTerm(db, actor, term, write, ct);
        var draft=write ? await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleAsync(ct)
            : await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(HOLDLOCK) WHERE Id={id}").SingleAsync(ct);
        if(write && draft.Kind=="renewal" && draft.State=="draft" && await db.Set<RenewalLapseEvent>().AnyAsync(x=>x.TermId==draft.BaseTermId,ct))
            throw new QuoteOperationException(409,"renewal-already-lapsed");
        return draft;
    }

    private static Task<ServicingLease?> Lease(BackOfficeDbContext db, Guid id, CancellationToken ct)
        => db.Set<ServicingLease>().FromSqlInterpolated($"SELECT * FROM ServicingLease WITH(UPDLOCK,HOLDLOCK) WHERE DraftId={id}").SingleOrDefaultAsync(ct);
    private static ServicingLeaseState LeaseState(ServicingLease lease) => new(lease.HolderId, lease.Token, lease.Generation, lease.ExpiresAt, lease.Active);
    internal async Task<ServicingLease> DemandLease(BackOfficeDbContext db, Guid id, Guid actor, Guid fence, CancellationToken ct)
    {
        var lease = await Lease(db, id, ct) ?? throw new QuoteOperationException(409, "servicing-lease-required");
        try { ServicingLeaseRules.Demand(LeaseState(lease), actor, fence, time.GetUtcNow()); }
        catch (ArgumentException) { throw new QuoteOperationException(409, "servicing-lease-conflict"); }
        return lease;
    }

    internal async Task Append(BackOfficeDbContext db, ServicingDraft draft, CanonicalServicingProposal proposal, Guid actor, CancellationToken ct)
    {
        var number = (await db.Set<ServicingRevision>().Where(x => x.DraftId == draft.Id).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1;
        var revision = new ServicingRevision { DraftId = draft.Id, Sequence = number, ProposalJson = proposal.Json, ContentHash = proposal.ContentHash, CreatedBy = actor, CreatedAt = time.GetUtcNow() };
        db.Add(revision); await db.SaveChangesAsync(ct); draft.CurrentRevisionId = revision.Id;
    }

    internal async Task<ServicingProposalAssessment> Assess(BackOfficeDbContext db, ActorContext actor, ServicingDraft draft, CanonicalServicingProposal proposal, CancellationToken ct)
    {
        // The caller holds policy/term/draft scope before this projection. The
        // current authority and ordering boundary are never request fields.
        var snapshot = await db.Set<PolicyVersion>().AsNoTracking().Where(x => x.Id == draft.BaseVersionId && x.PolicyId == draft.PolicyId && x.TermId == draft.BaseTermId)
            .Select(x => x.SnapshotJson).SingleAsync(ct);
        var term = await db.Set<PolicyTerm>().SingleAsync(x => x.Id == draft.BaseTermId, ct);
        if(draft.Kind=="renewal")
        {
            var prepared=await db.Set<RenewalPreparationVersion>().AsNoTracking().Where(x=>x.DraftId==draft.Id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync(ct);
            if(prepared is not null)
            {
                using var intent=JsonDocument.Parse(prepared.TermIntentJson);
                var coverage=QuoteTerm.Assess(intent.RootElement).Term??throw new QuoteOperationException(409,"renewal-term-unavailable");
                if(coverage.StartsAt!=prepared.StartsAt || coverage.EndsAt!=prepared.EndsAt)throw new QuoteOperationException(409,"renewal-term-unavailable");
                return ServicingProposalRules.Assess(snapshot,proposal.Json,new(draft.PolicyId,draft.BaseVersionId,
                    prepared.StartsAt,prepared.EndsAt,prepared.StartsAt,time.GetUtcNow(),false,coverage));
            }
        }
        var latest = await db.Set<PolicyVersion>().Where(x => x.PolicyId == draft.PolicyId && x.TermId == draft.BaseTermId).MaxAsync(x => x.EffectiveAt, ct);
        return ServicingProposalRules.Assess(snapshot, proposal.Json,
            new(draft.PolicyId, draft.BaseVersionId, term.StartsAt, term.EndsAt, latest, time.GetUtcNow(), actor.AgencyId is null && actor.Roles.Contains("senior-underwriter")));
    }

    private async Task<CommandOutcome> Receipt(BackOfficeDbContext db, ServicingDraft draft, int status, CancellationToken ct)
    { var view = await View(db, draft, ct); return new(draft.Id, status, view.Body, Etag: view.Etag); }

    private static async Task<ServicingDraftRead> View(BackOfficeDbContext db, ServicingDraft draft, CancellationToken ct)
    {
        var revision = await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x => x.Id == draft.CurrentRevisionId, ct);
        var lease = await db.Set<ServicingLease>().AsNoTracking().SingleOrDefaultAsync(x => x.DraftId == draft.Id, ct);
        var policyReference = await db.Set<Policy>().Where(x => x.Id == draft.PolicyId).Select(x => x.Reference).SingleAsync(ct);
        var preparedByLabel = await db.Set<StaffUser>().Where(x => x.Id == draft.CreatedBy).Select(x => x.DisplayName).SingleAsync(ct);
        var snapshotJson = await db.Set<PolicyVersion>().Where(x => x.Id == draft.BaseVersionId && x.PolicyId == draft.PolicyId).Select(x => x.SnapshotJson).SingleAsync(ct);
        using var snapshot = JsonDocument.Parse(snapshotJson);
        var baseTermPremium = snapshot.RootElement.GetProperty("premium").GetProperty("termPremium").GetString();
        var state=draft.Kind=="renewal" && draft.State=="draft" && await db.Set<RenewalLapseEvent>().AnyAsync(x=>x.TermId==draft.BaseTermId,ct)?"lapsed":draft.State;
        // The fence is never sufficient authority: every command binds it to the
        // authenticated current holder and freshly checked policy permission.
        return new(JsonSerializer.Serialize(new { draft.Id, draft.PolicyId, draft.BaseTermId, draft.BaseVersionId, revisionId = revision.Id, draft.Kind, state,
            proposal = JsonSerializer.Deserialize<JsonElement>(revision.ProposalJson), draft.CreatedAt, draft.UpdatedAt,
            context = new { policyReference, productCode = snapshot.RootElement.GetProperty("productCode").GetString(), baseTermPremium, preparedBy = new { id = draft.CreatedBy, label = preparedByLabel } },
            lease = lease is null ? null : new { lease.Id, lease.HolderId, lease.Generation, leaseToken = lease.Token, lease.ExpiresAt, lease.Active } }, Json), Etag(draft.RowVersion));
    }

    private static string Reason(string? value) => value?.Trim() is { Length: >= 10 and <= 2000 } reason ? reason : throw new QuoteInputException("servicing-reason-required");
    private void Audit(BackOfficeDbContext db, Guid id, Guid actor, string kind, string? reason, object after)
        => db.Add(new AuditEvent { ActorId = actor, CreatedBy = actor, SubjectRecordId = id, EventType = kind, OccurredAt = time.GetUtcNow(),
            After = JsonSerializer.Serialize(new { reason, detail = after }, Json) });
}
