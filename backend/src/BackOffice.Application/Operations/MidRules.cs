using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace BackOffice.Application.Operations;
public sealed class MidRuleException(string code):Exception(code);
public sealed record MidItem(Guid RiskItemId,string Kind,string Registration,string Action,DateTimeOffset EffectiveAt,DateTimeOffset EndsAt);
public static class MidRules
{
    private sealed record Item(Guid Id,string Kind,string Registration,string Fingerprint);
    // The caller provides the retained predecessor, never the policy's current pointer.
    public static IReadOnlyList<MidItem> Items(JsonElement source,JsonElement? basis,string purpose,DateTimeOffset effectiveAt,DateTimeOffset endsAt)
    {
        if(purpose is not("new-business" or "adjustment" or "renewal" or "cancellation")||effectiveAt>=endsAt||
            purpose=="new-business"&&basis is not null||purpose!="new-business"&&basis is null)throw Invalid();
        var current=Read(source);var previous=basis is{} old?Read(old):[];
        var changes=new List<MidItem>();
        void Add(Item row,string action)=>changes.Add(new(row.Id,row.Kind,row.Registration,action,effectiveAt,endsAt));
        foreach(var row in previous)
        {
            var next=current.SingleOrDefault(x=>x.Id==row.Id&&x.Kind==row.Kind);
            if(purpose=="cancellation"||next is null||next.Registration!=row.Registration)Add(row,"remove");
        }
        if(purpose!="cancellation")foreach(var row in current)
        {
            var prior=previous.SingleOrDefault(x=>x.Id==row.Id&&x.Kind==row.Kind);
            if(prior is null||prior.Registration!=row.Registration)Add(row,"add");
            else if(purpose=="renewal"||prior.Fingerprint!=row.Fingerprint||Cover(source)!=Cover(basis!.Value))Add(row,"change");
        }
        return changes.OrderBy(x=>x.Action=="remove"?0:1).ThenBy(x=>x.Kind,StringComparer.Ordinal).ThenBy(x=>x.RiskItemId).ToArray();
    }
    private static Item[] Read(JsonElement source)
    {
        try
        {
            if(source.GetProperty("productCode").GetString() is not("motor-trade-road-risks" or "motor-trade-combined"))throw Invalid();
            var risk=source.GetProperty("risk");var rows=new List<Item>();var ids=new HashSet<Guid>();var registrations=new HashSet<string>(StringComparer.Ordinal);
            void Add(JsonElement row,string kind,string field)
            {
                var id=row.GetProperty("id").GetGuid();var registration=Normalize(row.GetProperty(field).GetString()??"");
                if(id==Guid.Empty||!ids.Add(id)||!Regex.IsMatch(registration,"^[A-Z0-9]{1,20}$",RegexOptions.CultureInvariant)||!registrations.Add(registration))throw Invalid();
                var normalized=JsonNode.Parse(row.GetRawText())!.AsObject();normalized[field]=registration;
                rows.Add(new(id,kind,registration,Canonical(normalized)));
            }
            if(risk.TryGetProperty("vehicles",out var vehicles))foreach(var row in vehicles.EnumerateArray())
            {
                var report=Answer(row,"prototype.addveh.report-mid");
                if(report is not(JsonValueKind.True or JsonValueKind.False))throw Invalid();
                if(report==JsonValueKind.True)Add(row,"vehicle","registration");
            }
            if(risk.TryGetProperty("tradePlates",out var plates)&&plates.GetArrayLength()>0)
            {
                if(Answer(risk,"MTS-07-Q01")!=JsonValueKind.True)throw Invalid();
                foreach(var row in plates.EnumerateArray())Add(row,"trade-plate","number");
            }
            if(rows.Count>1000)throw Invalid();return rows.ToArray();
        }
        catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){throw Invalid();}
    }
    private static JsonValueKind Answer(JsonElement holder,string id)
    {
        if(!holder.TryGetProperty("responses",out var responses)||!responses.TryGetProperty("answers",out var answers))return JsonValueKind.Undefined;
        var matched=answers.EnumerateArray().Where(x=>x.GetProperty("questionId").GetString()==id).ToArray();
        return matched.Length==1?matched[0].GetProperty("value").ValueKind:JsonValueKind.Undefined;
    }
    private static string Normalize(string value)=>value.Replace(" ","",StringComparison.Ordinal).ToUpperInvariant();
    private static string Cover(JsonElement value)=>value.TryGetProperty("cover",out var cover)?Canonical(JsonNode.Parse(cover.GetRawText())):"null";
    private static string Canonical(JsonNode? node)=>node switch
    {
        JsonObject o=>"{"+string.Join(',',o.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>JsonSerializer.Serialize(x.Key)+":"+Canonical(x.Value)))+"}",
        JsonArray a=>"["+string.Join(',',a.Select(Canonical))+"]",_=>node?.ToJsonString()??"null"
    };
    private static MidRuleException Invalid()=>new("mid-source-invalid");
}
