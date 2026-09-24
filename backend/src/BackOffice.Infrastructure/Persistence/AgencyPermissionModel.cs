using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyPermissions(ModelBuilder model)
    {
        var request = Record<AgencyPermissionRequest>(model, "AgencyPermissionRequest");
        Text(request, ("Permission",60), ("Reason",1000), ("State",20), ("DecisionReason",1000));
        request.HasAlternateKey(x => new { x.Id, x.AgencyId, x.Permission });
        request.HasOne<Agency>().WithMany().HasForeignKey(x => x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.DecisionBy).OnDelete(DeleteBehavior.NoAction);
        request.HasIndex(x => new { x.AgencyId, x.Permission }).IsUnique().HasFilter("[State] = 'pending'");
        request.HasIndex(x => new { x.AgencyId, x.CreatedAt, x.Id });
        Check(request, "Permission", "([Permission] COLLATE Latin1_General_100_BIN2 = 'bordereau-download' AND DATALENGTH([Permission])=DATALENGTH(N'bordereau-download')) OR ([Permission] COLLATE Latin1_General_100_BIN2 = 'statement-download' AND DATALENGTH([Permission])=DATALENGTH(N'statement-download'))");
        Check(request, "Requester", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy] AND LEN(TRIM([Reason]))>0");
        Check(request, "Decision", "([State]='pending' AND [DecisionBy] IS NULL AND [DecisionReason] IS NULL AND [DecidedAt] IS NULL) OR ([State] IN ('granted','rejected') AND [DecisionBy] IS NOT NULL AND [DecisionBy]<>[RequestedBy] AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt])");
        request.ToTable(t => t.UseSqlOutputClause(false));

        var grant = Record<AgencyPermissionGrant>(model, "AgencyPermissionGrant");
        Text(grant, ("Permission",60), ("RevocationReason",1000));
        grant.HasOne<AgencyPermissionRequest>().WithMany().HasForeignKey(x => new { x.RequestId, x.AgencyId, x.Permission }).HasPrincipalKey(x => new { x.Id, x.AgencyId, x.Permission }).OnDelete(DeleteBehavior.NoAction);
        grant.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.NoAction);
        grant.HasOne<StaffUser>().WithMany().HasForeignKey(x => x.RevokedBy).OnDelete(DeleteBehavior.NoAction);
        grant.HasIndex(x => x.RequestId).IsUnique();
        grant.HasIndex(x => new { x.AgencyId, x.Permission }).IsUnique().HasFilter("[RevokedAt] IS NULL");
        Check(grant, "Provenance", "[CreatedBy] IS NOT NULL AND [CreatedBy]=[GrantedBy] AND [CreatedAt]=[GrantedAt]");
        Check(grant, "Revocation", "([RevokedAt] IS NULL AND [RevokedBy] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAt] IS NOT NULL AND [RevokedAt]>=[GrantedAt] AND [RevokedBy] IS NOT NULL AND [RevocationReason] IS NOT NULL AND LEN(TRIM([RevocationReason]))>0)");
        grant.ToTable(t => t.UseSqlOutputClause(false));
    }
}
