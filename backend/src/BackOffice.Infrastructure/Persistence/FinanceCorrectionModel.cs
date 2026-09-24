using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureFinanceCorrections(ModelBuilder model)
    {
        var correction = Record<FinanceCorrection>(model, "FinanceCorrection");
        correction.ToTable(t => t.UseSqlOutputClause(false));
        Text(correction, ("OriginalSourceKind", 30), ("DebtorKind", 20), ("Reason", 1000));
        correction.HasIndex(x => new { x.OriginalSourceKind, x.OriginalSourceId });
        correction.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        correction.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x => x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        correction.HasOne<Policy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        correction.HasOne<PolicyTransaction>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.NoAction);
        correction.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.NoAction);
        Check(correction, "Source", "[OriginalSourceKind] IN ('insurance','finance-posting') AND [OriginalSourceId]<>'00000000-0000-0000-0000-000000000000'");
        Check(correction, "Reason", "LEN(TRIM([Reason])) BETWEEN 10 AND 1000");
        Check(correction, "Debtor", "[DebtorKind]='agency' OR ([DebtorKind]='relationship' AND [RelationshipId] IS NOT NULL)");
    }
}
