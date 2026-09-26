using System.Text.Json;
namespace BackOffice.Infrastructure.Administration;
public static class AuditDisclosure
{
    private static readonly HashSet<string> Fields=new(StringComparer.OrdinalIgnoreCase)
    {"id","userId","resourceId","requestId","deliveryId","productId","productVersionId","providerId","authorityVersionId","binderVersionId","teamId","routingTeamId","state","status","scope","version","scenario","attemptLimit","attempts","workId","operationId","name","displayName","email","roles","resetMfa","code","kind","effectiveFrom","effectiveTo","coverSections","enabled","maximumReviewDays","agencySharingAllowed","notificationsEnabled","clientReferencePrefix","duplicateQuotePolicy","requireReview","priority","initialState","dueDays","leadDays","assignmentTeamId","values","etag"};
    public static object? Read(string? json)
    {
        if(string.IsNullOrEmpty(json)||json.Length>65536)return null;
        try{using var doc=JsonDocument.Parse(json);return Filter(doc.RootElement,0);}catch(JsonException){return null;}
    }
    private static object? Filter(JsonElement value,int depth)
    {
        if(depth>5)return null;
        return value.ValueKind switch
        {
            JsonValueKind.Object=>value.EnumerateObject().Where(x=>Fields.Contains(x.Name)&&!x.Name.Equals("code",StringComparison.OrdinalIgnoreCase)).Take(50).GroupBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>Filter(x.Last().Value,depth+1)),
            JsonValueKind.Array=>value.EnumerateArray().Take(50).Select(x=>Filter(x,depth+1)).ToArray(),
            JsonValueKind.String=>value.GetString() is {Length:<=254} s?s:"[long value omitted]",
            JsonValueKind.Number=>value.TryGetDecimal(out var n)?n:null,
            JsonValueKind.True=>true,JsonValueKind.False=>false,_=>null
        };
    }
}
