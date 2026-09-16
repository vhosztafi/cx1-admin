using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureQuoteEvidence(ModelBuilder model)
    {
        var file = Record<QuoteEvidenceFile>(model, "QuoteEvidenceFile");
        file.ToTable(t => t.UseSqlOutputClause(false));
        Text(file, ("FileName", 150), ("ContentType", 100), ("Sha256", 64), ("ScreeningState", 20), ("ScreeningMethod", 50));
        file.Property(x => x.Sha256).UseCollation("Latin1_General_100_BIN2");
        file.HasAlternateKey(x => new { x.Id, x.QuoteId });
        file.HasIndex(x => new { x.QuoteId, x.CreatedAt, x.Id });
        file.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.NoAction);
        Check(file, "Size", "[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content])=[ByteLength]");
        Check(file, "Hash", "LEN([Sha256])=64 AND [Sha256]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',[Content]),2))");
        Check(file, "Media", "[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
        Check(file, "Name", "LEN(TRIM([FileName]))>0 AND [FileName] NOT LIKE '%/%' AND [FileName] NOT LIKE '%\\%' AND [FileName] NOT LIKE '%:%'");
        Check(file, "Screening", "[ScreeningState]='accepted' AND [ScreeningMethod]='demo-signature-v1' AND [CreatedBy] IS NOT NULL");

        var evidence = Record<QuoteCaptureEvidence>(model, "QuoteCaptureEvidence");
        evidence.ToTable(t => t.UseSqlOutputClause(false));
        Text(evidence, ("RequirementCode", 100), ("InputFingerprint", 64), ("Reason", 1000));
        evidence.Property(x => x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        evidence.HasAlternateKey(x => new { x.Id, x.QuoteId });
        evidence.HasIndex(x => new { x.QuoteId, x.CreatedAt, x.Id });
        evidence.HasOne<QuoteRevision>().WithMany().HasForeignKey(x => new { x.RevisionId, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<QuoteEvidenceFile>().WithMany().HasForeignKey(x => new { x.FileId, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        Check(evidence, "Target", "([RequirementCode] IN ('motor-trader-proof','no-claims-proof') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
        Check(evidence, "Fingerprint", "LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(evidence, "Attestation", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");

        var withdrawal = Record<QuoteEvidenceWithdrawal>(model, "QuoteEvidenceWithdrawal");
        withdrawal.ToTable(t => t.UseSqlOutputClause(false)); Text(withdrawal, ("Reason", 1000));
        withdrawal.HasIndex(x => x.EvidenceId).IsUnique();
        withdrawal.HasOne<QuoteCaptureEvidence>().WithMany().HasForeignKey(x => new { x.EvidenceId, x.QuoteId })
            .HasPrincipalKey(x => new { x.Id, x.QuoteId }).OnDelete(DeleteBehavior.NoAction);
        withdrawal.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.NoAction);
        Check(withdrawal, "Reason", "LEN(TRIM([Reason]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId]");
    }
}
