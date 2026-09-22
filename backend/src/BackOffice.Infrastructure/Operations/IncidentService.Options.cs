using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class IncidentService
{
    public async Task<CommandOutcome> SubjectOptions(ActorContext actor,Guid id,Guid versionId,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var transaction=await db.Database.BeginTransactionAsync(token);
        var hint=await db.Set<OperationalIncident>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw Missing();
        await HoldPolicy(db,actor,hint.PolicyId,"incident-read",token);
        var row=await db.Set<OperationalIncident>().AsNoTracking().SingleAsync(x=>x.Id==id,token);
        if(row.CurrentResolutionId is not Guid resolutionId)throw new OperationalAccessException(409,"incident-resolution-required");
        var source=await db.Set<IncidentResolutionSource>().AsNoTracking().FirstOrDefaultAsync(x=>x.ResolutionId==resolutionId&&x.VersionId==versionId,token)??throw Missing();
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==source.VersionId&&x.PolicyId==row.PolicyId,token);
        if(Hash(version.SnapshotJson)!=source.SourceHash)throw new IncidentOccurrenceException("occurrence-source-invalid");
        using var document=JsonDocument.Parse(version.SnapshotJson);var snapshot=document.RootElement;var risk=snapshot.GetProperty("risk");
        object[] Choices(string collection,params string[] labels)=>risk.TryGetProperty(collection,out var values)?values.EnumerateArray().Select((x,index)=>(object)new{
            id=x.GetProperty("id").GetGuid(),label=string.Join(" · ",labels.Where(name=>x.TryGetProperty(name,out _)).Select(name=>x.GetProperty(name).ToString())) is string label&&label.Length>0?label:$"{collection} {index+1}"
        }).ToArray():[];
        var sections=snapshot.GetProperty("cover").GetProperty("sections").EnumerateArray().Select(x=>x.GetProperty("code").GetString()!).ToArray();
        var result=new{incidentId=id,revisionId=row.CurrentRevisionId,resolutionId,versionId,sourceHash=source.SourceHash,
            vehicles=Choices("vehicles","registration","make","model"),drivers=Choices("drivers","firstName","surname"),locations=Choices("locations","name","addressLine1","postcode"),occupations=Choices("wages","occupation"),coverCodes=sections};
        await transaction.CommitAsync(token);return new(id,200,JsonSerializer.Serialize(result,Json),Etag:TaskService.Etag(row.RowVersion));
    }
}
