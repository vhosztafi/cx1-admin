using System.Text.Json;
namespace BackOffice.Application.Agencies;

public static class AgencyDistributionRules
{
    // A malformed current rule must fail closed rather than fall back to an older grant.
    public static IReadOnlySet<Guid>? Parse(string json)
    {
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return null;
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in root.EnumerateObject())
                if(!names.Add(property.Name)||property.Name is not ("demo" or "kind" or "productVersionIds"))return null;
            if(names.Count!=3||root.GetProperty("demo").ValueKind!=JsonValueKind.True||root.GetProperty("kind").ValueKind!=JsonValueKind.String||root.GetProperty("kind").GetString()!="agency-distribution")return null;
            var products=root.GetProperty("productVersionIds");if(products.ValueKind!=JsonValueKind.Array||products.GetArrayLength()>3)return null;
            var ids=new HashSet<Guid>();
            foreach(var product in products.EnumerateArray())
                if(product.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(product.GetString(),"D",out var id)||id==Guid.Empty||!ids.Add(id))return null;
            return ids;
        }
        catch(JsonException){return null;}
    }
}
