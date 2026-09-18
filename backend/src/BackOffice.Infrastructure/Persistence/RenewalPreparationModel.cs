using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureRenewalPreparation(ModelBuilder model)
    {
        var preparation=Record<RenewalPreparationVersion>(model,"RenewalPreparationVersion");
        preparation.ToTable(t=>t.UseSqlOutputClause(false));
        preparation.HasAlternateKey(x=>new{x.Id,x.DraftId});
        preparation.HasIndex(x=>new{x.DraftId,x.Sequence}).IsUnique();
        preparation.HasOne<ServicingDraft>().WithMany().HasForeignKey(x=>new{x.DraftId,x.PolicyId,x.BaseTermId,x.BaseVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.PolicyId,x.BaseTermId,x.BaseVersionId}).OnDelete(DeleteBehavior.NoAction);
        preparation.HasOne<Policy>().WithMany().HasForeignKey(x=>new{x.PolicyId,x.ProductId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId}).OnDelete(DeleteBehavior.NoAction);
        preparation.HasOne<ProductVersion>().WithMany().HasForeignKey(x=>new{x.ProductVersionId,x.ProductId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId}).OnDelete(DeleteBehavior.NoAction);
        preparation.HasOne<BinderVersion>().WithMany().HasForeignKey(x=>new{x.BinderVersionId,x.ProductId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId}).OnDelete(DeleteBehavior.NoAction);
        preparation.HasOne<AgencyTermsVersion>().WithMany().HasForeignKey(x=>x.AgencyTermsVersionId).OnDelete(DeleteBehavior.NoAction);
        preparation.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.RuleSettingVersionId).OnDelete(DeleteBehavior.NoAction);
        preparation.HasOne<FairValueAssessmentVersion>().WithMany().HasForeignKey(x=>new{x.FairValueAssessmentId,x.ProductId,x.ProductVersionId,x.BinderVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId,x.ProductVersionId,x.BinderVersionId}).OnDelete(DeleteBehavior.NoAction);
        Check(preparation,"Term","[TermMonths] BETWEEN 1 AND 12 AND [StartsAt]<[EndsAt] AND ([EndUtcOffsetMinutes] IS NULL OR [EndUtcOffsetMinutes] IN (0,60))");
        Check(preparation,"Sequence","[Sequence]>0 AND [CreatedBy] IS NOT NULL");
        Check(preparation,"Intent","ISJSON([TermIntentJson],OBJECT)=1 AND DATALENGTH([TermIntentJson])<=8192");

        var evidence=Record<RenewalExperienceEvidence>(model,"RenewalExperienceEvidence");
        evidence.ToTable(t=>t.UseSqlOutputClause(false));
        evidence.HasAlternateKey(x=>new{x.Id,x.DraftId});
        evidence.HasOne<ServicingEvidenceFile>().WithMany().HasForeignKey(x=>new{x.FileId,x.DraftId})
            .HasPrincipalKey(x=>new{x.Id,x.DraftId}).OnDelete(DeleteBehavior.NoAction);
        Check(evidence,"Actor","[CreatedBy] IS NOT NULL");

        var experience=Record<RenewalExperienceVersion>(model,"RenewalExperienceVersion");
        experience.ToTable(t=>t.UseSqlOutputClause(false));
        experience.HasAlternateKey(x=>new{x.Id,x.DraftId});
        experience.HasIndex(x=>new{x.DraftId,x.Sequence}).IsUnique();
        experience.HasOne<RenewalExperienceEvidence>().WithMany().HasForeignKey(x=>new{x.EvidenceAssociationId,x.DraftId})
            .HasPrincipalKey(x=>new{x.Id,x.DraftId}).OnDelete(DeleteBehavior.NoAction);
        Text(experience,("SourceCode",30),("SourceReference",200));
        foreach(var name in new[]{"Paid","Outstanding","EarnedPremium"})
        {
            experience.Property<decimal>(name).HasPrecision(19,2);
            Check(experience,name,$"[{name}] BETWEEN 0 AND 9999999999999.99");
        }
        Check(experience,"Period","[ObservationStartsOn]<[ObservationEndsOn]");
        Check(experience,"Claims","[ClaimCount] BETWEEN 0 AND 100000");
        Check(experience,"Sequence","[Sequence]>0");
        Check(experience,"Source","[SourceCode] IN ('insured','agency','administrator') AND LEN(TRIM([SourceReference])) BETWEEN 1 AND 200 AND [CreatedBy] IS NOT NULL");

        var review=Record<RenewalExperienceReview>(model,"RenewalExperienceReview");
        review.ToTable(t=>t.UseSqlOutputClause(false));
        review.HasIndex(x=>new{x.ExperienceVersionId,x.Sequence}).IsUnique();
        review.HasOne<RenewalExperienceVersion>().WithMany().HasForeignKey(x=>new{x.ExperienceVersionId,x.DraftId})
            .HasPrincipalKey(x=>new{x.Id,x.DraftId}).OnDelete(DeleteBehavior.NoAction);
        review.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x=>new{x.AuthorityGrantId,x.CreatedBy,x.AuthorityVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.UserId,x.AuthorityVersionId}).OnDelete(DeleteBehavior.NoAction);
        Text(review,("Outcome",20),("Reason",2000));
        Check(review,"Outcome","[Outcome] IN ('accepted','rejected')");
        Check(review,"Provenance","[Sequence]>0 AND [CreatedBy] IS NOT NULL AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");

        var file=Record<ProductEvidenceFileVersion>(model,"ProductEvidenceFileVersion");
        file.ToTable(t=>t.UseSqlOutputClause(false));
        file.HasAlternateKey(x=>new{x.Id,x.ProductId,x.ProductVersionId,x.BinderVersionId});
        file.HasOne<ProductVersion>().WithMany().HasForeignKey(x=>new{x.ProductVersionId,x.ProductId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId}).OnDelete(DeleteBehavior.NoAction);
        file.HasOne<BinderVersion>().WithMany().HasForeignKey(x=>new{x.BinderVersionId,x.ProductId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId}).OnDelete(DeleteBehavior.NoAction);
        Text(file,("FileName",200),("ContentType",100),("Sha256",64),("ScreeningState",20),("ScreeningMethod",50));
        file.Property(x=>x.Sha256).UseCollation("Latin1_General_100_BIN2");
        Check(file,"Size","[ByteLength] BETWEEN 1 AND 10485760 AND DATALENGTH([Content])=[ByteLength]");
        Check(file,"Hash","LEN([Sha256])=64 AND [Sha256]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',[Content]),2))");
        Check(file,"Media","[ContentType] IN ('application/pdf','image/png','image/jpeg','text/plain')");
        Check(file,"Name","LEN(TRIM([FileName]))>0 AND [FileName] NOT LIKE '%/%' AND [FileName] NOT LIKE '%\\%' AND [FileName] NOT LIKE '%:%'");
        Check(file,"Screening","[ScreeningState]='accepted' AND [ScreeningMethod]='demo-signature-v1' AND [CreatedBy] IS NOT NULL");

        var assessment=Record<FairValueAssessmentVersion>(model,"FairValueAssessmentVersion");
        assessment.ToTable(t=>t.UseSqlOutputClause(false));
        assessment.HasAlternateKey(x=>new{x.Id,x.ProductId,x.ProductVersionId,x.BinderVersionId});
        assessment.HasIndex(x=>new{x.ProductVersionId,x.BinderVersionId,x.ValidFrom});
        assessment.HasOne<ProductEvidenceFileVersion>().WithMany().HasForeignKey(x=>new{x.EvidenceFileVersionId,x.ProductId,x.ProductVersionId,x.BinderVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.ProductId,x.ProductVersionId,x.BinderVersionId}).OnDelete(DeleteBehavior.NoAction);
        assessment.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.ApprovedBy).OnDelete(DeleteBehavior.NoAction);
        Text(assessment,("Outcome",20),("Reason",2000));
        Check(assessment,"Outcome","[Outcome] IN ('pass','refer','fail')");
        Check(assessment,"Validity","[ValidFrom]<[ValidTo]");
        Check(assessment,"Approval","[CreatedBy] IS NOT NULL AND [CreatedBy]=[ApprovedBy] AND [ApprovedAt]=[CreatedAt] AND LEN(TRIM([Reason])) BETWEEN 10 AND 2000");
    }
}
