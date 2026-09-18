using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingAcceptance(ModelBuilder model)
    {
        var acceptance=Record<ServicingAcceptance>(model,"ServicingAcceptance");acceptance.ToTable(t=>t.UseSqlOutputClause(false));
        Text(acceptance,("TermsHash",64),("AssuranceHash",64),("AccepterLabel",200),("Channel",20));
        acceptance.HasAlternateKey(x=>new{x.Id,x.DeliveryId,x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        acceptance.HasIndex(x=>new{x.DraftId,x.RecordedAt,x.Id});
        acceptance.HasOne<ServicingTermsDelivery>().WithMany()
            .HasForeignKey(x=>new{x.DeliveryId,x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        acceptance.HasOne<ServicingEvidenceEvent>().WithMany()
            .HasForeignKey(x=>new{x.EvidenceReviewId,x.EvidenceAssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        acceptance.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.RecordedBy).OnDelete(DeleteBehavior.NoAction);
        Check(acceptance,"Channel","[Channel] IN ('email','written','telephone')");
        Check(acceptance,"Provenance","LEN(TRIM([AccepterLabel]))>0 AND [CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt] AND [AcceptedAt]<=[RecordedAt]");
        foreach(var property in new[]{"TermsHash","AssuranceHash"})
        {acceptance.Property<string>(property).UseCollation("Latin1_General_100_BIN2");Check(acceptance,property,$"LEN([{property}])=64 AND [{property}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");}
        model.Entity<ServicingCycle>().HasOne<ServicingAcceptance>().WithMany()
            .HasForeignKey(x=>new{x.CurrentAcceptanceId,x.CurrentDeliveryId,x.CurrentTermsVersionId,x.Id,x.DraftId,x.RevisionId,x.CurrentRatingId})
            .HasPrincipalKey(x=>new{x.Id,x.DeliveryId,x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(model.Entity<ServicingCycle>(),"AcceptancePointer","[CurrentAcceptanceId] IS NULL OR ([CurrentDeliveryId] IS NOT NULL AND [CurrentTermsVersionId] IS NOT NULL AND [CurrentRatingId] IS NOT NULL)");
    }
}
