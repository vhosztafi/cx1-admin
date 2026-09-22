using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Persistence;

public sealed class RenewalLapseCorrespondence:StoredRecord
{
    public Guid LapseEventId {get;set;}
    public Guid MessageId {get;set;}
}
public sealed partial class BackOfficeDbContext
{
    private static void ConfigureLegacyRenewalCorrespondence(ModelBuilder model)
    {
        var row=Record<RenewalLapseCorrespondence>(model,"RenewalLapseCorrespondence");row.ToTable(t=>t.UseSqlOutputClause(false));
        row.HasOne<RenewalLapseEvent>().WithMany().HasForeignKey(x=>x.LapseEventId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<OperationalMessageDraft>().WithMany().HasForeignKey(x=>x.MessageId).OnDelete(DeleteBehavior.NoAction);
        row.HasIndex(x=>x.LapseEventId).IsUnique();row.HasIndex(x=>x.MessageId).IsUnique();Check(row,"Creator","[CreatedBy] IS NOT NULL");
    }
}
