using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyIdentity(ModelBuilder model)
    {
        var users=model.Entity<StaffUser>();users.ToTable(t=>t.UseSqlOutputClause(false));
        users.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        // SQL composite FK uses this unfiltered unique index. An EF alternate key
        // would incorrectly make AgencyId nonnullable for existing internal staff.
        users.HasIndex(x=>new{x.Id,x.AgencyId}).IsUnique().HasFilter(null);
        Check(users,"AgencyTeam","[AgencyId] IS NULL OR [TeamId] IS NULL");
        model.Entity<UserRole>().ToTable(t=>t.UseSqlOutputClause(false));
        model.Entity<Role>().ToTable(t=>t.UseSqlOutputClause(false));
        var invitation=Record<AgencyInvitation>(model,"AgencyInvitation");invitation.ToTable(t=>t.UseSqlOutputClause(false));
        invitation.HasAlternateKey(x=>new{x.Id,x.AgencyId});
        Text(invitation,("State",20));Hash(invitation,"TokenHash");
        invitation.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        invitation.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.NoAction);
        invitation.HasOne<AgencyNotification>().WithMany().HasForeignKey(x=>new{x.NotificationId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        invitation.HasIndex(x=>x.TokenHash).IsUnique().HasFilter("[TokenHash] IS NOT NULL");
        invitation.HasIndex(x=>x.UserId).HasDatabaseName("IX_AgencyInvitation_StagedUser").IsUnique().HasFilter("[State] = 'staged'");
        invitation.HasIndex(x=>new{x.UserId,x.State}).HasDatabaseName("IX_AgencyInvitation_PendingUser").IsUnique().HasFilter("[State] = 'pending'");
        invitation.HasIndex(x=>new{x.AgencyId,x.CreatedAt,x.Id});
        Check(invitation,"State","[State] IN ('staged','pending','accepted','expired','revoked')");
        Check(invitation,"IssuedBundle","([TokenHash] IS NULL AND [IssuedAt] IS NULL AND [ExpiresAt] IS NULL AND [NotificationId] IS NULL) OR ([TokenHash] IS NOT NULL AND [IssuedAt] IS NOT NULL AND [ExpiresAt] IS NOT NULL AND [NotificationId] IS NOT NULL AND [ExpiresAt]=DATEADD(day,14,[IssuedAt]))");
        Check(invitation,"Lifecycle","([State]='staged' AND [TokenHash] IS NULL AND [AcceptedAt] IS NULL AND [RevokedAt] IS NULL) OR ([State] IN ('pending','expired') AND [TokenHash] IS NOT NULL AND [AcceptedAt] IS NULL AND [RevokedAt] IS NULL) OR ([State]='accepted' AND [TokenHash] IS NOT NULL AND [AcceptedAt] IS NOT NULL AND [AcceptedAt]>=[IssuedAt] AND [AcceptedAt]<=[ExpiresAt] AND [RevokedAt] IS NULL) OR ([State]='revoked' AND [AcceptedAt] IS NULL AND [RevokedAt] IS NOT NULL)");
    }
}
