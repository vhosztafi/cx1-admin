using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingCarrierConditions(ModelBuilder model)
    {
        var condition=Record<ServicingCapacityCondition>(model,"ServicingCapacityCondition");
        condition.ToTable(t=>t.UseSqlOutputClause(false));Text(condition,("Code",60),("Kind",20));UnderwritingJson(condition,"DefinitionJson");
        condition.HasAlternateKey(x=>new{x.Id,x.ResponseId,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        condition.HasIndex(x=>new{x.ResponseId,x.Sequence}).IsUnique();
        condition.HasOne<ServicingCapacityResponseRecord>().WithMany()
            .HasForeignKey(x=>new{x.ResponseId,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(condition,"Sequence","[Sequence] BETWEEN 1 AND 20");
        Check(condition,"Kind","[Kind] IN ('documentary','warranty','risk-change')");
        Check(condition,"Actor","[CreatedBy] IS NOT NULL");
        Check(condition,"Dates","ISJSON([EffectiveDatesJson],ARRAY)=1 AND DATALENGTH([EffectiveDatesJson])<=16384 AND JSON_VALUE([EffectiveDatesJson],'$[0]') IS NOT NULL");
        Check(condition,"Code","JSON_VALUE([DefinitionJson],'$.code') IS NOT NULL AND [Code]=JSON_VALUE([DefinitionJson],'$.code') AND [Code] IN ('provide-driver-proof','provide-premises-security','provide-signed-statement','provide-trading-history','overnight-security','named-drivers-only','any-driver-minimum-licence','revise-stock-limit','revise-vehicle-limit')");
        var resolution=Record<ServicingCapacityConditionResolution>(model,"ServicingCapacityConditionResolution");resolution.ToTable(t=>t.UseSqlOutputClause(false));
        Text(resolution,("Outcome",20),("Reason",2000),("InputFingerprint",64));
        resolution.Property(x=>x.InputFingerprint).UseCollation("Latin1_General_100_BIN2");
        resolution.HasIndex(x=>new{x.ConditionId,x.Sequence}).IsUnique();
        resolution.HasOne<ServicingCapacityCondition>().WithMany().HasForeignKey(x=>new{x.ConditionId,x.ResponseId,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.ResponseId,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<ServicingEvidenceAssociation>().WithMany().HasForeignKey(x=>new{x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId,x.InputFingerprint})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId,x.InputFingerprint}).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<ServicingEvidenceEvent>().WithMany().HasForeignKey(x=>new{x.ReviewId,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<UserAuthorityGrant>().WithMany().HasForeignKey(x=>new{x.GrantId,x.ActorId,x.AuthorityVersionId})
            .HasPrincipalKey(x=>new{x.Id,x.UserId,x.AuthorityVersionId}).OnDelete(DeleteBehavior.NoAction);
        Check(resolution,"Sequence","[Sequence]>0");
        Check(resolution,"Outcome","[Outcome] IN ('satisfied','rejected')");
        Check(resolution,"Reason","LEN(TRIM([Reason]))>=10 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[ActorId] AND [RecordedAt]>=[CreatedAt]");
    }
}

