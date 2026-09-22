using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalClaims(ModelBuilder model)
    {
        var admin=Record<ClaimsAdministrator>(model,"ClaimsAdministrator");Text(admin,("Code",80),("Name",300),("State",20));admin.HasIndex(x=>x.Code).IsUnique();
        Check(admin,"State","[State] IN ('active','inactive')");
        var handoff=Record<ClaimsHandoff>(model,"ClaimsHandoff");handoff.ToTable(t=>t.UseSqlOutputClause(false));
        Text(handoff,("RequestHash",64),("State",20),("OutcomeCode",100),("ProviderReference",100));handoff.Property(x=>x.RequestJson).IsRequired();
        handoff.HasOne<OperationalIncident>().WithMany().HasForeignKey(x=>x.IncidentId).OnDelete(DeleteBehavior.NoAction);
        handoff.HasOne<Policy>().WithMany().HasForeignKey(x=>x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        handoff.HasOne<IncidentRevision>().WithMany().HasForeignKey(x=>x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        handoff.HasOne<IncidentOccurrenceRecord>().WithMany().HasForeignKey(x=>x.ResolutionId).OnDelete(DeleteBehavior.NoAction);
        handoff.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>x.SourceVersionId).OnDelete(DeleteBehavior.NoAction);
        handoff.HasOne<ClaimsAdministrator>().WithMany().HasForeignKey(x=>x.AdministratorId).OnDelete(DeleteBehavior.NoAction);
        handoff.HasIndex(x=>x.RevisionId).IsUnique();handoff.HasIndex(x=>new{x.IncidentId,x.CreatedAt,x.Id});
        Check(handoff,"Content","ISJSON([RequestJson])=1 AND LEN([RequestHash])=64 AND [CreatedBy] IS NOT NULL");
        Check(handoff,"State","[State] IN ('queued','acknowledged','rejected','failed','superseded')");
        var request=Record<ClaimsRequest>(model,"ClaimsRequest");request.ToTable(t=>t.UseSqlOutputClause(false));Text(request,("Purpose",20),("PayloadHash",64));request.Property(x=>x.PayloadJson).IsRequired();
        request.HasOne<ClaimsHandoff>().WithMany().HasForeignKey(x=>x.HandoffId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<OutboxWork>().WithMany().HasForeignKey(x=>x.WorkId).OnDelete(DeleteBehavior.NoAction);
        request.HasOne<SettingVersion>().WithMany().HasForeignKey(x=>x.ScenarioVersionId).OnDelete(DeleteBehavior.NoAction);
        request.HasIndex(x=>x.WorkId).IsUnique();request.HasIndex(x=>new{x.HandoffId,x.Purpose}).IsUnique().HasFilter("[Purpose]='handoff'");
        Check(request,"Content","ISJSON([PayloadJson])=1 AND LEN([PayloadHash])=64 AND [CreatedBy] IS NOT NULL AND [Purpose] IN ('handoff','refresh','contact')");
        var summary=Record<ClaimsSummary>(model,"ClaimsSummary");summary.ToTable(t=>t.UseSqlOutputClause(false));Text(summary,("ProviderEventId",200),("ContentHash",64));summary.Property(x=>x.SummaryJson).IsRequired();
        summary.HasOne<ClaimsHandoff>().WithMany().HasForeignKey(x=>x.HandoffId).OnDelete(DeleteBehavior.NoAction);
        summary.HasOne<ClaimsRequest>().WithMany().HasForeignKey(x=>x.RequestId).OnDelete(DeleteBehavior.NoAction);
        summary.HasOne<DemoProviderOperation>().WithMany().HasForeignKey(x=>x.ProviderOperationId).OnDelete(DeleteBehavior.NoAction);
        summary.HasIndex(x=>x.ProviderEventId).IsUnique();summary.HasIndex(x=>new{x.HandoffId,x.AsOf,x.ReceivedAt,x.Id});
        Check(summary,"Content","ISJSON([SummaryJson])=1 AND LEN([ContentHash])=64 AND [AsOf]<=[ReceivedAt] AND [ReceivedAt]=[CreatedAt]");
    }
}
