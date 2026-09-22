using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureCancellationOperations(ModelBuilder model)
    {
        var receipt = Record<CancellationOperationalReceipt>(model, "CancellationOperationalReceipt");
        receipt.ToTable(t => t.UseSqlOutputClause(false));
        Text(receipt, ("Outcome", 60), ("PayloadHash", 64));
        receipt.Property(x => x.ResultJson).IsRequired();
        receipt.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x => x.ConsequenceId).OnDelete(DeleteBehavior.NoAction);
        receipt.HasOne<OutboxWork>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.NoAction);
        receipt.HasIndex(x => x.ConsequenceId).IsUnique(); receipt.HasIndex(x => x.WorkId).IsUnique();
        Check(receipt, "Content", "ISJSON([ResultJson])=1 AND LEN([PayloadHash])=64 AND [CreatedBy] IS NOT NULL");

        var dispatch = Record<CancellationNoticeDispatch>(model, "CancellationNoticeDispatch");
        dispatch.ToTable(t => t.UseSqlOutputClause(false)); Text(dispatch, ("PayloadHash", 64));
        dispatch.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x => x.ConsequenceId).OnDelete(DeleteBehavior.NoAction);
        dispatch.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => x.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
        dispatch.HasOne<OperationalDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.NoAction);
        dispatch.HasIndex(x => x.ConsequenceId).IsUnique(); dispatch.HasIndex(x => x.DeliveryId).IsUnique();

        var withdrawal = Record<CertificateWithdrawal>(model, "CertificateWithdrawal");
        withdrawal.ToTable(t => t.UseSqlOutputClause(false)); Text(withdrawal, ("CertificateKind", 80));
        withdrawal.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x => x.ConsequenceId).OnDelete(DeleteBehavior.NoAction);
        withdrawal.HasOne<PolicyTerm>().WithMany().HasForeignKey(x => x.TermId).OnDelete(DeleteBehavior.NoAction);
        withdrawal.HasIndex(x => x.ConsequenceId).IsUnique();
        withdrawal.HasIndex(x => new { x.TermId, x.CertificateKind }).IsUnique();

        var closure = Record<CancellationTaskClosure>(model, "CancellationTaskClosure");
        closure.ToTable(t => t.UseSqlOutputClause(false));
        closure.HasOne<CancellationConsequence>().WithMany().HasForeignKey(x => x.ConsequenceId).OnDelete(DeleteBehavior.NoAction);
        closure.HasOne<OperationalTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
        closure.HasOne<OperationalTaskEvent>().WithMany().HasForeignKey(x => x.TaskEventId).OnDelete(DeleteBehavior.NoAction);
        closure.HasIndex(x => new { x.ConsequenceId, x.TaskId }).IsUnique(); closure.HasIndex(x => x.TaskEventId).IsUnique();
    }
}
