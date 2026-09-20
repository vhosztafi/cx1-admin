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

        var association = Record<ServicingEvidenceAssociation>(model,"ServicingEvidenceAssociation");
        association.ToTable(t=>t.UseSqlOutputClause(false));
        Text(association,("RequirementCode",60),("InputFingerprint",64),("Reason",2000));
        association.Property(x=>x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        association.HasAlternateKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId,x.InputFingerprint});
        association.HasIndex(x=>new{x.DraftId,x.CycleId,x.CreatedAt,x.Id});
        association.HasOne<ServicingRatingResult>().WithMany().HasForeignKey(x=>new{x.RatingId,x.CycleId,x.DraftId,x.RevisionId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId}).OnDelete(DeleteBehavior.NoAction);
        association.HasOne<ServicingEvidenceFile>().WithMany().HasForeignKey(x=>new{x.FileId,x.DraftId})
            .HasPrincipalKey(x=>new{x.Id,x.DraftId}).OnDelete(DeleteBehavior.NoAction);
        Check(association,"Fingerprint","LEN([InputFingerprint])=64 AND [InputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(association,"Reason","LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL");
        Check(association,"Purpose","([RequirementCode] IN ('motor-trader-proof','no-claims-proof','trading-history','warranty-acknowledgement','capacity-response','signed-statement','acceptance-proof','cc-property-proof','cc-liability-proof','cc-claims-experience-proof','cc-health-safety-proof','cc-bi-proof','cc-business-proof') AND [RiskItemId] IS NULL) OR ([RequirementCode] IN ('photocard-both-sides','driving-record','premises-security','cc-location-proof','cc-electrical-proof','cc-alarm-proof','cc-structural-proof','cc-wage-proof') AND [RiskItemId] IS NOT NULL AND [RiskItemId]<>'00000000-0000-0000-0000-000000000000')");
        association.HasOne<ServicingCapacitySubmission>().WithMany()
            .HasForeignKey(x=>new{x.CapacitySubmissionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(association,"CapacityPurpose","([RequirementCode]='capacity-response' AND [CapacitySubmissionId] IS NOT NULL AND [RiskItemId] IS NULL) OR ([RequirementCode]<>'capacity-response' AND [CapacitySubmissionId] IS NULL)");
        association.HasOne<ServicingTermsVersion>().WithMany()
            .HasForeignKey(x=>new{x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(association,"TermsPurpose","([RequirementCode] IN ('signed-statement','acceptance-proof') AND [TermsVersionId] IS NOT NULL AND [RiskItemId] IS NULL) OR ([RequirementCode] NOT IN ('signed-statement','acceptance-proof') AND [TermsVersionId] IS NULL)");

        var review = Record<ServicingEvidenceEvent>(model,"ServicingEvidenceEvent");review.ToTable(t=>t.UseSqlOutputClause(false));
        Text(review,("Kind",20),("Outcome",20),("Reason",2000),("InputFingerprint",64));
        review.Property(x=>x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        review.HasAlternateKey(x=>new{x.Id,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        review.HasIndex(x=>new{x.AssociationId,x.Sequence}).IsUnique();
        review.HasOne<ServicingEvidenceAssociation>().WithMany().HasForeignKey(x=>new{x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId,x.InputFingerprint})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId,x.InputFingerprint}).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ActorId).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<AuthorityVersion>().WithMany().HasForeignKey(x=>x.AuthorityVersionId).OnDelete(DeleteBehavior.NoAction);
        Check(review,"Sequence","[Sequence]>0");
        Check(review,"Outcome","([Kind]='review' AND [Outcome] IS NOT NULL AND [Outcome] IN ('accepted','rejected') AND [AuthorityVersionId] IS NOT NULL) OR ([Kind]='withdrawal' AND [Outcome] IS NULL AND [AuthorityVersionId] IS NULL)");
        Check(review,"Reason","LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
        association.HasOne<ServicingEvidenceEvent>().WithMany().HasForeignKey(x=>new{x.LatestReviewId,x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        association.HasOne<ServicingEvidenceEvent>().WithMany().HasForeignKey(x=>new{x.WithdrawnEventId,x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
    }
}
