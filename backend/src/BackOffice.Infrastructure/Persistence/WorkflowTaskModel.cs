using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureWorkflowTasks(ModelBuilder model)
    {
        model.Entity<OperationalTask>().HasAlternateKey(x => new { x.Id, x.SubjectId });
        var row = Record<WorkflowTaskBinding>(model, "WorkflowTaskBinding");
        row.ToTable(t => t.UseSqlOutputClause(false));
        Text(row, ("RuleCode", 64), ("SourceKind", 40), ("SourceHash", 64), ("RuleHash", 64));
        row.HasOne<OperationalTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.SubjectId }).HasPrincipalKey(x => new { x.Id, x.SubjectId }).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<QuoteReferral>().WithMany().HasForeignKey(x => x.QuoteReferralId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<ServicingReferral>().WithMany().HasForeignKey(x => x.ServicingReferralId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<QuoteReferralDecision>().WithMany().HasForeignKey(x => x.QuoteQueryDecisionId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<ServicingReferralDecision>().WithMany().HasForeignKey(x => x.ServicingQueryDecisionId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<MatchInformationRequest>().WithMany().HasForeignKey(x => x.MatchInformationRequestId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<PolicyTerm>().WithMany().HasForeignKey(x => x.PolicyTermId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<AgencyFollowUp>().WithMany().HasForeignKey(x => x.AgencyFollowUpId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<JobException>().WithMany().HasForeignKey(x => x.JobExceptionId).OnDelete(DeleteBehavior.NoAction);
        row.HasIndex(x => x.TaskId).IsUnique();
        row.HasIndex(x => new { x.RuleCode, x.SourceKind, x.SourceEventId }).IsUnique();
        var sources = new[] { ("quote-referral", "QuoteReferralId"), ("servicing-referral", "ServicingReferralId"), ("quote-query", "QuoteQueryDecisionId"),
            ("servicing-query", "ServicingQueryDecisionId"), ("match-information-request", "MatchInformationRequestId"), ("policy-term", "PolicyTermId"),
            ("agency-follow-up", "AgencyFollowUpId"), ("job-exception", "JobExceptionId") };
        Check(row, "Source", string.Join(" OR ", sources.Select(s => $"([SourceKind]='{s.Item1}' AND [{s.Item2}] IS NOT NULL AND [SourceEventId]=[{s.Item2}] AND " +
            string.Join(" AND ", sources.Where(other => other != s).Select(other => $"[{other.Item2}] IS NULL")) + ")")));
        Check(row, "Snapshots", "ISJSON([SourceSnapshotJson],OBJECT)=1 AND ISJSON([RuleSnapshotJson],OBJECT)=1");
        Check(row, "Hashes", "LEN([SourceHash])=64 AND LEN([RuleHash])=64 AND [SourceHash] NOT LIKE '%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2 AND [RuleHash] NOT LIKE '%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2");
        Check(row, "Creator", "[CreatedBy] IS NOT NULL");
    }
}
