using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalFiles(ModelBuilder model)
    {
        var file = Record<FileObject>(model, "FileObject");
        file.ToTable(t => t.UseSqlOutputClause(false));
        Text(file, ("StorageKind", 30), ("FileName", 255), ("MediaType", 100), ("Sha256", 64), ("State", 20), ("FailureCode", 100));
        file.Property(x => x.Sha256).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        file.HasOne<OperationalSubject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        file.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        file.HasOne<AgencyEvidenceFile>().WithMany().HasForeignKey(x => x.AgencyEvidenceFileId).OnDelete(DeleteBehavior.NoAction);
        file.HasOne<QuoteEvidenceFile>().WithMany().HasForeignKey(x => x.QuoteEvidenceFileId).OnDelete(DeleteBehavior.NoAction);
        file.HasOne<ServicingEvidenceFile>().WithMany().HasForeignKey(x => x.ServicingEvidenceFileId).OnDelete(DeleteBehavior.NoAction);
        foreach (var field in new[] { "WorkId", "AgencyEvidenceFileId", "QuoteEvidenceFileId", "ServicingEvidenceFileId" })
            file.HasIndex(field).IsUnique().HasFilter($"[{field}] IS NOT NULL");
        file.HasIndex(x => new { x.SubjectId, x.State, x.Id });
        Check(file, "Creator", "[CreatedBy] IS NOT NULL");
        Check(file, "Content", "[ByteLength] BETWEEN 1 AND 20971520 AND DATALENGTH([Sha256])=64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2 AND LEN(TRIM([FileName]))>0");
        Check(file, "State", "([State]='pending' AND [VerifiedAt] IS NULL AND [FailureCode] IS NULL) OR ([State]='ready' AND [VerifiedAt] IS NOT NULL AND [FailureCode] IS NULL) OR ([State]='quarantined' AND [FailureCode] IS NOT NULL AND LEN(TRIM([FailureCode]))>0)");
        Check(file, "VerifiedAt", "[VerifiedAt] IS NULL OR [VerifiedAt]>=[CreatedAt]");
        Check(file, "Storage", """
            ([StorageKind]='local' AND [WorkId] IS NOT NULL AND [AgencyEvidenceFileId] IS NULL AND [QuoteEvidenceFileId] IS NULL AND [ServicingEvidenceFileId] IS NULL AND [MediaType] IN ('application/pdf','image/png','image/jpeg')) OR
            ([StorageKind]='agency-evidence' AND [WorkId] IS NULL AND [AgencyEvidenceFileId] IS NOT NULL AND [QuoteEvidenceFileId] IS NULL AND [ServicingEvidenceFileId] IS NULL AND [State]<>'pending') OR
            ([StorageKind]='quote-evidence' AND [WorkId] IS NULL AND [AgencyEvidenceFileId] IS NULL AND [QuoteEvidenceFileId] IS NOT NULL AND [ServicingEvidenceFileId] IS NULL AND [State]<>'pending') OR
            ([StorageKind]='servicing-evidence' AND [WorkId] IS NULL AND [AgencyEvidenceFileId] IS NULL AND [QuoteEvidenceFileId] IS NULL AND [ServicingEvidenceFileId] IS NOT NULL AND [State]<>'pending')
            """);
    }
}
