using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal static class ServicingIssueWriter
{
    internal static async Task<CommandOutcome> Write(BackOfficeDbContext db,ServicingDecisionContext held,EffectiveUnderwritingGrant grant,
        ServicingAcceptance acceptance,string reason,TemplateVersion[] templates,DateTimeOffset now,Guid correlation,CancellationToken token)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Servicing issue requires one held graph transaction.");
        var draft=held.Scope.Draft;var actor=held.Scope.Source.Scope.Actor.UserId;var cycle=held.Cycle;var term=held.Scope.Term;
        var decision=new ServicingIssueDecision{DraftId=draft.Id,PolicyId=draft.PolicyId,BaseTermId=draft.BaseTermId,BaseVersionId=draft.BaseVersionId,
            RevisionId=cycle.RevisionId,CycleId=cycle.Id,RatingId=held.Rating.Id,TermsVersionId=acceptance.TermsVersionId,AcceptanceId=acceptance.Id,
            ActorId=actor,GrantId=grant.Grant.Id,AuthorityVersionId=grant.Version.Id,InputHash=cycle.InputHash,TermsHash=acceptance.TermsHash,
            AssuranceHash=acceptance.AssuranceHash,EffectiveAt=held.Input.Slices[0].EffectiveAt,Reason=reason,CreatedAt=now,CreatedBy=actor};
        db.Add(decision);await db.SaveChangesAsync(token);
        var sequence=checked(await db.Set<PolicyTransaction>().Where(x=>x.TermId==term.Id).MaxAsync(x=>x.Sequence,token)+1);
        var transaction=new PolicyTransaction{PolicyId=draft.PolicyId,TermId=term.Id,SourceQuoteId=held.Scope.Source.Quote.Id,Kind="adjustment",Sequence=sequence,
            ServicingDraftId=draft.Id,ServicingRevisionId=cycle.RevisionId,ServicingCycleId=cycle.Id,ServicingRatingId=held.Rating.Id,
            ServicingAcceptanceId=acceptance.Id,ServicingIssueDecisionId=decision.Id,EffectiveAt=decision.EffectiveAt,ProcessedAt=now,Reason=reason,
            OperationKey="servicing-issue/"+draft.Id.ToString("N"),CreatedAt=now,CreatedBy=actor};
        db.Add(transaction);await db.SaveChangesAsync(token);
        var baseSequence=await db.Set<PolicyVersion>().Where(x=>x.TermId==term.Id).MaxAsync(x=>x.Sequence,token);
        var slices=ServicingEvidenceProjection.Slices(held);
        var rating=JsonSerializer.Deserialize<ServicingRatingOutcome>(held.Rating.ResultJson,ServicingRatingService.Json)?.Rating
            ??throw new QuoteOperationException(409,"servicing-rating-input-unavailable");
        var terms=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==acceptance.TermsVersionId,token);
        using var contract=JsonDocument.Parse(terms.TermsJson);
        var versions=new List<PolicyVersion>();
        for(var i=0;i<slices.Count;i++)
        {
            var snapshot=ServicingIssueSnapshot.Create(held,decision,transaction,slices,rating,i,contract.RootElement);
            var version=new PolicyVersion{PolicyId=draft.PolicyId,TermId=term.Id,TransactionId=transaction.Id,Sequence=checked(baseSequence+i+1),SliceOrdinal=i+1,
                SnapshotJson=snapshot,ContentHash=Hash(snapshot),EffectiveAt=slices[i].EffectiveAt,ProcessedAt=now,CreatedAt=now,CreatedBy=actor};
            db.Add(version);await db.SaveChangesAsync(token);versions.Add(version);
            using var parsed=JsonDocument.Parse(snapshot);
            foreach(var vehicle in parsed.RootElement.GetProperty("risk").TryGetProperty("vehicles",out var vehicles)?vehicles.EnumerateArray().ToArray():[])
                db.Add(new PolicyRegistration{PolicyId=draft.PolicyId,VersionId=version.Id,RiskItemId=vehicle.GetProperty("id").GetGuid(),
                    NormalizedRegistration=vehicle.GetProperty("registration").GetString()!.Replace(" ","").Replace("-","").ToUpperInvariant()});
            await db.SaveChangesAsync(token);
        }
        var financial=await ServicingPostingService.WriteAsync(db,transaction.Id,token);
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==draft.PolicyId,token);
        var documents=new List<Guid>();var midIntents=new List<Guid>();
        foreach(var version in versions)
        {
            foreach(var template in templates)
            {
                var document=new PolicyDocumentRequest{PolicyId=policy.Id,TermId=term.Id,TransactionId=transaction.Id,VersionId=version.Id,Kind=template.Kind,
                    Purpose="adjustment",TemplateVersionId=template.Id,CreatedAt=now,UpdatedAt=now,CreatedBy=actor};
                document.PayloadJson=JsonSerializer.Serialize(new{format="policy-document-1",requestId=document.Id,policyId=policy.Id,policyReference=policy.Reference,
                    termId=term.Id,transactionId=transaction.Id,versionId=version.Id,contentHash=Convert.ToHexStringLower(version.ContentHash),
                    kind=document.Kind,templateVersionId=template.Id,template=JsonSerializer.Deserialize<JsonElement>(template.ContentJson),
                    snapshot=JsonSerializer.Deserialize<JsonElement>(version.SnapshotJson)},ServicingRatingService.Json);
                document.PayloadHash=Hash(document.PayloadJson);
                var work=new OutboxWork{Kind="policy-document",OperationKey="policy-document/"+document.Id.ToString("N"),SubjectRecordId=document.Id,
                    Payload=document.PayloadJson,NextAttemptAt=now,CreatedAt=now,CreatedBy=actor,CorrelationId=correlation};
                db.Add(work);await db.SaveChangesAsync(token);document.WorkId=work.Id;db.Add(document);await db.SaveChangesAsync(token);documents.Add(document.Id);
            }
            var mid=new PolicyMidIntent{PolicyId=policy.Id,TermId=term.Id,TransactionId=transaction.Id,VersionId=version.Id,CreatedAt=now,CreatedBy=actor};
            mid.PayloadJson=JsonSerializer.Serialize(new{format="policy-mid-intent-1",intentId=mid.Id,policyId=policy.Id,termId=term.Id,versionId=version.Id,
                contentHash=Convert.ToHexStringLower(version.ContentHash),action="change",effectiveAt=version.EffectiveAt,endsAt=term.EndsAt},ServicingRatingService.Json);
            mid.PayloadHash=Hash(mid.PayloadJson);
            var midWork=new OutboxWork{Kind="mid-update",OperationKey="mid-update/"+mid.Id.ToString("N"),SubjectRecordId=mid.Id,
                Payload=mid.PayloadJson,NextAttemptAt=now,CreatedAt=now,CreatedBy=actor,CorrelationId=correlation};
            db.Add(midWork);await db.SaveChangesAsync(token);mid.WorkId=midWork.Id;db.Add(mid);await db.SaveChangesAsync(token);midIntents.Add(mid.Id);
        }
        var lease=await db.Set<ServicingLease>().SingleAsync(x=>x.DraftId==draft.Id,token);lease.Active=false;lease.ExpiresAt=now;
        // This pointer tracks the newest immutable source for future drafts;
        // ordinary reads and registration discovery select by effective time.
        term.CurrentVersionId=versions[^1].Id;
        await db.SaveChangesAsync(token);
        draft.IssuedTransactionId=transaction.Id;draft.State="issued";
        db.Add(new ClientActivity{ClientId=policy.ClientId,RelationshipId=policy.RelationshipId,RecordKind="quote",RecordId=policy.SourceQuoteId,
            EventType="policy.adjustment-issued",ActorId=actor,CreatedBy=actor,CreatedAt=now,OccurredAt=now});
        db.Add(new AuditEvent{SubjectRecordId=policy.Id,ActorId=actor,CreatedBy=actor,CreatedAt=now,OccurredAt=now,EventType="policy.adjustment-issued",Reason=reason,
            CorrelationId=correlation,After=JsonSerializer.Serialize(new{draftId=draft.Id,transactionId=transaction.Id,decisionId=decision.Id,versionIds=versions.Select(x=>x.Id).ToArray()})});
        await db.SaveChangesAsync(token);
        var etag="\""+Convert.ToBase64String(draft.RowVersion)+"\"";
        var body=JsonSerializer.Serialize(new{policyId=policy.Id,policyReference=policy.Reference,draftId=draft.Id,draftEtag=etag,termId=term.Id,
            transactionId=transaction.Id,decisionId=decision.Id,versionId=versions[0].Id,versionIds=versions.Select(x=>x.Id).ToArray(),
            obligationId=financial.ObligationId,journalId=financial.JournalId,accountingPeriodId=financial.AccountingPeriodId,postingDate=financial.PostingDate,
            currency="GBP",amountDue=PolicyIssueWriter.Money(Math.Max(0,financial.InvoiceDue)),amountCredit=PolicyIssueWriter.Money(Math.Max(0,-financial.InvoiceDue)),
            netAmount=PolicyIssueWriter.Money(financial.InvoiceDue),documentRequestIds=documents.ToArray(),midIntentIds=midIntents.ToArray(),processedAt=now},ServicingRatingService.Json);
        return new(transaction.Id,201,body,Etag:etag);
    }

    private static byte[] Hash(string json)=>SHA256.HashData(Encoding.UTF8.GetBytes(json));
}
