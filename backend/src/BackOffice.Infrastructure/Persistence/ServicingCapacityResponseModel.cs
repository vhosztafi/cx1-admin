using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingCapacityResponses(ModelBuilder model)
    {
        var response=Record<ServicingCapacityResponseRecord>(model,"ServicingCapacityResponse");
        response.ToTable(t=>t.UseSqlOutputClause(false));
        Text(response,("Provenance",30),("Outcome",30),("Body",10000),("ProviderUnderwriter",200),("ProviderReference",100),("ProviderEventId",100),("ApplicationState",20));
        UnderwritingJson(response,"DefinitionJson");Hash(response,"ContentHash");
        response.HasAlternateKey(x=>new{x.Id,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        response.HasIndex(x=>new{x.CaseId,x.Sequence}).IsUnique();
        response.HasIndex(x=>new{x.ProviderId,x.ProviderEventId}).IsUnique().HasFilter("[ProviderEventId] IS NOT NULL");
        response.HasIndex(x=>x.InboxId).IsUnique().HasFilter("[InboxId] IS NOT NULL");
        response.HasOne<ServicingCapacitySubmission>().WithMany()
            .HasForeignKey(x=>new{x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        response.HasOne<CapacityProvider>().WithMany().HasForeignKey(x=>x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        response.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.RecordedBy).OnDelete(DeleteBehavior.NoAction);
        response.HasOne<AdapterInbox>().WithMany().HasForeignKey(x=>x.InboxId).OnDelete(DeleteBehavior.NoAction);
        response.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x=>x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        response.HasOne<ServicingEvidenceEvent>().WithMany()
            .HasForeignKey(x=>new{x.EvidenceReviewId,x.EvidenceAssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.AssociationId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(response,"Sequence","[Sequence]>0");
        Check(response,"Outcome","[Outcome] IN ('approve','approve-with-conditions','query','decline')");
        Check(response,"Text","LEN(TRIM([Body]))>0 AND DATALENGTH([Body])<=20000 AND LEN(TRIM([ProviderUnderwriter]))>0 AND LEN(TRIM([ProviderReference]))>0");
        Check(response,"Actor","[CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt] AND [ReceivedAt]<=[RecordedAt]");
        Check(response,"Hash","[ContentHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[DefinitionJson] COLLATE Latin1_General_100_BIN2_UTF8))");
        Check(response,"Provenance","([Provenance]='supplied-response' AND [EvidenceAssociationId] IS NOT NULL AND [EvidenceReviewId] IS NOT NULL AND [ProviderEventId] IS NULL AND [ProviderOperationId] IS NULL AND [InboxId] IS NULL AND [ApplicationState]='applied') OR ([Provenance]='demo-provider' AND [EvidenceAssociationId] IS NULL AND [EvidenceReviewId] IS NULL AND [ProviderEventId] IS NOT NULL AND [ProviderOperationId] IS NOT NULL AND [InboxId] IS NOT NULL AND [ApplicationState] IN ('applied','superseded'))");
        var capacity=model.Entity<ServicingCapacityCase>();
        capacity.HasOne<ServicingCapacityResponseRecord>().WithMany()
            .HasForeignKey(x=>new{x.CurrentResponseId,x.CurrentSubmissionId,x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.SubmissionId,x.CaseId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(capacity,"ResponseOwner","[CurrentResponseId] IS NULL OR [CurrentSubmissionId] IS NOT NULL");
        ConfigureServicingCarrierConditions(model);
    }
}
