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
    }
}
