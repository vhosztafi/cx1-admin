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
            id=x.GetProperty("id").GetGuid(),label=ChoiceLabel(x,labels) is string label&&label.Length>0?label:$"{collection} {index+1}"
        }).ToArray():[];
        var sectionRows=snapshot.GetProperty("cover").GetProperty("sections").EnumerateArray().ToArray();
        var sections=sectionRows.Select(x=>x.GetProperty("code").GetString()!).ToArray();
        var reference=await db.Set<Policy>().Where(x=>x.Id==row.PolicyId).Select(x=>x.Reference).SingleAsync(token);
        string? Optional(JsonElement value,string name)=>value.TryGetProperty(name,out var found)&&found.ValueKind==JsonValueKind.String?found.GetString():null;
        var insured=snapshot.GetProperty("insured");
        var insuredName=Optional(insured,"legalName")??ChoiceLabel(insured,["firstName","surname"]).Replace(" · "," ",StringComparison.Ordinal);
        var policyContext=new{reference,insuredName=string.IsNullOrWhiteSpace(insuredName)?"Not recorded in this policy version":insuredName,
            termPremium=Optional(snapshot.GetProperty("premium"),"termPremium"),termEndsAt=snapshot.GetProperty("term").GetProperty("endsAt").GetString(),
            href=$"/policies/{row.PolicyId}?termId={version.TermId}&versionId={version.Id}&tab=Transactions",
            sections=sectionRows.Select(x=>new{code=x.GetProperty("code").GetString(),coverLevel=Optional(x,"coverLevel"),limit=Optional(x,"limit"),excess=Optional(x,"excess")}).ToArray()};
        var result=new{incidentId=id,revisionId=row.CurrentRevisionId,resolutionId,versionId,sourceHash=source.SourceHash,policyContext,
            vehicles=Choices("vehicles","registration","make","model"),drivers=Choices("drivers","firstName","surname"),locations=Choices("locations","reference","name","address.line1","address.postcode"),occupations=Choices("wages","category","occupation"),coverCodes=sections};
        await transaction.CommitAsync(token);return new(id,200,JsonSerializer.Serialize(result,Json),Etag:TaskService.Etag(row.RowVersion));
    }
    private static string ChoiceLabel(JsonElement row,IEnumerable<string> paths)
    {
        var labels=new List<string>();
        foreach(var path in paths)
        {
            var value=row;foreach(var part in path.Split('.')){if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty(part,out var found)){value=default;break;}value=found;}
            if(value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("label",out var label))value=label;
            if(value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString()))labels.Add(value.GetString()!);
        }
        var joined=string.Join(" · ",labels.Distinct(StringComparer.Ordinal));return joined[..Math.Min(joined.Length,1000)];
    }
}
