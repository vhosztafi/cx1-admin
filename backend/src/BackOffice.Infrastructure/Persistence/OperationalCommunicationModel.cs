using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalCommunication(ModelBuilder model)
    {
        var note=Record<InternalNote>(model,"InternalNote");note.ToTable(t=>t.UseSqlOutputClause(false));
        Text(note,("Body",8000),("AuthorLabel",300));
        note.HasOne<OperationalSubject>().WithMany().HasForeignKey(x=>x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        note.HasIndex(x=>new{x.SubjectId,x.CreatedAt,x.Id});
        Check(note,"Content","[CreatedBy] IS NOT NULL AND LEN(TRIM([Body]))>0 AND LEN(TRIM([AuthorLabel]))>0");
        var thread=Record<OperationalThread>(model,"OperationalThread");thread.ToTable(t=>t.UseSqlOutputClause(false));
        Text(thread,("Visibility",20),("Subject",300),("AuthorLabel",300));
        thread.HasOne<OperationalSubject>().WithMany().HasForeignKey(x=>x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        thread.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        thread.HasIndex(x=>new{x.SubjectId,x.Visibility,x.CreatedAt,x.Id});
        Check(thread,"Audience","([Visibility]='internal' AND [RelationshipId] IS NULL) OR ([Visibility]='agency' AND [RelationshipId] IS NOT NULL)");
        Check(thread,"Content","[CreatedBy] IS NOT NULL AND LEN(TRIM([Subject]))>0 AND LEN(TRIM([AuthorLabel]))>0");
        var draft=Record<OperationalMessageDraft>(model,"OperationalMessageDraft");draft.ToTable(t=>t.UseSqlOutputClause(false));
        Text(draft,("Body",8000),("AuthorLabel",300),("State",20));
        draft.HasOne<OperationalThread>().WithMany().HasForeignKey(x=>x.ThreadId).OnDelete(DeleteBehavior.NoAction);
        draft.HasIndex(x=>new{x.ThreadId,x.CreatedAt,x.Id});
        Check(draft,"Creator","[CreatedBy] IS NOT NULL AND LEN(TRIM([AuthorLabel]))>0");
        Check(draft,"State","[State] IN ('draft','queued','sent','failed','superseded')");
        var recipient=Record<MessageDraftRecipient>(model,"MessageDraftRecipient");recipient.ToTable(t=>t.UseSqlOutputClause(false));
        recipient.HasOne<OperationalMessageDraft>().WithMany().HasForeignKey(x=>x.MessageId).OnDelete(DeleteBehavior.NoAction);
        recipient.HasOne<Contact>().WithMany().HasForeignKey(x=>x.ContactId).OnDelete(DeleteBehavior.NoAction);
        recipient.HasIndex(x=>new{x.MessageId,x.ContactId}).IsUnique();
        Check(recipient,"Creator","[CreatedBy] IS NOT NULL");
        var attachment=Record<MessageDraftAttachment>(model,"MessageDraftAttachment");attachment.ToTable(t=>t.UseSqlOutputClause(false));
        attachment.HasOne<OperationalMessageDraft>().WithMany().HasForeignKey(x=>x.MessageId).OnDelete(DeleteBehavior.NoAction);
        attachment.HasOne<DocumentVersion>().WithMany().HasForeignKey(x=>x.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
        attachment.HasIndex(x=>new{x.MessageId,x.DocumentVersionId}).IsUnique();
        Check(attachment,"Creator","[CreatedBy] IS NOT NULL");
    }
}
