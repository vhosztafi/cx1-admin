using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace BackOffice.Infrastructure.Operations;

internal sealed record ClaimsEvidence(Guid VersionId,string Name,string MediaType,long Length,string Sha256);
internal sealed record ClaimsSnapshot(string Format,Guid IncidentId,Guid PolicyId,Guid RevisionId,string RevisionHash,Guid ResolutionId,string ResolutionHash,
    Guid SourceVersionId,string SourceHash,Guid AdministratorId,string AdministratorName,JsonElement Facts,JsonElement Resolution,JsonElement? Commercial,ClaimsEvidence[] Evidence,int WithheldEvidenceCount);
internal static class ClaimsSnapshots
{
    internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    internal static string Serialize<T>(T value)=>JsonSerializer.Serialize(value,Json);
    internal static async Task<ClaimsSnapshot> Capture(BackOfficeDbContext db,ActorContext actor,OperationalIncident incident,Guid revisionId,Guid resolutionId,Guid providerId,IncidentOccurrenceResolver resolver,CancellationToken token)
    {
        await OperationalScope.HoldParents(db,actor,[new("policy",incident.PolicyId)],"incident-handoff",token);
        var admin=await db.Set<ClaimsAdministrator>().FromSqlInterpolated($"SELECT * FROM ClaimsAdministrator WITH(HOLDLOCK,ROWLOCK) WHERE Id={providerId}").AsNoTracking().SingleOrDefaultAsync(token);
        if(admin is not{State:"active"})throw new OperationalAccessException(422,"claims-administrator-unavailable");
        var revision=await db.Set<IncidentRevision>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==revisionId&&x.IncidentId==incident.Id,token)??throw Missing();
        var resolution=await db.Set<IncidentOccurrenceRecord>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==resolutionId&&x.IncidentId==incident.Id&&x.RevisionId==revisionId,token)??throw Missing();
        if(resolution.State!="resolved"||DeliverySnapshots.Hash(revision.DraftJson)!=revision.ContentHash||DeliverySnapshots.Hash(resolution.ResolutionJson)!=resolution.ContentHash)throw Invalid();
        using var facts=JsonDocument.Parse(revision.DraftJson);var draft=facts.RootElement;IncidentRules.Draft(draft);
        var subject=draft.GetProperty(incident.ProductCode=="commercial-combined"?"commercialSubject":"motorSubject");
        var occurrence=draft.GetProperty("occurrence").Deserialize<IncidentOccurrence>(IncidentRules.Json)!;
        var checkedResolution=await resolver.ResolveHeld(db,actor,incident.PolicyId,occurrence,resolution.KnownAt,token,subject);
        if(IncidentRules.Missing(draft,checkedResolution.State,checkedResolution.State=="resolved").Count!=0)throw new OperationalAccessException(422,"incident-not-ready");
        var sources=await db.Set<IncidentResolutionSource>().AsNoTracking().Where(x=>x.ResolutionId==resolutionId).ToArrayAsync(token);
        if(sources.Length==0||sources.Select(x=>x.VersionId).Distinct().Count()!=1||sources.Length!=checkedResolution.Candidates.Count||
            sources.Any(x=>!checkedResolution.Candidates.Any(c=>c.VersionId==x.VersionId&&c.SourceHash==x.SourceHash&&c.From==x.From&&c.To==x.To)))throw Invalid();
        var source=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==sources[0].VersionId&&x.PolicyId==incident.PolicyId,token);
        using var resolved=JsonDocument.Parse(resolution.ResolutionJson);
        if(resolved.RootElement.GetProperty("sourceHash").GetString()!=sources[0].SourceHash)throw Invalid();
        JsonElement? commercial=null;
        if(incident.ProductCode=="commercial-combined")
        {
            using var snapshot=JsonDocument.Parse(source.SnapshotJson);var term=snapshot.RootElement.GetProperty("term");var provenance=snapshot.RootElement.GetProperty("provenance");
            var from=provenance.TryGetProperty("effectiveAt",out var at)?at.GetDateTimeOffset():term.GetProperty("startsAt").GetDateTimeOffset();
            commercial=CommercialIncidentPayload.CreateResolved(new(incident.PolicyId,source.Id,sources[0].SourceHash,source.SnapshotJson,from,term.GetProperty("endsAt").GetDateTimeOffset()),occurrence,resolution.KnownAt,subject);
        }
        var evidence=new List<ClaimsEvidence>();var withheld=0;
        var ids=await db.Set<IncidentEvidence>().Where(x=>x.RevisionId==revisionId).Select(x=>x.DocumentVersionId).Order().ToArrayAsync(token);
        foreach(var id in ids)
        {
            var row=await(from v in db.Set<DocumentVersion>() join d in db.Set<OperationalDocument>() on v.DocumentId equals d.Id
                join parent in db.Set<OperationalSubject>() on d.SubjectId equals parent.Id where v.Id==id select new{v,d,parent}).AsNoTracking().SingleAsync(token);
            if(row.parent.Kind!="policy"||row.parent.PolicyId!=incident.PolicyId)throw Missing();
            await DocumentService.AttachmentVersion(db,actor,row.parent.Id,id,true,token);
            // Only explicitly insurer-visible evidence is disclosed to the administrator.
            // Internal and agency-only documents stay local; no storage locator enters the projection.
            if(row.d.Visibility!="insurer"){withheld++;continue;}
            var file=await(from c in db.Set<DocumentVersionContent>() join f in db.Set<FileObject>() on c.FileObjectId equals f.Id where c.VersionId==id select f).AsNoTracking().SingleAsync(token);
            evidence.Add(new(id,row.v.OriginalName,file.MediaType,file.ByteLength,file.Sha256));
        }
        var projected=JsonNode.Parse(revision.DraftJson)!;projected.AsObject().Remove("evidenceDocumentVersionIds");
        return new("claims-handoff-2",incident.Id,incident.PolicyId,revision.Id,revision.ContentHash,resolution.Id,resolution.ContentHash,source.Id,sources[0].SourceHash,
            admin.Id,admin.Name,JsonSerializer.SerializeToElement(projected,Json),resolved.RootElement.Clone(),commercial,evidence.ToArray(),withheld);
    }
    internal static OperationalAccessException Missing()=>new(404,"claims-record-not-found");
    internal static OperationalAccessException Invalid()=>new(409,"claims-source-invalid");
}
