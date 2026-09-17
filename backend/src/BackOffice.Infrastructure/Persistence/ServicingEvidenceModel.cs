using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingEvidence(ModelBuilder model)
    {
        var file = Record<ServicingEvidenceFile>(model, "ServicingEvidenceFile");
        file.ToTable(t => t.UseSqlOutputClause(false));
        Text(file, ("FileName", 200), ("ContentType", 100), ("Sha256", 64), ("ScreeningState", 20), ("ScreeningMethod", 50));
        file.Property(x => x.Sha256).UseCollation("Latin1_General_100_BIN2");
        file.HasAlternateKey(x => new { x.Id, x.DraftId });
        file.HasIndex(x => new { x.DraftId, x.CreatedAt, x.Id });
        file.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.NoAction);
        Check(file, "Size", "[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content])=[ByteLength]");
        Check(file, "Hash", "LEN([Sha256])=64 AND [Sha256]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',[Content]),2))");
        Check(file, "Media", "[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
        Check(file, "Name", "LEN(TRIM([FileName]))>0 AND [FileName] NOT LIKE '%/%' AND [FileName] NOT LIKE '%\\%' AND [FileName] NOT LIKE '%:%'");
        Check(file, "Screening", "[ScreeningState]='accepted' AND [ScreeningMethod]='demo-signature-v1' AND [CreatedBy] IS NOT NULL");

    }
}
