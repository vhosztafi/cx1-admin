using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Json.Schema;

namespace BackOffice.Application.Operations;

public sealed class IncidentRuleException(string code):Exception("Review the incident details.")
{ public string Code {get;}=code; }
public static class IncidentRules
{
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=false,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    private static readonly Lazy<JsonSchema> Schema=new(()=>
    {
        using var stream=typeof(IncidentRules).Assembly.GetManifestResourceStream("Operations.RuntimeSchema")??throw new InvalidOperationException("Incident contract is missing.");
        var schema=JsonNode.Parse(stream)!;schema["$ref"]="#/$defs/OpsIncidentDraftWrite";
        return JsonSchema.FromText(schema.ToJsonString(),new BuildOptions{SchemaRegistry=new SchemaRegistry{Fetch=(_,_)=>throw new InvalidOperationException("External incident schema resolution is forbidden.")}});
    });
    public static void Draft(JsonElement draft)
    {
        if(draft.ValueKind!=JsonValueKind.Object||Encoding.UTF8.GetByteCount(draft.GetRawText())>65536||!Unique(draft)||
            !Schema.Value.Evaluate(draft,new EvaluationOptions{RequireFormatValidation=true}).IsValid||draft.GetProperty("policyId").GetGuid()==Guid.Empty)
            throw new IncidentRuleException("incident-fields-invalid");
        if(draft.TryGetProperty("occurrence",out var observed))
        {
            try{IncidentOccurrenceRules.Window(observed.Deserialize<IncidentOccurrence>(Json)!);}
            catch(Exception error)when(error is JsonException or IncidentOccurrenceException){throw new IncidentRuleException("incident-occurrence-invalid");}
        }
        var thirdParty=Text(draft,"thirdPartyInvolvement");
        if(thirdParty is not("yes" or "unknown")&&(draft.TryGetProperty("thirdPartyName",out _)||draft.TryGetProperty("thirdPartyInsurerOrRegistration",out _)))
            throw new IncidentRuleException("incident-third-party-inactive");
    }
    public static IReadOnlyList<string> Missing(JsonElement draft,string resolutionState,bool subjectReady)
    {
        Draft(draft);var missing=new List<string>();
        foreach(var field in new[]{"occurrence","kind","thirdPartyInvolvement","reportedBy","reportingRoute","bestContactDescription"})if(!draft.TryGetProperty(field,out _))missing.Add(field);
        if(Text(draft,"description") is not string description||description.Trim().Length<20)missing.Add("description");
        if(!subjectReady)missing.Add("subject");
        if(resolutionState!="resolved")missing.Add("historical-cover");
        return missing;
    }
    public static string? Text(JsonElement value,string name)=>value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString():null;
    private static bool Unique(JsonElement value)=>value.ValueKind switch
    {
        JsonValueKind.Object=>value.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()==value.EnumerateObject().Count()&&value.EnumerateObject().All(x=>Unique(x.Value)),
        JsonValueKind.Array=>value.EnumerateArray().All(Unique),_=>true
    };
}
