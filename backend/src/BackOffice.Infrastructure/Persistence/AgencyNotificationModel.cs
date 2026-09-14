using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyNotifications(ModelBuilder model)
    {
        var notification=Record<AgencyNotification>(model,"AgencyNotification");
        notification.ToTable(t=>t.UseSqlOutputClause(false));
        Text(notification,("Purpose",40),("ProtectedPayload",30000));Hash(notification,"ContentHash");
        notification.HasAlternateKey(x=>new{x.Id,x.AgencyId});
        notification.HasIndex(x=>x.WorkId).IsUnique();notification.HasIndex(x=>new{x.AgencyId,x.CreatedAt,x.Id});
        notification.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        notification.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        notification.HasOne<AgencyInvitation>().WithMany().HasForeignKey(x=>new{x.InvitationId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        Check(notification,"Invitation","([Purpose]='agency-invitation' AND [InvitationId] IS NOT NULL) OR ([Purpose]='agency-activated' AND [InvitationId] IS NULL)");
        Check(notification,"Purpose","[Purpose] IN ('agency-activated','agency-invitation')");
        Check(notification,"Payload","LEN([ProtectedPayload]) > 0");
        var receipt=Record<AgencyNotificationReceipt>(model,"AgencyNotificationReceipt");
        receipt.ToTable(t=>t.UseSqlOutputClause(false));Text(receipt,("ResultCode",40));
        receipt.HasIndex(x=>x.NotificationId).IsUnique();
        receipt.HasOne<AgencyNotification>().WithMany().HasForeignKey(x=>new{x.NotificationId,x.AgencyId})
            .HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        Check(receipt,"Outcome","([Accepted]=1 AND [ResultCode]='demo-delivered') OR ([Accepted]=0 AND [ResultCode]='demo-rejected')");
    }
}
