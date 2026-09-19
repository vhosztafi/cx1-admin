using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureCancellationIssue(ModelBuilder model)
    {
        var decision=Record<CancellationIssueDecision>(model,"CancellationIssueDecision");decision.ToTable(t=>t.UseSqlOutputClause(false));
        Text(decision,("Reason",2000));Hash(decision,"PreviewHash");decision.HasIndex(x=>x.DraftId).IsUnique();
        decision.HasAlternateKey(x=>new{x.Id,x.PolicyId,x.BaseTermId});
        decision.HasAlternateKey(x=>new{x.Id,x.DraftId,x.PolicyId,x.RevisionId});
        decision.HasOne<ServicingDraft>().WithMany().HasForeignKey(x=>new{x.DraftId,x.PolicyId,x.BaseTermId,x.BaseVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.PolicyId,x.BaseTermId,x.BaseVersionId}).OnDelete(DeleteBehavior.NoAction);
        model.Entity<CancellationApproval>().HasAlternateKey(x=>new{x.Id,x.DraftId,x.RevisionId,x.PreviewId,x.PreviewHash});
        decision.HasOne<CancellationApproval>().WithMany().HasForeignKey(x=>new{x.ApprovalId,x.DraftId,x.RevisionId,x.PreviewId,x.PreviewHash})
            .HasPrincipalKey(x=>new{x.Id,x.DraftId,x.RevisionId,x.PreviewId,x.PreviewHash}).OnDelete(DeleteBehavior.NoAction);
        decision.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x=>new{x.AuthorityGrantId,x.ActorId,x.AuthorityVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.UserId,x.AuthorityVersionId}).OnDelete(DeleteBehavior.NoAction);
        Check(decision,"Actor","[CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
        Check(decision,"Effective","DATEPART(TZOFFSET,[EffectiveAt])=0");

        var intent=Record<CancellationConsequence>(model,"CancellationConsequence");intent.ToTable(t=>t.UseSqlOutputClause(false));
        Text(intent,("Kind",30));Hash(intent,"PayloadHash");UnderwritingJson(intent,"PayloadJson");
        intent.HasIndex(x=>new{x.TransactionId,x.Kind}).IsUnique();intent.HasIndex(x=>x.WorkId).IsUnique();
        intent.HasAlternateKey(x=>new{x.Id,x.WorkId,x.PayloadHash});
        intent.HasOne<CancellationIssueDecision>().WithMany().HasForeignKey(x=>new{x.DecisionId,x.PolicyId,x.TermId})
            .HasPrincipalKey(x=>new{x.Id,x.PolicyId,x.BaseTermId}).OnDelete(DeleteBehavior.NoAction);
        intent.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>new{x.VersionId,x.TransactionId,x.TermId,x.PolicyId})
            .HasPrincipalKey(x=>new{x.Id,x.TransactionId,x.TermId,x.PolicyId}).OnDelete(DeleteBehavior.NoAction);
        intent.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        Check(intent,"Kind","[Kind] IN ('notice','certificate-withdrawal','mid-removal','task-close')");
        Check(intent,"Hash","[PayloadHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))");

        var receipt=Record<CancellationNoticeReceipt>(model,"CancellationNoticeReceipt");receipt.ToTable(t=>t.UseSqlOutputClause(false));
        Text(receipt,("Outcome",30));Hash(receipt,"PayloadHash");receipt.HasIndex(x=>x.ConsequenceId).IsUnique();receipt.HasIndex(x=>x.WorkId).IsUnique();
        receipt.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x=>new{x.ConsequenceId,x.WorkId,x.PayloadHash})
            .HasPrincipalKey(x=>new{x.Id,x.WorkId,x.PayloadHash}).OnDelete(DeleteBehavior.NoAction);
        Check(receipt,"Outcome","[Outcome] IN ('demo-delivered','demo-no-recipient')");
    }
}
