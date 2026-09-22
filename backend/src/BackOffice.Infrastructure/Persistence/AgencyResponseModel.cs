using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureAgencyResponse(ModelBuilder model)
    {
        model.HasSequence<long>("AgencyResponseReferenceSequence");
        var row=Record<AgencyResponseRequest>(model,"AgencyResponseRequest");
        row.ToTable(t=>t.UseSqlOutputClause(false));
        Text(row,("Reference",40),("Subject",300),("Instruction",8000),("Reason",2000),("State",30),("ResolutionReason",2000));
        row.HasOne<OperationalMessageVersion>().WithMany().HasForeignKey(x=>x.MessageVersionId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<OperationalSubject>().WithMany().HasForeignKey(x=>x.SubjectId).OnDelete(DeleteBehavior.NoAction);
        row.HasOne<ClientAgencyRelationship>().WithMany().HasForeignKey(x=>x.RelationshipId).OnDelete(DeleteBehavior.NoAction);
        row.HasIndex(x=>x.MessageVersionId).IsUnique();
        row.HasIndex(x=>x.Reference).IsUnique();
        row.HasIndex(x=>new{x.RelationshipId,x.State,x.CreatedAt,x.Id});
        Check(row,"Content","[CreatedBy] IS NOT NULL AND LEN(TRIM([Reason]))>=10 AND LEN(TRIM([Instruction]))>0 AND LEN(TRIM([Subject]))>0");
        Check(row,"Resolution","([State]='awaiting-response' AND [ResolvedAt] IS NULL AND [ResolvedBy] IS NULL AND [ResolutionReason] IS NULL) OR ([State] IN ('response-received','withdrawn') AND [ResolvedAt] IS NOT NULL AND [ResolvedBy] IS NOT NULL AND [ResolutionReason] IS NOT NULL AND LEN(TRIM([ResolutionReason]))>=10)");
    }
}
