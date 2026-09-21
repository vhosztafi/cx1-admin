using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalTasks(ModelBuilder model)
    {
        model.HasSequence<long>("TaskReferenceSequence").StartsAt(1).IncrementsBy(1);
        var subject = Record<OperationalSubject>(model, "OperationalSubject");
        Text(subject, ("Kind", 30)); subject.ToTable(t => t.UseSqlOutputClause(false));
        subject.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        subject.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        subject.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.NoAction);
        subject.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        subject.HasOne<ServicingDraft>().WithMany().HasForeignKey(x => x.ServicingDraftId).OnDelete(DeleteBehavior.NoAction);
        var parents = new[] { ("AgencyId", "agency"), ("RelationshipId", "relationship"), ("QuoteId", "quote"), ("PolicyId", "policy"), ("ServicingDraftId", "servicing-draft") };
        foreach (var (field, _) in parents) subject.HasIndex(field).IsUnique().HasFilter($"[{field}] IS NOT NULL");
        Check(subject, "Parent", string.Join(" OR ", parents.Select(p => $"([Kind]='{p.Item2}' AND [{p.Item1}] IS NOT NULL AND " + string.Join(" AND ", parents.Where(x => x != p).Select(x => $"[{x.Item1}] IS NULL")) + ")")));
        Check(subject, "Creator", "[CreatedBy] IS NOT NULL");

        var task = Record<OperationalTask>(model, "OperationalTask");
        task.ToTable(t => t.UseSqlOutputClause(false));
        Text(task, ("Reference", 40), ("TypeCode", 40), ("Title", 300), ("Priority", 20), ("State", 30), ("CompletionReason", 1000));
        task.HasOne<OperationalSubject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        task.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.NoAction);
        task.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
        task.HasIndex(x => x.Reference).IsUnique();
        task.HasIndex(x => new { x.SubjectId, x.State, x.Id });
        task.HasIndex(x => new { x.OwnerId, x.State, x.DueOn, x.Id });
        task.HasIndex(x => new { x.TeamId, x.State, x.DueOn, x.Id });
        task.HasIndex(x => new { x.CreatedBy, x.CreatedAt, x.Id });
        Check(task, "Type", "[TypeCode] IN ('servicing','underwriting-referral','authority-referral','renewal','data-exception','agency-onboarding','complaint','underwriting')");
        Check(task, "State", "[State] IN ('open','in-progress','awaiting-information','blocked','completed','cancelled')");
        Check(task, "Priority", "[Priority] IN ('low','normal','high','urgent')");
        Check(task, "Assignment", "[OwnerId] IS NULL OR [TeamId] IS NULL");
        Check(task, "Creator", "[CreatedBy] IS NOT NULL");
        Check(task, "Sequence", "[EventSequence]>=1");
        Check(task, "Title", "LEN(LTRIM(RTRIM([Title])))>0");
        Check(task, "Completion", "[State] NOT IN ('completed','cancelled') OR ([CompletionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([CompletionReason])))>0)");

        var history = Record<OperationalTaskEvent>(model, "OperationalTaskEvent");
        history.ToTable(t => t.UseSqlOutputClause(false));
        Text(history, ("Kind", 60), ("Reason", 1000), ("ActorLabel", 300));
        history.HasOne<OperationalTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
        history.HasIndex(x => new { x.TaskId, x.Sequence }).IsUnique();
        Check(history, "Sequence", "[Sequence]>0"); Check(history, "Creator", "[CreatedBy] IS NOT NULL");
        Check(history, "Snapshot", "ISJSON([SnapshotJson],OBJECT)=1");

        var comment = Record<OperationalTaskComment>(model, "OperationalTaskComment");
        comment.ToTable(t => t.UseSqlOutputClause(false));
        Text(comment, ("Body", 8000), ("AuthorLabel", 300));
        comment.HasOne<OperationalTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
        comment.HasIndex(x => new { x.TaskId, x.CreatedAt, x.Id });
        Check(comment, "Creator", "[CreatedBy] IS NOT NULL"); Check(comment, "Body", "LEN(LTRIM(RTRIM([Body])))>0");

        var item = Record<OperationalTaskChecklist>(model, "OperationalTaskChecklist");
        item.ToTable(t => t.UseSqlOutputClause(false)); Text(item, ("Label", 300));
        item.HasOne<OperationalTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
        item.HasIndex(x => new { x.TaskId, x.Ordinal }).IsUnique();
        Check(item, "Ordinal", "[Ordinal]>=0"); Check(item, "Creator", "[CreatedBy] IS NOT NULL");
    }
}
