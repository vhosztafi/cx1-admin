using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinanceSubmissions(ModelBuilder model)
    {
        var submission = Record<FinanceBordereauSubmission>(model, "FinanceBordereauSubmission");
        submission.ToTable(t => t.UseSqlOutputClause(false));
        submission.HasOne<FinanceBordereauBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<FinanceBordereauVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<SettingVersion>().WithMany().HasForeignKey(x => x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        submission.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x => x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        submission.HasIndex(x => x.BatchId).IsUnique();
        submission.HasIndex(x => x.VersionId).IsUnique();
        submission.HasIndex(x => x.WorkId).IsUnique();
        submission.HasIndex(x => x.OperationKey).IsUnique();
        Hash(submission, "ContentHash"); Hash(submission, "RequestHash");
        Text(submission, ("OperationKey", 200), ("State", 30), ("ProviderState", 20), ("ProviderEventId", 200));
        Check(submission, "State", "[State] IN ('queued','uncertain','acknowledged','submitted','rejected','failed')");
        Check(submission, "Provider", "([ProviderState] IS NULL AND [ProviderOperationId] IS NULL AND [ProviderEventId] IS NULL) OR ([ProviderState] IN ('accepted','rejected') AND [ProviderOperationId] IS NOT NULL AND [ProviderEventId] IS NOT NULL)");
        Check(submission, "Applied", "([State] IN ('submitted','rejected') AND [AppliedAt] IS NOT NULL AND [ProviderState] IS NOT NULL) OR ([State] NOT IN ('submitted','rejected') AND [AppliedAt] IS NULL)");
    }
}
