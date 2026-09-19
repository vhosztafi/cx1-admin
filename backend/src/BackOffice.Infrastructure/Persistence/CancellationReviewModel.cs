using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureCancellationReview(ModelBuilder model)
    {
        var evidence = Record<CancellationEvidence>(model, "CancellationEvidence");
        evidence.ToTable(t => t.UseSqlOutputClause(false));
        evidence.HasAlternateKey(x => new { x.Id, x.DraftId, x.RevisionId });
        evidence.HasOne<ServicingRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.DraftId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<ServicingEvidenceFile>().WithMany().HasForeignKey(x => new { x.FileId, x.DraftId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasIndex(x => new { x.DraftId, x.RevisionId, x.CreatedAt });
        Text(evidence, ("Purpose", 40));
        Check(evidence, "Purpose", "[Purpose] IN ('cancellation-request','cancellation-notice','cancellation-reason','insurer-instruction')");
        Check(evidence, "Delivery", "[NoticeDeliveredAt] IS NULL OR ([Purpose]='cancellation-notice' AND [NoticeDeliveredAt]<=[CreatedAt] AND DATEPART(TZOFFSET,[NoticeDeliveredAt])=0)");
        Check(evidence, "Actor", "[CreatedBy] IS NOT NULL");

        var review = Record<CancellationEvidenceReview>(model, "CancellationEvidenceReview");
        review.ToTable(t => t.UseSqlOutputClause(false));
        review.HasIndex(x => new { x.EvidenceId, x.Sequence }).IsUnique();
        review.HasOne<CancellationEvidence>().WithMany().HasForeignKey(x => new { x.EvidenceId, x.DraftId, x.RevisionId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId, x.RevisionId }).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x => new { x.AuthorityGrantId, x.CreatedBy, x.AuthorityVersionId })
            .HasPrincipalKey(x => new { x.Id, x.UserId, x.AuthorityVersionId }).OnDelete(DeleteBehavior.NoAction);
        Text(review, ("Outcome", 20), ("Reason", 2000));
        Check(review, "Decision", "[Outcome] IN ('accepted','rejected') AND [Sequence]>0 AND [CreatedBy] IS NOT NULL AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");

        var preview = Record<CancellationPreview>(model, "CancellationPreview");
        preview.ToTable(t => t.UseSqlOutputClause(false));
        preview.HasAlternateKey(x => new { x.Id, x.DraftId, x.RevisionId, x.InputHash });
        preview.HasIndex(x => new { x.DraftId, x.CreatedAt });
        preview.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => new { x.DraftId, x.PolicyId, x.BaseTermId, x.BaseVersionId })
            .HasPrincipalKey(x => new { x.Id, x.PolicyId, x.BaseTermId, x.BaseVersionId }).OnDelete(DeleteBehavior.NoAction);
        preview.HasOne<ServicingRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.DraftId })
            .HasPrincipalKey(x => new { x.Id, x.DraftId }).OnDelete(DeleteBehavior.NoAction);
        preview.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.RuleSettingVersionId).OnDelete(DeleteBehavior.NoAction);
        Hash(preview, "InputHash");Text(preview, ("RuleVersion", 60), ("ReasonCode", 40));
        Check(preview, "Reason", "[ReasonCode] IN ('insured-request','non-payment','non-disclosure','trade-ceased','insurer-instruction') AND [CreatedBy] IS NOT NULL");
        Check(preview, "Input", "ISJSON([InputJson],OBJECT)=1 AND DATALENGTH([InputJson])<=8388608 AND [InputHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[InputJson] COLLATE Latin1_General_100_BIN2_UTF8))");
        Check(preview, "Result", "ISJSON([ResultJson],OBJECT)=1 AND DATALENGTH([ResultJson])<=8388608 AND JSON_QUERY([InputJson],'$.amounts') IS NOT NULL AND CONVERT(varbinary(max),[ResultJson])=CONVERT(varbinary(max),JSON_QUERY([InputJson],'$.amounts'))");
        Check(preview, "Effective", "DATEPART(TZOFFSET,[EffectiveAt])=0");

        var approval = Record<CancellationApproval>(model, "CancellationApproval");
        approval.ToTable(t => t.UseSqlOutputClause(false));
        approval.HasIndex(x => x.PreviewId);
        Hash(approval, "PreviewHash"); Text(approval, ("Reason", 2000));
        approval.HasOne<CancellationPreview>().WithMany().HasForeignKey(x => new { x.PreviewId, x.DraftId, x.RevisionId, x.PreviewHash })
            .HasPrincipalKey(x => new { x.Id, x.DraftId, x.RevisionId, x.InputHash }).OnDelete(DeleteBehavior.NoAction);
        approval.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x => new { x.AuthorityGrantId, x.CreatedBy, x.AuthorityVersionId })
            .HasPrincipalKey(x => new { x.Id, x.UserId, x.AuthorityVersionId }).OnDelete(DeleteBehavior.NoAction);
        Check(approval, "Decision", "[CreatedBy] IS NOT NULL AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
    }
}
