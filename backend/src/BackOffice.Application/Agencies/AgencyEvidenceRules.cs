using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BackOffice.Application.Agencies;

public sealed record AgencyFileInput(string FileName,string ContentType,byte[] Content,string Sha256);
public sealed record AgencyEvidenceFact(Guid Id,string Kind,string State,string InputFingerprint,Guid RuleVersionId,DateOnly? ExpiresOn);
public sealed record AgencyChecklistItem(string Code,string Path,int Stage,string State,string Message,Guid? EvidenceId=null);

public static partial class AgencyEvidenceRules
{
    public const int MaximumFileBytes=10*1024*1024;
    public static readonly string[] CheckKinds=["fca","financial-check","sanctions","ownership"];
    public static readonly string[] AttestationKinds=["toba","professional-indemnity","dpa","client-money"];
    private static readonly string[] IdentityPaths=["legalName","entityType","companyNumber","address","regulatoryReference","regulatoryStatus","principalFirm"];
    public static bool IsKind(string kind)=>CheckKinds.Contains(kind)||AttestationKinds.Contains(kind);

    public static AgencyFileInput ValidateFile(string name,string contentType,byte[] content)
    {
        if(content.Length is 0 or >MaximumFileBytes)throw new AgencyCommandException(413,"evidence-file-size");
        if(string.IsNullOrWhiteSpace(name)||name.Length>150||name!=name.Trim()||name.StartsWith('.')||name.EndsWith('.')||name.Any(c=>char.IsControl(c)||"/\\:<>\"|?*".Contains(c)))
            throw new AgencyCommandException(422,"evidence-file-name");
        var extension=Path.GetExtension(name).ToLowerInvariant();
        var bytes=content.AsSpan();
        var valid=contentType switch
        {
            "application/pdf"=>extension==".pdf"&&bytes.StartsWith("%PDF-"u8),
            "image/png"=>extension==".png"&&bytes.StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
            "image/jpeg"=>(extension is ".jpg" or ".jpeg")&&bytes.Length>=4&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255&&bytes[^2]==255&&bytes[^1]==217,
            "text/plain"=>extension==".txt"&&ValidText(content),
            _=>false
        };
        if(!valid)throw new AgencyCommandException(422,"evidence-file-type");
        // Signature/type checks are a bounded demo screen, not a malware scan.
        return new(name,contentType,content,Convert.ToHexStringLower(SHA256.HashData(content)));
    }
    private static bool ValidText(byte[] bytes)
    {
        try {var text=new UTF8Encoding(false,true).GetString(bytes);return !text.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n' or '\t'))&&!text.TrimStart('\uFEFF',' ','\r','\n','\t').StartsWith('<');}
        catch(DecoderFallbackException){return false;}
    }
    public static string Snapshot(JsonElement details,string kind)
    {
        if(!IsKind(kind))throw new AgencyCommandException(422,"evidence-kind");
        var paths=IdentityPaths.Concat(kind switch
        {
            "fca"=>["arrangesGeneralInsurance","territory","clientMoneyBasis"],
            "toba"=>["compliance.tobaStatus","compliance.tobaVersion","compliance.tobaSignedOn"],
            "professional-indemnity"=>["compliance.professionalIndemnityStatus","compliance.professionalIndemnityLimit","compliance.piExpiresOn"],
            "dpa"=>["compliance.dataProcessingAgreement"],
            "client-money"=>["clientMoneyBasis"],
            _=>Array.Empty<string>()
        });
        var result=new JsonObject();
        foreach(var path in paths.Order(StringComparer.Ordinal))if(Value(details,path) is JsonElement value)result[path]=Canonical(value);
        return result.ToJsonString();
    }
    private static JsonNode? Canonical(JsonElement value)=>value.ValueKind==JsonValueKind.Object
        ?new JsonObject(value.EnumerateObject().OrderBy(x=>x.Name,StringComparer.Ordinal).Select(x=>new KeyValuePair<string,JsonNode?>(x.Name,Canonical(x.Value))))
        :JsonNode.Parse(value.GetRawText());
    public static string Fingerprint(JsonElement details,string kind)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(details,kind))));
    public static JsonElement? Value(JsonElement details,string path)
    {
        var value=details;foreach(var part in path.Split('.')){if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty(part,out var next))return null;value=next;}return value;
    }
    public static string? Text(JsonElement details,string path)=>Value(details,path) is {ValueKind:JsonValueKind.String} value?value.GetString():null;
    public static bool Present(JsonElement details,string path)=>Value(details,path) is JsonElement value&&value.ValueKind!=JsonValueKind.Null&&!(value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(value.GetString()));

    public static string DemoCheck(JsonElement details,string kind,string scenario)
    {
        if(!CheckKinds.Contains(kind))throw new AgencyCommandException(422,"evidence-check-kind");
        if(scenario is not ("pass" or "refer" or "unavailable"))throw new AgencyCommandException(503,"evidence-demo-configuration");
        if(scenario!="pass")return scenario;
        if(!Present(details,"legalName"))return "refer";
        if(kind=="fca"&&(!FcaReference().IsMatch(Text(details,"regulatoryReference")??"")||Text(details,"arrangesGeneralInsurance")!="confirmed"||Text(details,"regulatoryStatus") is not ("directly-authorised" or "appointed-representative")||Text(details,"regulatoryStatus")=="appointed-representative"&&!Present(details,"principalFirm")))return "refer";
        return "pass";
    }
    public static bool AttestationValid(JsonElement details,string kind,DateOnly today,decimal minimumPi,string tobaVersion,DateOnly? expiresOn)
    {
        if(!AttestationKinds.Contains(kind))throw new AgencyCommandException(422,"evidence-attestation-kind");
        if(expiresOn is DateOnly expiry&&expiry<today)return false;
        return kind switch
        {
            "toba"=>Text(details,"compliance.tobaStatus")=="signed"&&Text(details,"compliance.tobaVersion")==tobaVersion&&DateOnly.TryParseExact(Text(details,"compliance.tobaSignedOn"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var signed)&&signed<=today,
            "professional-indemnity"=>Text(details,"compliance.professionalIndemnityStatus")=="meets-minimum"&&decimal.TryParse(Text(details,"compliance.professionalIndemnityLimit"),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var limit)&&limit>=minimumPi&&DateOnly.TryParseExact(Text(details,"compliance.piExpiresOn"),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var piExpiry)&&piExpiry>=today&&expiresOn==piExpiry,
            "dpa"=>Text(details,"compliance.dataProcessingAgreement")=="signed",
            "client-money"=>Text(details,"clientMoneyBasis")=="cass5-client-money",
            _=>false
        };
    }
    public static AgencyChecklistItem EvidenceItem(JsonElement details,string kind,AgencyEvidenceFact? latest,Guid rule,DateOnly today)
    {
        var state=latest is null?"missing":latest.InputFingerprint!=Fingerprint(details,kind)||latest.RuleVersionId!=rule?"stale":latest.ExpiresOn is DateOnly expiry&&expiry<today?"expired":latest.State=="verified"?"satisfied":latest.State=="rejected"?"failed":latest.State=="unavailable"?"unavailable":"pending";
        var label=kind.Replace('-',' ');
        return new("evidence-"+kind,"/evidence/"+kind,kind=="fca"?1:4,state,state=="satisfied"?label+" has current demo verification.":label+" evidence is "+state+". Record current evidence or run the demo check.",latest?.Id);
    }
    [GeneratedRegex("^[0-9]{6,7}$",RegexOptions.CultureInvariant)]private static partial Regex FcaReference();
}
