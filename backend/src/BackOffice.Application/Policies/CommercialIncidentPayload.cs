using System.Text.Json;

namespace BackOffice.Application.Policies;

public static class CommercialIncidentPayload
{
    public static JsonElement CreateResolved(CommercialPayloadSource source,BackOffice.Application.Operations.IncidentOccurrence occurrence,DateTimeOffset knownAt,JsonElement subject)
    {
        try
        {
            var snapshot=source.Read();var window=BackOffice.Application.Operations.IncidentOccurrenceRules.Window(occurrence);
            if(snapshot.GetProperty("snapshotFormat").GetString()=="issued-commercial-cancellation-1"||window.From<source.EffectiveFrom||
                (window.IsExact?window.From>=source.EffectiveUntil:window.To>source.EffectiveUntil)||window.To>knownAt||
                snapshot.GetProperty("provenance").TryGetProperty("processedAt",out var processed)&&processed.GetDateTimeOffset()>knownAt||
                !BackOffice.Application.Operations.IncidentSubjectRules.Ready(snapshot,subject))throw new ArgumentException("commercial-incident-resolution-not-applicable");
            return JsonSerializer.SerializeToElement(new{format="commercial-incident-2",policyId=source.PolicyId,versionId=source.VersionId,sourceContentHash=source.SourceContentHash,
                occurrence,applicability=window,knownAt,subject},new JsonSerializerOptions(JsonSerializerDefaults.Web){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});
        }
        catch(BackOffice.Application.Operations.IncidentOccurrenceException){throw new ArgumentException("commercial-incident-subject-invalid");}
    }
    public static JsonElement Create(CommercialPayloadSource source, DateTimeOffset occurredAt, DateTimeOffset knownAt, JsonElement subject)
    {
        var snapshot = source.Read();
        if (snapshot.GetProperty("snapshotFormat").GetString() == "issued-commercial-cancellation-1" ||
            occurredAt < source.EffectiveFrom || occurredAt >= source.EffectiveUntil || occurredAt > knownAt ||
            (snapshot.GetProperty("provenance").TryGetProperty("processedAt", out var processed) && processed.GetDateTimeOffset() > knownAt))
            throw new ArgumentException("commercial-incident-version-not-applicable");
        if (subject.ValueKind != JsonValueKind.Object || !CommercialPayloadSource.Unique(subject)) throw InvalidSubject();
        var kind = Text(subject, "kind", 20, true);
        var allowed = kind switch {
            "property" => new[] { "kind", "locationId", "damageDescription" },
            "liability" => new[] { "kind", "section", "locationId", "employeeOccupation", "thirdPartyDescription" },
            _ => throw InvalidSubject()
        };
        if (subject.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal))) throw InvalidSubject();
        var risk = snapshot.GetProperty("risk"); var sections = snapshot.GetProperty("cover").GetProperty("sections").EnumerateArray().ToArray();
        Guid? location = null;
        if (subject.TryGetProperty("locationId", out var locationValue))
        {
            if (locationValue.ValueKind != JsonValueKind.String || !locationValue.TryGetGuid(out var id) || id == Guid.Empty ||
                !risk.GetProperty("locations").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == id)) throw InvalidSubject();
            location = id;
        }
        if (kind == "property")
        {
            _ = Text(subject, "damageDescription", 4000, true);
            if (location is null || !sections.Any(x => x.GetProperty("code").GetString() == "property" && x.GetProperty("targetIds").EnumerateArray().Any(y => y.GetGuid() == location))) throw InvalidSubject();
        }
        else
        {
            var section = Text(subject, "section", 30, true);
            if (section is not ("employers-liability" or "public-liability" or "products-liability") || !sections.Any(x => x.GetProperty("code").GetString() == section)) throw InvalidSubject();
            var occupation = Text(subject, "employeeOccupation", 200, false);
            if (occupation is not null && (section != "employers-liability" || !risk.GetProperty("wages").EnumerateArray().Any(x => x.GetProperty("category").GetProperty("label").GetString() == occupation))) throw InvalidSubject();
            _ = Text(subject, "thirdPartyDescription", 2000, false);
        }
        return JsonSerializer.SerializeToElement(new { format = "commercial-incident-1", policyId = source.PolicyId, versionId = source.VersionId,
            sourceContentHash = source.SourceContentHash, occurredAt, subject });
    }

    public static bool Valid(JsonElement payload, CommercialPayloadSource source, DateTimeOffset knownAt)
    {
        try { return CommercialPayloadSource.Unique(payload) && JsonElement.DeepEquals(payload, Create(source, payload.GetProperty("occurredAt").GetDateTimeOffset(), knownAt, payload.GetProperty("subject"))); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or JsonException) { return false; }
    }

    private static string? Text(JsonElement value, string name, int max, bool required)
    {
        if (!value.TryGetProperty(name, out var property)) { if (required) throw InvalidSubject(); return null; }
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()) || property.GetString()!.Length > max) throw InvalidSubject();
        return property.GetString();
    }
    private static ArgumentException InvalidSubject() => new("commercial-incident-subject-invalid");
}
