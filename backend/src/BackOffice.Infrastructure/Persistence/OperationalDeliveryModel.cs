using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalDelivery(ModelBuilder model)
    {
        var version=Record<OperationalMessageVersion>(model,"OperationalMessageVersion");version.ToTable(t=>t.UseSqlOutputClause(false));
        Text(version,("ContentHash",64));Json(version,"ContentJson");
        version.HasOne<OperationalMessageDraft>().WithMany().HasForeignKey(x=>x.MessageId).OnDelete(DeleteBehavior.NoAction);
        version.HasIndex(x=>x.MessageId).IsUnique();
        Check(version,"Content","[CreatedBy] IS NOT NULL AND LEN([ContentHash])=64");
        var delivery=Record<OperationalDelivery>(model,"OperationalDelivery");delivery.ToTable(t=>t.UseSqlOutputClause(false));
        Text(delivery,("ContentHash",64),("State",20),("OutcomeCode",100));Json(delivery,"ContentJson");
        delivery.HasOne<OperationalSubject>().WithMany().HasForeignKey(x=>x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<OperationalMessageVersion>().WithMany().HasForeignKey(x=>x.MessageVersionId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<OperationalDelivery>().WithMany().HasForeignKey(x=>x.ResendOfId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x=>x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasOne<AdapterAttempt>().WithMany().HasForeignKey(x=>x.AttemptId).OnDelete(DeleteBehavior.NoAction);
        delivery.HasIndex(x=>x.WorkId).IsUnique();delivery.HasIndex(x=>new{x.SubjectId,x.CreatedAt,x.Id});
        delivery.HasIndex(x=>x.MessageVersionId);
        Check(delivery,"State","[State] IN ('queued','delivered','failed','superseded')");
        Check(delivery,"Content","[CreatedBy] IS NOT NULL AND LEN([ContentHash])=64 AND ([ResendOfId] IS NULL OR [ResendOfId]<>[Id])");
        var recipient=Record<OperationalDeliveryRecipient>(model,"OperationalDeliveryRecipient");recipient.ToTable(t=>t.UseSqlOutputClause(false));
        Text(recipient,("Name",300),("Email",254));
        recipient.HasOne<OperationalDelivery>().WithMany().HasForeignKey(x=>x.DeliveryId).OnDelete(DeleteBehavior.NoAction);
        recipient.HasOne<Contact>().WithMany().HasForeignKey(x=>x.ContactId).OnDelete(DeleteBehavior.NoAction);
        recipient.HasIndex(x=>new{x.DeliveryId,x.ContactId}).IsUnique();
        Check(recipient,"Content","[CreatedBy] IS NOT NULL AND LEN(TRIM([Name]))>0 AND LEN(TRIM([Email]))>0");
        var attachment=Record<OperationalDeliveryAttachment>(model,"OperationalDeliveryAttachment");attachment.ToTable(t=>t.UseSqlOutputClause(false));
        Text(attachment,("ContentHash",64),("OriginalName",255),("MediaType",100));
        attachment.HasOne<OperationalDelivery>().WithMany().HasForeignKey(x=>x.DeliveryId).OnDelete(DeleteBehavior.NoAction);
        attachment.HasOne<DocumentVersion>().WithMany().HasForeignKey(x=>x.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
        attachment.HasOne<FileObject>().WithMany().HasForeignKey(x=>x.FileObjectId).OnDelete(DeleteBehavior.NoAction);
        attachment.HasIndex(x=>new{x.DeliveryId,x.DocumentVersionId}).IsUnique();
        Check(attachment,"Content","[CreatedBy] IS NOT NULL AND LEN([ContentHash])=64 AND [Length]>0");
    }
}
