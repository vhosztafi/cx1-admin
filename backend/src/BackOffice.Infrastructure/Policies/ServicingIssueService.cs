using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record ServicingIssueInput(Guid CycleId, Guid RatingId, Guid TermsVersionId, Guid AcceptanceId,
    string TermsHash, string AssuranceHash, string Reason);

public sealed class ServicingIssueService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory,time);

    public Task<CommandOutcome> IssueAsync(ActorContext actor, Guid draftId, byte[] version, Guid lease,
        ServicingIssueInput input, string key, Guid correlation, CancellationToken token = default)
    {
        if (input is null || new[] { draftId,lease,input.CycleId,input.RatingId,input.TermsVersionId,input.AcceptanceId }.Contains(Guid.Empty) ||
            version is null || version.Length != 8 || !ReferralRules.Hash(input.TermsHash) || !ReferralRules.Hash(input.AssuranceHash))
            throw new QuoteOperationException(422,"servicing-issue-input-invalid");
        input = input with { Reason = QuoteRatingService.Reason(input.Reason,1000) };
        ServicingDecisionContext? held = null; EffectiveUnderwritingGrant? grant = null; bool issued = false;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/issue",key,correlation),
            new { draftId,version=Convert.ToBase64String(version),lease,input },"servicing.issued",
            async (db,ct) =>
            {
                var hint = await (from d in db.Set<ServicingDraft>() join p in db.Set<Policy>() on d.PolicyId equals p.Id
                    where d.Id==draftId select new { p.SourceQuoteId }).SingleOrDefaultAsync(ct)
                    ?? throw new QuoteOperationException(404,"servicing-draft-not-found");
                var source = await QuoteUnderwritingScope.HoldAsync(db,actor,hint.SourceQuoteId,"policy-issue-within-authority",ct);
                var draft = await ServicingDraftService.HoldDraft(db,source.Scope.Actor,draftId,true,ct);
                var now = time.GetUtcNow(); issued = draft.State=="issued";
                if (issued)
                {
                    // Receipt expiry cannot create another effect. Recheck the
                    // actual issuing grant (immutable definition) and current
                    // actor before the command boundary may return old bytes.
                    await ReplayAuthority(db,source,draft,now,ct); return;
                }
                if (draft.State!="draft" || draft.Kind is not("adjustment" or "renewal")) throw new QuoteOperationException(409,"servicing-issue-state");
                if(draft.Kind=="renewal" && !RenewalLifecycleRules.WithinIssueWindow(now,await db.Set<PolicyTerm>().Where(x=>x.Id==draft.BaseTermId).Select(x=>x.EndsAt).SingleAsync(ct)))
                    throw new QuoteOperationException(409,"renewal-late-issue-unsupported");
                held = await ServicingDecisionContext.Hold(db,source.Scope.Actor,draftId,"policy-issue-within-authority",now,ct,input.CycleId);
                var remaining = held.Input.Term with { Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt };
                var grants = await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,held.Cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                    held.Scope.Eligible.Capture.Product.Code,remaining,now,ct);
                foreach (var candidate in grants.OrderBy(x=>x.Grant.Id))
                    if (await ServicingReferralService.ResolutionAuthority(db,held,candidate.Definition,now,ct)) { grant=candidate; break; }
                if (grant is null) throw new QuoteOperationException(403,"servicing-issue-authority-required");
            },
            async (db,ct) =>
            {
                if (issued) throw new QuoteOperationException(409,"servicing-already-issued");
                var now = time.GetUtcNow(); await held!.Current(db,factory,time,version,lease,ct);
                var assessment = await new ServicingDraftService(factory,time).Assess(db,held.Scope.Source.Scope.Actor,held.Scope.Draft,
                    ServicingProposalInput.Parse(held.Scope.Revision.ProposalJson,held.Scope.Draft.BaseVersionId),ct);
                if (assessment.ReadinessIssues.Count!=0 || (held.Scope.Renewal is not null?1:assessment.Slices.Count)!=held.Input.Slices.Count)
                    throw new QuoteOperationException(409,"servicing-issue-proposal-stale");
                if (held.Rating.Id!=input.RatingId || held.Cycle.CurrentAcceptanceId!=input.AcceptanceId || held.Cycle.CurrentTermsVersionId!=input.TermsVersionId)
                    throw new QuoteOperationException(412,"servicing-issue-acceptance-stale");
                var terms = new ServicingTermsService(factory,time);
                var accepted = await db.Set<ServicingAcceptance>().AsNoTracking().SingleAsync(x=>x.Id==input.AcceptanceId,ct);
                var assurance = await ServicingTermsService.Assurance(db,held,ct);
                if (input.TermsHash!=accepted.TermsHash || input.AssuranceHash!=assurance || !await terms.AcceptanceCurrent(db,held,accepted,assurance,now,ct))
                    throw new QuoteOperationException(409,"servicing-issue-acceptance-stale");
                var templates = await PolicyIssueWriter.Templates(db,held.Cycle.ProductId,now,ct);
                return await ServicingIssueWriter.Write(db,held,grant!,accepted,input.Reason,templates,now,correlation,ct);
            },token);
    }

    private static async Task ReplayAuthority(BackOfficeDbContext db,OwnedQuoteScope source,ServicingDraft draft,DateTimeOffset now,CancellationToken token)
    {
        var decision = await (from t in db.Set<PolicyTransaction>() join d in db.Set<ServicingIssueDecision>() on t.ServicingIssueDecisionId equals d.Id
            where t.Id==draft.IssuedTransactionId && d.DraftId==draft.Id select d).AsNoTracking().SingleAsync(token);
        var cycle = await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==decision.CycleId,token);
        var input = ServicingRatingInput.Read(cycle.InputJson,cycle.InputHash);
        var binder = await db.Set<BinderVersion>().FromSqlInterpolated($"SELECT * FROM BinderVersion WITH(HOLDLOCK) WHERE Id={cycle.BinderVersionId}").AsNoTracking().SingleAsync(token);
        var product = await db.Set<Product>().Where(x=>x.Id==cycle.ProductId).Select(x=>x.Code).SingleAsync(token);
        var grants = await QuoteUnderwritingScope.GrantsAsync(db,source,cycle.ProductVersionId,binder,product,
            input.Term with { Kind="short-period",StartsAt=decision.EffectiveAt },now,token);
        if (!grants.Any(x=>x.Grant.Id==decision.GrantId && x.Version.Id==decision.AuthorityVersionId))
            throw new QuoteOperationException(403,"servicing-issue-authority-required");
    }
}
