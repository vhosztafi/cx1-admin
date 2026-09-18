using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureServicingCapacity(ModelBuilder model)
    {
        var capacity = Record<ServicingCapacityCase>(model, "ServicingCapacityCase");
        capacity.ToTable(t => t.UseSqlOutputClause(false)); Text(capacity, ("Reason", 2000), ("State", 20));
        capacity.HasAlternateKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
        capacity.HasIndex(x => x.ReferralId).IsUnique();
        capacity.HasIndex(x => new { x.DraftId, x.CreatedAt, x.Id });
        capacity.HasOne<ServicingReferral>().WithMany()
            .HasForeignKey(x => new { x.ReferralId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId })
            .HasPrincipalKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        capacity.HasOne<CapacityProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        capacity.HasOne<BinderVersion>().WithMany().HasForeignKey(x => x.BinderVersionId).OnDelete(DeleteBehavior.NoAction);
        capacity.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RaisedBy).OnDelete(DeleteBehavior.NoAction);
        Check(capacity, "Identity", "[Id]<>'00000000-0000-0000-0000-000000000000'");
        Check(capacity, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RaisedBy]");
        Check(capacity, "Reason", "LEN(TRIM([Reason]))>=10");
        Check(capacity, "State", "[State] IN ('draft','queued','sent','queried','approved','conditional','declined','failed','superseded')");
        Check(capacity, "Time", "[UpdatedAt]>=[CreatedAt]");

        var submission = Record<ServicingCapacitySubmission>(model, "ServicingCapacitySubmission");
        submission.ToTable(t => t.UseSqlOutputClause(false));
        Text(submission, ("Body", 10000), ("Reason", 2000));
        UnderwritingJson(submission, "ContextJson"); Hash(submission, "ContextHash");
        submission.HasAlternateKey(x => new { x.Id, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
        submission.HasAlternateKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId });
        submission.HasIndex(x => new { x.CaseId, x.Sequence }).IsUnique();
        submission.HasIndex(x => x.WorkId).IsUnique();
        submission.HasOne<ServicingCapacityCase>().WithMany()
            .HasForeignKey(x => new { x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId })
            .HasPrincipalKey(x => new { x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.NoAction);
        Check(submission, "Identity", "[Id]<>'00000000-0000-0000-0000-000000000000'");
        Check(submission, "Sequence", "[Sequence]>0");
        Check(submission, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[SubmittedBy]");
        Check(submission, "Text", "LEN(TRIM([Body]))>0 AND DATALENGTH([Body])<=20000 AND LEN(TRIM([Reason]))>=10");
        Check(submission, "Time", "[SubmittedAt]=[CreatedAt] AND [ResponseDueAt]>[SubmittedAt]");
        Check(submission, "ContextHash", "[ContextHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[ContextJson] COLLATE Latin1_General_100_BIN2_UTF8))");
        capacity.HasOne<ServicingCapacitySubmission>().WithMany()
            .HasForeignKey(x => new { x.CurrentSubmissionId, x.Id, x.CycleId, x.DraftId, x.RevisionId, x.RatingId })
            .HasPrincipalKey(x => new { x.Id, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);

        var evidence = Record<ServicingCapacitySubmissionEvidence>(model, "ServicingCapacitySubmissionEvidence");
        evidence.ToTable(t => t.UseSqlOutputClause(false));
        evidence.HasIndex(x => new { x.SubmissionId, x.AssociationId }).IsUnique();
        evidence.HasOne<ServicingCapacitySubmission>().WithMany()
            .HasForeignKey(x => new { x.SubmissionId, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId })
            .HasPrincipalKey(x => new { x.Id, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<ServicingEvidenceEvent>().WithMany()
            .HasForeignKey(x => new { x.ReviewId, x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId })
            .HasPrincipalKey(x => new { x.Id, x.AssociationId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        Check(evidence, "Actor", "[CreatedBy] IS NOT NULL");

        var message = Record<ServicingCapacityMessage>(model, "ServicingCapacityMessage");
        message.ToTable(t => t.UseSqlOutputClause(false)); Text(message, ("Kind", 20), ("Body", 10000)); Hash(message, "ContentHash");
        message.HasIndex(x => new { x.CaseId, x.Sequence }).IsUnique();
        message.HasOne<ServicingCapacitySubmission>().WithMany()
            .HasForeignKey(x => new { x.SubmissionId, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId })
            .HasPrincipalKey(x => new { x.Id, x.CaseId, x.CycleId, x.DraftId, x.RevisionId, x.RatingId }).OnDelete(DeleteBehavior.NoAction);
        message.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.NoAction);
        message.HasIndex(x => x.SubmissionId).IsUnique().HasFilter("[Kind]='submission'");
        Check(message, "Sequence", "[Sequence]>0");
        Check(message, "Kind", "[Kind] IN ('submission','chase','query-reply')");
        Check(message, "Body", "LEN(TRIM([Body]))>0 AND DATALENGTH([Body])<=20000");
        Check(message, "ContentHash", "[ContentHash]=HASHBYTES('SHA2_256',CONVERT(varchar(max),[Body] COLLATE Latin1_General_100_BIN2_UTF8))");
        Check(message, "Actor", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RecordedBy] AND [RecordedAt]=[CreatedAt]");
    }
}
