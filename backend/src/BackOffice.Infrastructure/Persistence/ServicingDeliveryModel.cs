using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingDelivery(ModelBuilder model)
    {
        var delivery=Record<ServicingTermsDelivery>(model,"ServicingTermsDelivery");delivery.ToTable(t=>t.UseSqlOutputClause(false));
        Text(delivery,("State",20),("PayloadHash",64),("AssuranceHashAtSend",64),("OutcomeCode",100));
        delivery.HasAlternateKey(x=>new{x.Id,x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId});
        delivery.HasIndex(x=>x.WorkId).IsUnique();delivery.HasIndex(x=>new{x.DraftId,x.CreatedAt,x.Id});
        delivery.HasOne<ServicingTermsVersion>().WithMany().HasForeignKey(x=>new{x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId})
            .HasPrincipalKey(x=>new{x.Id,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.SentBy).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x=>x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<AdapterAttempt>().WithMany().HasForeignKey(x=>new{x.AttemptId,x.WorkId}).HasPrincipalKey(x=>new{x.Id,x.WorkId}).OnDelete(DeleteBehavior.NoAction);
        Check(delivery,"Recipients","ISJSON([RecipientSnapshotJson],ARRAY)=1 AND DATALENGTH(CONVERT(varchar(max),[RecipientSnapshotJson] COLLATE Latin1_General_100_BIN2_UTF8))<=1048576");
        Check(delivery,"Payload","ISJSON([PayloadJson],OBJECT)=1 AND DATALENGTH(CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8))<=9437184");
        Check(delivery,"State","[State] IN ('queued','delivered','failed','superseded')");
        Check(delivery,"Completion","([State]='queued' AND [CompletedAt] IS NULL AND [ProviderOperationId] IS NULL AND [AttemptId] IS NULL AND [OutcomeCode] IS NULL) OR ([State]<>'queued' AND [CompletedAt] IS NOT NULL AND [CompletedAt]>=[CreatedAt] AND [AttemptId] IS NOT NULL)");
        Check(delivery,"Delivered","[State]<>'delivered' OR ([ProviderOperationId] IS NOT NULL AND [OutcomeCode] IS NULL)");
        Check(delivery,"Provenance","[CreatedBy] IS NOT NULL AND [CreatedBy]=[SentBy]");
        Check(delivery,"PayloadHash","[PayloadHash]=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONVERT(varchar(max),[PayloadJson] COLLATE Latin1_General_100_BIN2_UTF8)),2))");
        foreach(var property in new[]{"PayloadHash","AssuranceHashAtSend"})
        {delivery.Property<string>(property).UseCollation("Latin1_General_100_BIN2");Check(delivery,property+"Format",$"LEN([{property}])=64 AND [{property}] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");}
        model.Entity<ServicingCycle>().HasOne<ServicingTermsDelivery>().WithMany()
            .HasForeignKey(x=>new{x.CurrentDeliveryId,x.CurrentTermsVersionId,x.Id,x.DraftId,x.RevisionId,x.CurrentRatingId})
            .HasPrincipalKey(x=>new{x.Id,x.TermsVersionId,x.CycleId,x.DraftId,x.RevisionId,x.RatingId}).OnDelete(DeleteBehavior.NoAction);
        Check(model.Entity<ServicingCycle>(),"DeliveryPointer","[CurrentDeliveryId] IS NULL OR ([CurrentTermsVersionId] IS NOT NULL AND [CurrentRatingId] IS NOT NULL)");
    }
}
