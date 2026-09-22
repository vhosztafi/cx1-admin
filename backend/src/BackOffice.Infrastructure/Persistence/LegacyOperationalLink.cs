using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

// The historical request stays recorded. Current message and delivery state are
// read through this association, never written back into the original record.
public sealed class MatchCorrespondence : StoredRecord
{
    public Guid InformationRequestId { get; set; }
    public Guid MessageId { get; set; }
}

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureLegacyOperations(ModelBuilder model)
    {
        var link=Record<MatchCorrespondence>(model,"MatchCorrespondence");
        link.ToTable(t=>t.UseSqlOutputClause(false));
        link.HasOne<MatchInformationRequest>().WithMany().HasForeignKey(x=>x.InformationRequestId).OnDelete(DeleteBehavior.NoAction);
        link.HasOne<OperationalMessageDraft>().WithMany().HasForeignKey(x=>x.MessageId).OnDelete(DeleteBehavior.NoAction);
        link.HasIndex(x=>x.InformationRequestId).IsUnique();link.HasIndex(x=>x.MessageId).IsUnique();
        Check(link,"Creator","[CreatedBy] IS NOT NULL");
    }
}
