using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyEvidence(ModelBuilder model)
    {
        var file=Record<AgencyEvidenceFile>(model,"AgencyEvidenceFile");
        Text(file,("FileName",150),("ContentType",50),("Sha256",64),("ScreeningState",20));
        file.HasAlternateKey(x=>new{x.Id,x.AgencyId});file.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        file.Property(x=>x.Content).HasColumnType("varbinary(max)");
        Check(file,"Length","[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content]) = [ByteLength]");
        Check(file,"Type","[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
        Check(file,"Screen","[ScreeningState] IN ('pending','demo-cleared','rejected')");
        Check(file,"Hash","LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        file.ToTable(t=>t.UseSqlOutputClause(false));
        var evidence=Record<AgencyEvidence>(model,"AgencyEvidence");
        Text(evidence,("Kind",30),("State",20),("InputFingerprint",64),("ResultCode",100),("Notes",1000));Json(evidence,"InputSnapshot");
        evidence.Property(x=>x.Ordinal).UseIdentityColumn();evidence.HasAlternateKey(x=>new{x.Id,x.AgencyId});
        evidence.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.RuleVersionId).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<AgencyEvidenceFile>().WithMany().HasForeignKey(x=>new{x.FileId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.AttestedBy).OnDelete(DeleteBehavior.NoAction);
        evidence.HasIndex(x=>new{x.AgencyId,x.Kind,x.Ordinal});
        Check(evidence,"Kind","[Kind] IN ('fca','toba','professional-indemnity','financial-check','sanctions','ownership','dpa','client-money')");
        Check(evidence,"State","[State] IN ('pending','verified','rejected','unavailable')");
        Check(evidence,"Verified","([State] = 'verified' AND [VerifiedAt] IS NOT NULL) OR ([State] <> 'verified' AND [VerifiedAt] IS NULL)");
        Check(evidence,"Attestation","[Kind] NOT IN ('toba','professional-indemnity','dpa','client-money') OR ([FileId] IS NOT NULL AND [AttestedBy] IS NOT NULL AND [Notes] IS NOT NULL AND LEN(TRIM([Notes])) > 0)");
        Check(evidence,"SnapshotBounds","DATALENGTH([InputSnapshot]) <= 131072 AND LEFT(LTRIM([InputSnapshot]),1) = '{'");
        Check(evidence,"Fingerprint","LEN([InputFingerprint]) = 64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        evidence.ToTable(t=>t.UseSqlOutputClause(false));
        var attempt=Record<AgencyCheckAttempt>(model,"AgencyCheckAttempt");
        Text(attempt,("Kind",30),("State",20),("InputFingerprint",64),("Scenario",20),("ResultCode",100));attempt.Property(x=>x.Ordinal).UseIdentityColumn();
        attempt.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        attempt.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.RuleVersionId).OnDelete(DeleteBehavior.NoAction);
        attempt.HasOne<AgencyEvidence>().WithMany().HasForeignKey(x=>new{x.EvidenceId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        attempt.HasIndex(x=>new{x.AgencyId,x.Ordinal});
        Check(attempt,"Kind","[Kind] IN ('fca','financial-check','sanctions','ownership')");
        Check(attempt,"State","[State] IN ('passed','refer','unavailable')");
        Check(attempt,"Scenario","[Scenario] IN ('pass','refer','unavailable')");
        attempt.ToTable(t=>t.UseSqlOutputClause(false));
    }
}
