using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;

// Durable obligation provenance. Phase 9 will add a real task association;
// this record does not pretend that a work queue task already exists.
public sealed class AgencyFollowUp:StoredRecord
{
    public Guid AgencyId {get;set;}
    public Guid? EvidenceId {get;set;}
    public Guid? ActivationRequestId {get;set;}
    public string Purpose {get;set;}="";
    public DateOnly DueOn {get;set;}
}
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyFollowUps(ModelBuilder model)
    {
        var row=Record<AgencyFollowUp>(model,"AgencyFollowUp");Text(row,("Purpose",40));
        row.HasOne<Agency>().WithMany().HasForeignKey(x=>x.AgencyId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<AgencyEvidence>().WithMany().HasForeignKey(x=>new{x.EvidenceId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<AgencyStateRequest>().WithMany().HasForeignKey(x=>new{x.ActivationRequestId,x.AgencyId}).HasPrincipalKey(x=>new{x.Id,x.AgencyId}).OnDelete(DeleteBehavior.NoAction);
        row.HasIndex(x=>new{x.AgencyId,x.EvidenceId,x.Purpose,x.DueOn}).IsUnique().HasFilter("[EvidenceId] IS NOT NULL");
        row.HasIndex(x=>new{x.AgencyId,x.ActivationRequestId,x.Purpose,x.DueOn}).IsUnique().HasFilter("[ActivationRequestId] IS NOT NULL");
        row.HasIndex(x=>new{x.DueOn,x.AgencyId,x.Id});
        Check(row,"Source","([Purpose]='pi-expiry' AND [EvidenceId] IS NOT NULL AND [ActivationRequestId] IS NULL) OR ([Purpose]='quarter-review' AND [EvidenceId] IS NULL AND [ActivationRequestId] IS NOT NULL)");
        Check(row,"Creator","[CreatedBy] IS NOT NULL");row.ToTable(t=>t.UseSqlOutputClause(false));
    }
}
