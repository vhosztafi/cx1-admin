using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public sealed partial class BackOfficeDbContext
{
    private static void ConfigureOperationalIncidents(ModelBuilder model)
    {
        model.HasSequence<long>("IncidentReferenceSequence").StartsAt(1).IncrementsBy(1);
        var incident=Record<OperationalIncident>(model,"Incident");incident.ToTable(t=>t.UseSqlOutputClause(false));
        Text(incident,("ProductCode",80),("Reference",40),("State",20));
        incident.HasOne<Policy>().WithMany().HasForeignKey(x=>x.PolicyId).OnDelete(DeleteBehavior.NoAction);
        incident.HasOne<IncidentRevision>().WithMany().HasForeignKey(x=>x.CurrentRevisionId).OnDelete(DeleteBehavior.NoAction);
        incident.HasOne<IncidentOccurrenceRecord>().WithMany().HasForeignKey(x=>x.CurrentResolutionId).OnDelete(DeleteBehavior.NoAction);
        incident.HasIndex(x=>x.Reference).IsUnique();incident.HasIndex(x=>new{x.PolicyId,x.CreatedAt,x.Id});
        Check(incident,"State","[State] IN ('draft','logged','queued','handed-off','failed')");
        Check(incident,"Product","[ProductCode] IN ('motor-trade-road-risks','motor-trade-combined','commercial-combined')");
        Check(incident,"Creator","[CreatedBy] IS NOT NULL AND LEN(TRIM([Reference]))>0");
        var revision=Record<IncidentRevision>(model,"IncidentRevision");revision.ToTable(t=>t.UseSqlOutputClause(false));
        Text(revision,("ContentHash",64),("Reason",1000),("AuthorLabel",300));
        revision.Property(x=>x.DraftJson).IsRequired();revision.HasOne<OperationalIncident>().WithMany().HasForeignKey(x=>x.IncidentId).OnDelete(DeleteBehavior.NoAction);
        revision.HasIndex(x=>new{x.IncidentId,x.Number}).IsUnique();
        Check(revision,"Content","[CreatedBy] IS NOT NULL AND [Number]>0 AND ISJSON([DraftJson])=1 AND DATALENGTH([DraftJson])<=131072 AND LEN([ContentHash])=64 AND LEN(TRIM([Reason]))>0 AND LEN(TRIM([AuthorLabel]))>0");
        var resolution=Record<IncidentOccurrenceRecord>(model,"IncidentOccurrenceResolution");resolution.ToTable(t=>t.UseSqlOutputClause(false));
        Text(resolution,("State",30),("ContentHash",64));resolution.Property(x=>x.ResolutionJson).IsRequired();
        resolution.HasOne<OperationalIncident>().WithMany().HasForeignKey(x=>x.IncidentId).OnDelete(DeleteBehavior.NoAction);
        resolution.HasOne<IncidentRevision>().WithMany().HasForeignKey(x=>x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        resolution.HasIndex(x=>new{x.RevisionId,x.CreatedAt,x.Id});
        Check(resolution,"Content","[CreatedBy] IS NOT NULL AND ISJSON([ResolutionJson])=1 AND LEN([ContentHash])=64 AND [KnownAt]<=[CreatedAt]");
        Check(resolution,"State","[State] IN ('resolved','ambiguous','partly-uncovered','uncovered','incomplete')");
        var source=Record<IncidentResolutionSource>(model,"IncidentResolutionSource");source.ToTable(t=>t.UseSqlOutputClause(false));Text(source,("SourceHash",64));
        source.HasOne<IncidentOccurrenceRecord>().WithMany().HasForeignKey(x=>x.ResolutionId).OnDelete(DeleteBehavior.NoAction);
        source.HasOne<PolicyVersion>().WithMany().HasForeignKey(x=>x.VersionId).OnDelete(DeleteBehavior.NoAction);
        source.HasIndex(x=>new{x.ResolutionId,x.VersionId,x.From,x.To}).IsUnique();
        Check(source,"Interval","[From]<=[To] AND LEN([SourceHash])=64 AND [CreatedBy] IS NOT NULL");
        var evidence=Record<IncidentEvidence>(model,"IncidentEvidence");evidence.ToTable(t=>t.UseSqlOutputClause(false));
        evidence.HasOne<IncidentRevision>().WithMany().HasForeignKey(x=>x.RevisionId).OnDelete(DeleteBehavior.NoAction);
        evidence.HasOne<DocumentVersion>().WithMany().HasForeignKey(x=>x.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
        evidence.HasIndex(x=>new{x.RevisionId,x.DocumentVersionId}).IsUnique();Check(evidence,"Creator","[CreatedBy] IS NOT NULL");
    }
}
