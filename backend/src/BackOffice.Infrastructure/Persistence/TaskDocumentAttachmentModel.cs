using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureTaskDocumentAttachments(ModelBuilder model)
    {
        var link=Record<TaskDocumentAttachment>(model,"TaskDocumentAttachment");link.ToTable(t=>t.UseSqlOutputClause(false));
        Text(link,("AuthorLabel",300),("Reason",1000),("RemovalReason",1000));
        link.HasOne<OperationalTask>().WithMany().HasForeignKey(x=>x.TaskId).OnDelete(DeleteBehavior.NoAction);
        link.HasOne<DocumentVersion>().WithMany().HasForeignKey(x=>x.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
        link.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.RemovedBy).OnDelete(DeleteBehavior.NoAction);
        link.HasIndex(x=>new{x.TaskId,x.DocumentVersionId}).IsUnique().HasFilter("[RemovedAt] IS NULL");
        Check(link,"Creator","[CreatedBy] IS NOT NULL AND LEN(TRIM([AuthorLabel]))>0 AND LEN(TRIM([Reason]))>0");
        Check(link,"Removal","([RemovedAt] IS NULL AND [RemovedBy] IS NULL AND [RemovalReason] IS NULL) OR ([RemovedAt] IS NOT NULL AND [RemovedBy] IS NOT NULL AND [RemovalReason] IS NOT NULL AND LEN(TRIM([RemovalReason]))>0 AND [RemovedAt]>=[CreatedAt] AND DATEPART(TZOFFSET,[RemovedAt])=0)");
    }
}
