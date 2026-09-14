using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyApprovals(ModelBuilder model)
    {
        var request=Record<AgencyStateRequest>(model,"AgencyStateRequest");
        Text(request,("Kind",30),("RequestedState",20),("ProposedInputFingerprint",64),("RequestReason",1000),("State",20),("DecisionReason",1000));
        request.Property(x=>x.BaseVersion).HasColumnType("binary(8)");
        request.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.RequestedBy).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<StaffUser>().WithMany().HasForeignKey(x=>x.DecisionBy).OnDelete(DeleteBehavior.NoAction);
        request.HasIndex(x=>new{x.AgencyId,x.Kind}).IsUnique().HasFilter("[State] = 'pending'");
        request.HasIndex(x=>new{x.AgencyId,x.CreatedAt,x.Id});
        Check(request,"KindState","([Kind] IN ('activation','reactivation') AND [RequestedState]='active') OR ([Kind]='suspension' AND [RequestedState]='suspended')");
        Check(request,"State","[State] IN ('pending','applied','rejected','stale')");
        Check(request,"Requester","[CreatedBy] IS NOT NULL AND [CreatedBy]=[RequestedBy] AND LEN(TRIM([RequestReason]))>0");
        Check(request,"Fingerprint","LEN([ProposedInputFingerprint])=64 AND [ProposedInputFingerprint] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_100_BIN2");
        Check(request,"Decision","([State]='pending' AND [DecisionBy] IS NULL AND [DecisionReason] IS NULL AND [DecidedAt] IS NULL) OR ([State] IN ('applied','rejected') AND [DecisionBy] IS NOT NULL AND [DecisionBy]<>[RequestedBy] AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt]) OR ([State]='stale' AND [DecisionBy] IS NULL AND [DecisionReason] IS NOT NULL AND LEN(TRIM([DecisionReason]))>0 AND [DecidedAt] IS NOT NULL AND [DecidedAt]>=[CreatedAt])");
        request.ToTable(t=>t.UseSqlOutputClause(false));
    }
}
