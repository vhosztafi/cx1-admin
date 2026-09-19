using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyVersionComparison(Guid PolicyId,Guid BeforeVersionId,Guid AfterVersionId,string BeforeHash,string AfterHash,IReadOnlyList<QuoteRevisionChange> Changes);
public sealed record PolicyHistoryDocument(Guid Id,string Kind,string State);
public sealed record PolicyHistoryVersion(Guid Id,Guid TermId,Guid TransactionId,int TermNumber,int VersionSequence,int TransactionSequence,int SliceOrdinal,
    string Kind,DateTimeOffset EffectiveAt,DateTimeOffset ProcessedAt,string ContentHash,string Reason,Guid? ActorId,string Applicability,
    string ActorLabel,Guid ObligationId,string AmountDue,IReadOnlyList<PolicyHistoryDocument> DocumentRequests,string ProviderName,Guid? SourceDraftId,
    string DecisionBinder,string DecisionAuthority);
public sealed record PolicyHistoryView(Guid PolicyId,string Reference,DateTimeOffset EffectiveAt,DateTimeOffset KnownAt,
    Guid? SelectedVersionId,string CoverageState,IReadOnlyList<PolicyHistoryVersion> Versions,string PolicyEtag,string AgencyName);

public sealed partial class PolicyHistoryService(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    public async Task<PolicyVersionComparison> CompareAsync(ActorContext actor,Guid policyId,Guid beforeId,Guid afterId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        await using var tx=await db.Database.BeginTransactionAsync(token);
        await PolicyScope.Hold(db,actor,policyId,token);
        var ids=new[]{beforeId,afterId}.Distinct().ToArray();
        var versions=await db.Set<PolicyVersion>().AsNoTracking().Where(x=>x.PolicyId==policyId&&ids.Contains(x.Id)).ToArrayAsync(token);
        if(versions.Length!=ids.Length)throw new QuoteOperationException(404,"policy-version-not-found");
        var before=versions.Single(x=>x.Id==beforeId);var after=versions.Single(x=>x.Id==afterId);
        using var left=JsonDocument.Parse(before.SnapshotJson);using var right=JsonDocument.Parse(after.SnapshotJson);
        var result=new PolicyVersionComparison(policyId,beforeId,afterId,Convert.ToHexStringLower(before.ContentHash),Convert.ToHexStringLower(after.ContentHash),
            PolicyHistoryRules.Compare(left.RootElement,right.RootElement));
        await tx.CommitAsync(token);return result;
    }

    public async Task<PolicyHistoryView> ReadAsync(ActorContext actor,Guid policyId,DateTimeOffset? effectiveAt=null,DateTimeOffset? knownAt=null,CancellationToken token=default,Guid? termId=null)
    {
        var effective=effectiveAt??time.GetUtcNow();var known=knownAt??time.GetUtcNow();
        if(effective.Offset!=TimeSpan.Zero||known.Offset!=TimeSpan.Zero)throw new QuoteOperationException(400,"policy-cutoffs-must-be-utc");
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var policy=await PolicyScope.Hold(db,actor,policyId,token);
        var rows=await (from v in db.Set<PolicyVersion>().AsNoTracking()
            join term in db.Set<PolicyTerm>() on new{v.TermId,v.PolicyId} equals new{TermId=term.Id,term.PolicyId}
            join transaction in db.Set<PolicyTransaction>() on new{v.TransactionId,v.TermId,v.PolicyId} equals new{TransactionId=transaction.Id,transaction.TermId,transaction.PolicyId}
            where v.PolicyId==policyId && (termId==null || v.TermId==termId) orderby term.Number descending,transaction.Sequence descending,v.SliceOrdinal descending
            select new{Version=new{v.Id,v.Sequence,v.SliceOrdinal,v.EffectiveAt,v.ProcessedAt,v.ContentHash},
                Term=new{term.Id,term.Number,term.StartsAt,term.EndsAt},
                Transaction=new{transaction.Id,transaction.Sequence,transaction.Kind,transaction.ProcessedAt,transaction.Reason,transaction.CreatedBy,transaction.ServicingDraftId}}).ToArrayAsync(token);
        var actorIds=rows.Where(x=>x.Transaction.CreatedBy!=null).Select(x=>x.Transaction.CreatedBy!.Value).Distinct().ToArray();
        var actors=await db.Set<StaffUser>().AsNoTracking().Where(x=>actorIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,token);
        var obligations=await db.Set<IssueFinancialObligation>().AsNoTracking().Where(x=>x.PolicyId==policyId)
            .Select(x=>new{x.Id,x.TransactionId,x.InvoiceDue,x.ProviderId}).ToDictionaryAsync(x=>x.TransactionId,token);
        var providerIds=obligations.Values.Select(x=>x.ProviderId).Distinct().ToArray();
        var providers=await db.Set<CapacityProvider>().AsNoTracking().Where(x=>providerIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Name,token);
        var agencyName=await db.Set<Agency>().Where(x=>x.Id==policy.AgencyId).Select(x=>x.LegalName).SingleAsync(token);
        var decisions=await DecisionLabelsAsync(db,policyId,token);
        var documents=await db.Set<PolicyDocumentRequest>().AsNoTracking().Where(x=>x.PolicyId==policyId)
            .Select(x=>new{x.Id,x.VersionId,x.Kind,x.State}).ToArrayAsync(token);
        var documentGroups=documents.ToLookup(x=>x.VersionId);
        var candidates=rows.Select(x=>new PolicyTemporalCandidate(policyId,x.Term.Id,x.Version.Id,x.Term.StartsAt,x.Term.EndsAt,x.Version.EffectiveAt,
            x.Version.ProcessedAt>x.Transaction.ProcessedAt?x.Version.ProcessedAt:x.Transaction.ProcessedAt,x.Transaction.Sequence,x.Version.SliceOrdinal,x.Transaction.Kind)).ToArray();
        var selected=PolicyTemporalSelector.Select(candidates,policyId,effective,known);
        var versions=rows.Select(x=>new PolicyHistoryVersion(x.Version.Id,x.Term.Id,x.Transaction.Id,x.Term.Number,x.Version.Sequence,x.Transaction.Sequence,x.Version.SliceOrdinal,
            x.Transaction.Kind,x.Version.EffectiveAt,x.Version.ProcessedAt,Convert.ToHexStringLower(x.Version.ContentHash),x.Transaction.Reason,x.Transaction.CreatedBy,
            x.Version.ProcessedAt>known||x.Transaction.ProcessedAt>known?"not-yet-known":x.Version.Id==selected?.Candidate.VersionId?"selected":
            x.Version.EffectiveAt>effective?"not-yet-effective":x.Term.Id!=selected?.Candidate.TermId?"different-term":"superseded",
            x.Transaction.CreatedBy is Guid actorId&&actors.TryGetValue(actorId,out var actorName)?actorName:"System",
            obligations[x.Transaction.Id].Id,PolicyIssueWriter.Money(obligations[x.Transaction.Id].InvoiceDue),
            documentGroups[x.Version.Id].Select(d=>new PolicyHistoryDocument(d.Id,d.Kind,d.State)).ToArray(),
            providers[obligations[x.Transaction.Id].ProviderId],x.Transaction.ServicingDraftId,decisions[x.Transaction.Id].Binder,decisions[x.Transaction.Id].Authority)).ToArray();
        var result=new PolicyHistoryView(policyId,policy.Reference,effective,known,selected?.Candidate.VersionId,selected?.State??"not-covered",versions,Etag(policy.RowVersion),agencyName);
        await tx.CommitAsync(token);return result;
    }
    private static string Etag(byte[] value)=>"\""+Convert.ToBase64String(value)+"\"";
}
