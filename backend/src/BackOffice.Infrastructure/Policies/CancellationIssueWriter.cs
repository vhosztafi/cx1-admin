using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

internal static class CancellationIssueWriter
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private static byte[] Hash(string json)=>SHA256.HashData(Encoding.UTF8.GetBytes(json));
    internal static async Task<CommandOutcome> Write(BackOfficeDbContext db,HeldCancellation held,CancellationIssueDecision decision,
        CancellationReviewView preview,DateTimeOffset now,Guid correlation,CancellationToken token)
    {
        var draft=held.Draft;var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==draft.PolicyId,token);
        var transaction=new PolicyTransaction{PolicyId=policy.Id,TermId=held.Term.Id,SourceQuoteId=policy.SourceQuoteId,Kind="cancellation",
            Sequence=checked(await db.Set<PolicyTransaction>().Where(x=>x.TermId==held.Term.Id).MaxAsync(x=>x.Sequence,token)+1),
            ServicingDraftId=draft.Id,ServicingRevisionId=decision.RevisionId,CancellationIssueDecisionId=decision.Id,
            EffectiveAt=decision.EffectiveAt,ProcessedAt=now,Reason=decision.Reason,OperationKey="cancellation-issue/"+draft.Id.ToString("N"),
            CreatedAt=now,CreatedBy=decision.ActorId};
        db.Add(transaction);await db.SaveChangesAsync(token);
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==draft.BaseVersionId,token);
        using var source=JsonDocument.Parse(basis.SnapshotJson);
        var snapshot=CancellationIssueSnapshot.Create(source.RootElement,new(policy.SourceQuoteId,decision.Id,decision.ApprovalId,decision.PreviewId,
            decision.BaseVersionId,decision.RevisionId,transaction.Id,decision.EffectiveAt,now,Convert.ToHexStringLower(decision.PreviewHash),preview.ReasonCode,preview.RuleVersion));
        var version=new PolicyVersion{PolicyId=policy.Id,TermId=held.Term.Id,TransactionId=transaction.Id,SliceOrdinal=1,
            Sequence=checked(await db.Set<PolicyVersion>().Where(x=>x.TermId==held.Term.Id).MaxAsync(x=>x.Sequence,token)+1),
            SnapshotJson=snapshot,ContentHash=Hash(snapshot),EffectiveAt=decision.EffectiveAt,ProcessedAt=now,CreatedAt=now,CreatedBy=decision.ActorId};
        db.Add(version);await db.SaveChangesAsync(token);
        var financial=await CancellationPostingService.Write(db,transaction,decision,token);
        if(financial.InvoiceDue!=preview.Amounts!.Posting.InvoiceDue)throw new InvalidOperationException("Independent cancellation posting differs from reviewed preview.");
        var contacts=await db.Set<Contact>().FromSqlInterpolated($"SELECT * FROM Contact WITH(HOLDLOCK) WHERE ClientId={policy.ClientId} AND RelationshipId={policy.RelationshipId}")
            .AsNoTracking().Where(x=>x.EndedAt==null).OrderBy(x=>x.Id).ToArrayAsync(token);
        var recipients=contacts.Where(x=>!string.IsNullOrWhiteSpace(x.DeclaredFullName)&&x.Email is not null&&!x.Email.Any(char.IsControl)&&
            System.Net.Mail.MailAddress.TryCreate(x.Email,out var email)&&email.Address==x.Email).Select(x=>new ServicingTermsRecipient(x.Id,x.DeclaredFullName,x.Email!)).ToArray();
        var consequenceIds=new List<Guid>();
        foreach(var kind in new[]{"notice","certificate-withdrawal","mid-removal","task-close"})
        {
            var intent=new CancellationConsequence{PolicyId=policy.Id,TermId=held.Term.Id,TransactionId=transaction.Id,VersionId=version.Id,
                DecisionId=decision.Id,Kind=kind,CreatedAt=now,CreatedBy=decision.ActorId};
            intent.PayloadJson=JsonSerializer.Serialize(new{format="cancellation-consequence-1",intentId=intent.Id,kind,policyId=policy.Id,policyReference=policy.Reference,
                termId=held.Term.Id,transactionId=transaction.Id,versionId=version.Id,contentHash=Convert.ToHexStringLower(version.ContentHash),
                effectiveAt=decision.EffectiveAt,reasonCode=preview.ReasonCode,reason=decision.Reason,recipients,demo=true},Json);
            intent.PayloadHash=Hash(intent.PayloadJson);
            var work=new OutboxWork{Kind="cancellation-"+kind,OperationKey="cancellation-"+kind+"/"+transaction.Id.ToString("N"),SubjectRecordId=intent.Id,
                Payload=intent.PayloadJson,ScenarioVersionId=held.Setting.Id,NextAttemptAt=now,CreatedAt=now,CreatedBy=decision.ActorId,CorrelationId=correlation};
            db.Add(work);await db.SaveChangesAsync(token);intent.WorkId=work.Id;db.Add(intent);await db.SaveChangesAsync(token);consequenceIds.Add(intent.Id);
        }
        var conflicts=await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(UPDLOCK,HOLDLOCK) WHERE PolicyId={policy.Id} AND Id<>{draft.Id} AND State='draft'").ToArrayAsync(token);
        foreach(var conflict in conflicts)
        {
            await ServicingRatingService.InvalidateAsync(db,conflict,"Policy cancelled by "+transaction.Id.ToString("D"),now,token);
            conflict.State="abandoned";
            var lease=await db.Set<ServicingLease>().SingleOrDefaultAsync(x=>x.DraftId==conflict.Id,token);
            if(lease is not null){lease.Active=false;lease.ExpiresAt=now;}
            db.Add(new AuditEvent{SubjectRecordId=conflict.Id,EventType="servicing.cancelled-by-policy",Reason=decision.Reason,ActorId=decision.ActorId,
                CorrelationId=correlation,OccurredAt=now,CreatedAt=now,CreatedBy=decision.ActorId,After=JsonSerializer.Serialize(new{transactionId=transaction.Id})});
        }
        var currentLease=await db.Set<ServicingLease>().SingleAsync(x=>x.DraftId==draft.Id,token);currentLease.Active=false;currentLease.ExpiresAt=now;
        held.Term.CurrentVersionId=version.Id;await db.SaveChangesAsync(token);
        draft.State="issued";draft.IssuedTransactionId=transaction.Id;
        db.Add(new AuditEvent{SubjectRecordId=policy.Id,EventType="policy.cancellation-issued",Reason=decision.Reason,ActorId=decision.ActorId,
            CorrelationId=correlation,OccurredAt=now,CreatedAt=now,CreatedBy=decision.ActorId,After=JsonSerializer.Serialize(new{transactionId=transaction.Id,versionId=version.Id,decisionId=decision.Id})});
        db.Add(new ClientActivity{ClientId=policy.ClientId,RelationshipId=policy.RelationshipId,RecordKind="quote",RecordId=policy.SourceQuoteId,
            EventType="policy.cancellation-issued",ActorId=decision.ActorId,CreatedAt=now,CreatedBy=decision.ActorId,OccurredAt=now});
        await db.SaveChangesAsync(token);
        var etag="\""+Convert.ToBase64String(draft.RowVersion)+"\"";
        return new(transaction.Id,201,JsonSerializer.Serialize(new{policyId=policy.Id,policyReference=policy.Reference,draftId=draft.Id,draftEtag=etag,
            termId=held.Term.Id,transactionId=transaction.Id,versionId=version.Id,decisionId=decision.Id,approvalId=decision.ApprovalId,previewId=decision.PreviewId,
            obligationId=financial.ObligationId,journalId=financial.JournalId,accountingPeriodId=financial.AccountingPeriodId,postingDate=financial.PostingDate,
            currency="GBP",amountDue=PolicyIssueWriter.Money(Math.Max(0,financial.InvoiceDue)),amountCredit=PolicyIssueWriter.Money(Math.Max(0,-financial.InvoiceDue)),
            netAmount=PolicyIssueWriter.Money(financial.InvoiceDue),cashPaid="0.00",consequenceIds=consequenceIds.ToArray(),effectiveAt=decision.EffectiveAt,
            processedAt=now,coverageState=now<decision.EffectiveAt?"cancellation-scheduled":"cancelled"},Json),Etag:etag);
    }
}
