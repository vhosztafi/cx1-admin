using System.Globalization;
using System.Text.Json;

namespace BackOffice.Application.Operations;

public static class IncidentSubjectRules
{
    // Missing selections can remain draft facts. Supplied identities must always
    // belong to the selected retained snapshot, even before handoff is ready.
    public static bool Ready(JsonElement snapshot,JsonElement subject)
    {
        if(subject.ValueKind!=JsonValueKind.Object||subject.EnumerateObject().Select(x=>x.Name).Distinct(StringComparer.Ordinal).Count()!=subject.EnumerateObject().Count())throw Invalid();
        var kind=Text(subject,"kind");if(kind is null)throw Invalid();
        var risk=snapshot.GetProperty("risk");var commercial=snapshot.GetProperty("productCode").GetString()=="commercial-combined";
        string[] allowed=commercial?kind switch
        {
            "property"=>["kind","locationId","coverCode","itemDescription","owner","estimatedValueAtRisk"],
            "liability"=>["kind","coverCode","locationId","occupationId","itemDescription"],_=>throw Invalid()
        }:kind switch
        {
            "registered-vehicle"=>["kind","vehicleId","driverId","driverDeclaration","drivable"],
            "unregistered-vehicle"=>["kind","registration","driverDeclaration","drivable"],
            "stock-or-customer-vehicle" or "premises" or "third-party-only"=>["kind","itemDescription","owner","estimatedValueAtRisk"],_=>throw Invalid()
        };
        if(subject.EnumerateObject().Any(x=>!allowed.Contains(x.Name,StringComparer.Ordinal)))throw Invalid();
        foreach(var name in new[]{"itemDescription","registration","owner","driverDeclaration","drivable"})if(subject.TryGetProperty(name,out var value)&&
            (value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString())||value.GetString()!.Length>(name=="itemDescription"?1000:name=="registration"?20:30)))throw Invalid();
        if(subject.TryGetProperty("owner",out var owner)&&owner.GetString() is not("insured" or "customer" or "third-party"))throw Invalid();
        if(subject.TryGetProperty("estimatedValueAtRisk",out var amount)&&(amount.ValueKind!=JsonValueKind.String||!decimal.TryParse(amount.GetString(),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var money)||money<0))throw Invalid();
        if(!commercial)
        {
            var vehicle=Owned(subject,"vehicleId",risk,"vehicles");var driver=Owned(subject,"driverId",risk,"drivers");
            var declaration=Text(subject,"driverDeclaration");
            if(declaration is not(null or "named" or "not-named" or "unknown")||kind=="unregistered-vehicle"&&declaration=="named")throw Invalid();
            if(Text(subject,"drivable") is not(null or "yes" or "no-recovered" or "unknown")||driver is not null&&declaration!="named")throw Invalid();
            return kind!="registered-vehicle"||vehicle is not null&&(declaration!="named"||driver is not null);
        }
        var location=Owned(subject,"locationId",risk,"locations");var occupation=Owned(subject,"occupationId",risk,"wages");var code=Text(subject,"coverCode");
        if(code is null)return false;
        var sections=snapshot.GetProperty("cover").GetProperty("sections").EnumerateArray().ToArray();
        if(kind=="property")
        {
            if(code is not("buildings" or "contents" or "stock" or "business-interruption"))throw Invalid();
            if(location is null)return false;
            var sectionCode=code=="business-interruption"?code:"property";
            if(!sections.Any(x=>x.GetProperty("code").GetString()==sectionCode&&(sectionCode=="business-interruption"||x.GetProperty("targetIds").EnumerateArray().Any(y=>y.GetGuid()==location))))throw Invalid();
            if(code!="business-interruption")
            {
                var row=risk.GetProperty("locations").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==location);
                if(!decimal.TryParse(row.GetProperty(code).GetString(),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var selected)||selected<=0)throw Invalid();
            }
        }
        else if(code is not("employers-liability" or "public-liability" or "products-liability")||!sections.Any(x=>x.GetProperty("code").GetString()==code)||occupation is not null&&code!="employers-liability")throw Invalid();
        return true;
    }
    private static Guid? Owned(JsonElement subject,string name,JsonElement risk,string collection)
    {
        if(!subject.TryGetProperty(name,out var value))return null;
        if(value.ValueKind!=JsonValueKind.String||!value.TryGetGuid(out var id)||id==Guid.Empty||!risk.TryGetProperty(collection,out var rows)||!rows.EnumerateArray().Any(x=>x.GetProperty("id").GetGuid()==id))throw Invalid();
        return id;
    }
    private static string? Text(JsonElement value,string name)
    {
        if(!value.TryGetProperty(name,out var property))return null;
        if(property.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(property.GetString()))throw Invalid();
        return property.GetString();
    }
    private static IncidentOccurrenceException Invalid()=>new("incident-subject-invalid");
}
