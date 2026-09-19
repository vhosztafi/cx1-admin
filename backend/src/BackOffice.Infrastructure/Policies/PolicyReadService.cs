using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class PolicyReadService(IDbContextFactory<BackOfficeDbContext> factory, TimeProvider? time = null)
{
    public Task<Dictionary<string, object>> ReadAsync(ActorContext actor, Guid policyId, Guid? termId = null, Guid? versionId = null, Guid? transactionId = null, Guid? obligationId = null, CancellationToken token = default)
        => ReadCoreAsync(actor, policyId, termId, versionId, transactionId, obligationId, null, null, token);

    public Task<Dictionary<string, object>> ReadAtAsync(ActorContext actor, Guid policyId, DateTimeOffset effectiveAt,
        DateTimeOffset knownAt, Guid? termId = null, CancellationToken token = default)
        => ReadCoreAsync(actor, policyId, termId, null, null, null, effectiveAt, knownAt, token);

    public async Task<Dictionary<string, object>> ReadTermAtAsync(ActorContext actor, Guid termId, DateTimeOffset effectiveAt,
        DateTimeOffset knownAt, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var policyId = await db.Set<PolicyTerm>().AsNoTracking().Where(x => x.Id == termId).Select(x => (Guid?)x.PolicyId).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "policy-term-not-found");
        return await ReadAtAsync(actor, policyId, effectiveAt, knownAt, termId, token);
    }

    private async Task<Dictionary<string, object>> ReadCoreAsync(ActorContext actor, Guid policyId, Guid? termId,
        Guid? versionId, Guid? transactionId, Guid? obligationId, DateTimeOffset? effectiveAt, DateTimeOffset? knownAt, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token); await using var tx = await db.Database.BeginTransactionAsync(token);
        var policy = await PolicyScope.Hold(db, actor, policyId, token);
        var now = (time ?? TimeProvider.System).GetUtcNow();
        var effective = effectiveAt ?? now; var known = knownAt ?? now;
        if (termId is Guid requiredTerm && !await db.Set<PolicyTerm>().AnyAsync(x => x.Id == requiredTerm && x.PolicyId == policy.Id, token))
            throw new QuoteOperationException(404, "policy-term-not-found");
        PolicyTemporalSelection? selection = null;
        if (versionId == null && transactionId == null && obligationId == null)
        {
            var candidates = await PolicyTemporalSelector.Candidates(db, policy.Id, known).ToArrayAsync(token);
            selection = PolicyTemporalSelector.Select(candidates, policy.Id, effective, known, termId);
            if (selection is null)
            {
                await tx.CommitAsync(token);
                return new() { ["id"] = policy.Id, ["coverageState"] = "not-covered", ["effectiveCutoff"] = effective, ["knownCutoff"] = known };
            }
            termId = selection.Candidate.TermId; versionId = selection.Candidate.VersionId;
        }
        if (obligationId is Guid requestedObligation)
        {
            var historical = await db.Set<IssueFinancialObligation>().AsNoTracking()
                .Where(x => x.Id == requestedObligation && x.PolicyId == policy.Id).Select(x => new { x.TransactionId }).SingleOrDefaultAsync(token)
                ?? throw new QuoteOperationException(404, "policy-obligation-not-found");
            transactionId = historical.TransactionId;
        }
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleOrDefaultAsync(x => x.PolicyId == policy.Id && x.Id == (termId ?? policy.CurrentTermId), token)
            ?? throw new QuoteOperationException(404, "policy-term-not-found");
        var versions = db.Set<PolicyVersion>().AsNoTracking().Where(x => x.PolicyId == policy.Id && x.TermId == term.Id);
        var version = await (transactionId is Guid transaction ? versions.Where(x => x.TransactionId == transaction && x.SliceOrdinal == 1) : versions.Where(x => x.Id == (versionId ?? term.CurrentVersionId))).SingleOrDefaultAsync(token)
            ?? throw new QuoteOperationException(404, "policy-version-not-found");
        var issued = await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x => x.Id == version.TransactionId && x.PolicyId == policy.Id && x.TermId == term.Id, token);
        var obligation = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x => x.TransactionId == issued.Id && x.PolicyId == policy.Id, token);
        if (obligationId is Guid expected && expected != obligation.Id) throw new QuoteOperationException(404, "policy-obligation-not-found");
        var journal = await db.Set<Journal>().AsNoTracking().SingleAsync(x => x.ObligationId == obligation.Id && x.PostedAt != null, token);
        var lines = await db.Set<JournalLine>().AsNoTracking().Where(x => x.JournalId == journal.Id).OrderBy(x => x.ComponentCode).ThenBy(x => x.AccountCode).ToArrayAsync(token);
        var documents = await db.Set<PolicyDocumentRequest>().AsNoTracking().Where(x => x.VersionId == version.Id).OrderBy(x => x.Kind).ToArrayAsync(token);
        string Money(decimal n) => PolicyIssueWriter.Money(n);
        var result = new Dictionary<string, object> { ["id"] = policy.Id, ["reference"] = policy.Reference, ["sourceQuoteId"] = policy.SourceQuoteId,
            ["clientId"] = policy.ClientId, ["relationshipId"] = policy.RelationshipId, ["agencyId"] = policy.AgencyId, ["termId"] = term.Id, ["versionId"] = version.Id,
            ["transactionId"] = issued.Id, ["issuedAt"] = issued.ProcessedAt, ["snapshot"] = JsonSerializer.Deserialize<JsonElement>(version.SnapshotJson),
            ["effectiveCutoff"] = effective, ["knownCutoff"] = known,
            ["coverageState"] = selection?.State ?? (effective < term.StartsAt ? "scheduled" : issued.Kind == "cancellation" ? effective >= version.EffectiveAt ? "cancelled" : "scheduled" : effective >= term.EndsAt ? "expired" : "active"),
            ["contentHash"] = Convert.ToHexStringLower(version.ContentHash), ["effectiveAt"] = issued.EffectiveAt, ["reason"] = issued.Reason,
            ["termNumber"] = term.Number, ["versionSequence"] = version.Sequence, ["transactionSequence"] = issued.Sequence,
            ["financials"] = new { obligationId = obligation.Id, transactionId = issued.Id, journalId = journal.Id, currency = "GBP", debtorKind = obligation.DebtorKind,
                debtorId = obligation.DebtorAgencyId ?? obligation.DebtorRelationshipId!.Value, amountDue = Money(obligation.InvoiceDue), premium = Money(obligation.Premium), tax = Money(obligation.Tax),
                fee = Money(obligation.Fee), brokerCommission = Money(obligation.Commission), brokerFeeShare = Money(obligation.FeeShare), insurerPayable = Money(obligation.Premium + obligation.Tax - obligation.Commission),
                retainedFeeIncome = Money(obligation.Fee - obligation.FeeShare), brokerRemunerationPayable = Money(obligation.BrokerPayable),
                lines = lines.Select(x => new { accountCode = x.AccountCode, side = x.Debit > 0 ? "debit" : "credit", amount = Money(x.Debit + x.Credit), componentCode = x.ComponentCode }).ToArray() },
            ["documentRequests"] = documents.Select(x => new { id = x.Id, versionId = x.VersionId, templateVersionId = x.TemplateVersionId, kind = x.Kind, state = x.State }).ToArray() };
        if(issued.Kind=="cancellation")
        {
            var decision=await db.Set<CancellationIssueDecision>().AsNoTracking().SingleAsync(x=>x.Id==issued.CancellationIssueDecisionId && x.PolicyId==policy.Id,token);
            result["cancellationDecisionId"]=decision.Id;result["cancellationApprovalId"]=decision.ApprovalId;result["cancellationPreviewId"]=decision.PreviewId;
        }
        else
        {
            result["sourceCycleId"]=issued.CycleId??issued.ServicingCycleId??throw new InvalidOperationException("Missing policy decision source.");
            result["ratingId"]=issued.RatingId??issued.ServicingRatingId??throw new InvalidOperationException("Missing policy rating source.");
            result["acceptanceId"]=issued.AcceptanceId??issued.ServicingAcceptanceId??throw new InvalidOperationException("Missing policy acceptance source.");
        }
        await tx.CommitAsync(token); return result;
    }
}
