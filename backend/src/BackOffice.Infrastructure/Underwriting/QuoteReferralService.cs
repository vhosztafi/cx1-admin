using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;

public sealed record ReferralDecisionInput(Guid ReferralId, byte[] Version, string Outcome, string Reason, IReadOnlyList<JsonElement> Conditions, string? Question = null);

public sealed partial class QuoteReferralService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider time)
{
    private readonly SqlCommandBoundary commands = new(factory, time);

    public Task<CommandOutcome> DecideAsync(ActorContext actor, Guid quoteId, Guid cycleId, byte[] version,
        IReadOnlyList<ReferralDecisionInput> decisions, string key, Guid correlationId, CancellationToken token = default, Guid? routeReferralId = null)
    {
        if (decisions.Count is < 1 or > 50 || decisions.Select(x => x.ReferralId).Distinct().Count() != decisions.Count ||
            decisions.Any(x => x.ReferralId == Guid.Empty || x.Version.Length != 8 || x.Outcome is not ("approve" or "approve-with-conditions" or "query" or "decline" or "reopen")))
            throw new QuoteOperationException(422, "referral-decision-invalid");
        if (routeReferralId is Guid route && (decisions.Count != 1 || decisions[0].ReferralId != route)) throw new QuoteOperationException(422, "referral-route-mismatch");
        var normalized = decisions.Select(x => x with { Reason = QuoteRatingService.Reason(x.Reason), Question = x.Question is null ? null : QuoteRatingService.Reason(x.Question) }).ToArray();
        foreach (var item in normalized)
            if ((item.Outcome is "approve-with-conditions" or "query" ? item.Conditions.Count is < 1 or > 20 : item.Conditions.Count != 0) || (item.Outcome == "query") != (item.Question is not null))
                throw new QuoteOperationException(422, "referral-conditions-required");
        UnderwritingDecisionContext? held = null;
        var selected = new Dictionary<Guid, (QuoteReferral Row, EffectiveUnderwritingGrant Grant, ReferralCondition[] Conditions)>();
        var routeName = routeReferralId is Guid single ? $"/api/v1/referrals/{single:D}/decisions" : $"/api/v1/quotes/{quoteId:D}/referral-decisions";
        return commands.ExecuteAuthorizedAsync(new(actor.UserId, routeName, key, correlationId), new { quoteId, cycleId, version = Convert.ToBase64String(version), decisions = normalized }, "underwriting.referrals-decided",
            async (db, ct) =>
            {
                held = await UnderwritingDecisionContext.Hold(db, actor, quoteId, cycleId, "underwriting-decide-within-authority", time.GetUtcNow(), true, ct);
                using var proposal = JsonDocument.Parse(held.Revision.ProposalJson);
                var active = await UnderwritingEvidenceService.ActiveConditions(db, cycleId, ct);
                var retainedConditions = active.Where(x => !normalized.Any(n => n.ReferralId == x.ReferralId)).Select(x => Parse(x.DefinitionJson, proposal.RootElement)).ToArray();
                if (retainedConditions.Length + normalized.Sum(x => x.Conditions.Count) > 100) throw new QuoteOperationException(422, "underwriting-condition-limit");
                foreach (var item in normalized.OrderBy(x => x.ReferralId))
                {
                    var referral = await db.Set<QuoteReferral>().FromSqlInterpolated($"SELECT * FROM QuoteReferral WITH(UPDLOCK,HOLDLOCK) WHERE Id={item.ReferralId} AND CycleId={cycleId} AND QuoteId={quoteId}").SingleOrDefaultAsync(ct)
                        ?? throw new QuoteOperationException(404, "referral-not-found");
                    ReferralCondition[] conditions;
                    try { conditions = item.Conditions.Select(x => ReferralRules.Condition(x, proposal.RootElement)).ToArray(); }
                    catch (ArgumentException) { throw new QuoteOperationException(422, "referral-condition-invalid"); }
                    if (conditions.Select(x => x.DefinitionJson).Distinct().Count() != conditions.Length) throw new QuoteOperationException(422, "duplicate-referral-condition");
                    // Exact prepared-terms ownership arrives with06-08. No supplied
                    // GUID can stand in for a terms record before that handler exists.
                    if (conditions.Any(x => x.TermsVersionId is not null)) throw new QuoteOperationException(409, "prepared-terms-required");
                    if (item.Outcome == "query" && conditions.Any(x => x.Kind != "documentary")) throw new QuoteOperationException(422, "query-documentary-condition-required");
                    var approval = item.Outcome is "approve" or "approve-with-conditions";
                    var revisionRequired = item.Outcome == "approve-with-conditions" && conditions.Any(x => x.Kind == "risk-change");
                    // A request to revise risk remains permanently outstanding on
                    // this cycle. It never grants an approval above authority.
                    var grant = held.Grants.FirstOrDefault(x => !approval || revisionRequired ||
                        ReferralRules.AuthorityBlockers(x.Definition, held.Eligible.Binder, held.Risk, retainedConditions.Concat(conditions).ToArray()).Count == 0)
                        ?? throw new QuoteOperationException(403, "underwriting-dimension-authority-required");
                    selected.Add(item.ReferralId, (referral, grant, conditions));
                }
            },
            async (db, ct) =>
            {
                var now = time.GetUtcNow(); held!.Current(version, now, currentPrice: true);
                // Validate every selected child and prerequisite before any write.
                var oldConditions = await UnderwritingEvidenceService.ActiveConditions(db, cycleId, ct);
                foreach (var item in normalized)
                {
                    var row = selected[item.ReferralId].Row; UnderwritingDecisionContext.CheckVersion(row.RowVersion, item.Version, "stale-referral");
                    if (row.State == "superseded" || item.Outcome != "reopen" && row.State == "declined") throw new QuoteOperationException(409, "referral-reopen-required");
                    if (item.Outcome == "approve")
                    {
                        foreach (var condition in oldConditions.Where(x => x.ReferralId == row.Id))
                            if (!await UnderwritingEvidenceService.Resolved(db, condition, ct)) throw new QuoteOperationException(409, "referral-condition-outstanding");
                        if (row.RuleCode == "UW-22" && !(await UnderwritingEvidenceService.Requirements(db, held.Cycle, held.Revision, held.Input, ct)).Any(x => x.Code == "trading-history" && x.Satisfied))
                            throw new QuoteOperationException(409, "trading-history-review-required");
                    }
                }
                Guid firstDecision = Guid.Empty;
                foreach (var item in normalized)
                {
                    var selection = selected[item.ReferralId];
                    var decision = new QuoteReferralDecision { QuoteId = quoteId, CycleId = cycleId, ReferralId = item.ReferralId,
                        Sequence = checked((await db.Set<QuoteReferralDecision>().Where(x => x.ReferralId == item.ReferralId).MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1),
                        Outcome = item.Outcome, Reason = item.Reason, Question = item.Question, ConditionsJson = JsonSerializer.Serialize(item.Conditions),
                        ActorId = actor.UserId, AuthorityVersionId = selection.Grant.Version.Id, DecidedAt = now, CreatedAt = now, CreatedBy = actor.UserId };
                    db.Add(decision); await db.SaveChangesAsync(ct); if (firstDecision == Guid.Empty) firstDecision = decision.Id;
                    selection.Row.LatestDecisionId = decision.Id;
                    selection.Row.State = item.Outcome switch { "approve" => "approved", "approve-with-conditions" => "conditional", "query" => "queried", "decline" => "declined", _ => "open" };
                    var number = 0;
                    foreach (var condition in selection.Conditions) db.Add(new QuoteCondition { DecisionId = decision.Id, ReferralId = item.ReferralId, CycleId = cycleId, QuoteId = quoteId,
                        Sequence = ++number, Code = condition.Code, Kind = condition.Kind, DefinitionJson = condition.DefinitionJson, Wording = condition.Wording,
                        EndorsementCode = condition.EndorsementCode, CreatedAt = now, CreatedBy = actor.UserId, UpdatedAt = now });
                }
                await db.SaveChangesAsync(ct); await RefreshState(db, held, ct);
                return await held.Receipt(db, firstDecision, 200, "underwriting.referrals-decided", now, ct);
            }, token);
    }

    internal static ReferralCondition Parse(string json, JsonElement proposal)
    { using var document = JsonDocument.Parse(json); return ReferralRules.Condition(document.RootElement, proposal); }

    internal static async Task RefreshState(BackOfficeDbContext db, UnderwritingDecisionContext held, CancellationToken token)
    {
        var referrals = await db.Set<QuoteReferral>().Where(x => x.CycleId == held.Cycle.Id && x.State != "superseded").ToArrayAsync(token);
        foreach (var referral in referrals.Where(x => x.LatestDecisionId is not null && x.State is "conditional" or "approved"))
        {
            var decision = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(x => x.Id == referral.LatestDecisionId, token);
            if (decision.Outcome != "approve-with-conditions") continue;
            var conditions = await db.Set<QuoteCondition>().AsNoTracking().Where(x => x.DecisionId == decision.Id).ToArrayAsync(token);
            var complete = conditions.Length > 0;
            foreach (var condition in conditions) complete &= await UnderwritingEvidenceService.Resolved(db, condition, token);
            referral.State = complete ? "approved" : "conditional";
        }
        var quote = held.Owned.Quote; if (db.Entry(quote).State == EntityState.Detached) db.Attach(quote);
        // Sending/acceptance are versioned by later slices; assurance changes
        // deliberately return an unbound quote to the current decision status.
        quote.State = referrals.Any(x => x.State == "declined") ? "declined" : referrals.Any(x => x.State != "approved") ? "referred" : referrals.Length > 0 ? "approved" : "rated";
    }
}
