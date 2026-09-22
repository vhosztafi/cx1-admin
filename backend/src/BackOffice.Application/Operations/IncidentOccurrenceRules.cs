using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackOffice.Application.Operations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IncidentOccurrence(DateOnly OccurredOn,string TimeZone,string Precision,
    [property:JsonConverter(typeof(IncidentLocalTimeConverter))]TimeOnly? ApproximateLocalTime=null,DateTimeOffset? OccurredAt=null);
public sealed class IncidentLocalTimeConverter:JsonConverter<TimeOnly?>
{
    public override TimeOnly? Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
    {
        if(reader.TokenType!=JsonTokenType.String||!TimeOnly.TryParseExact(reader.GetString(),"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var result))throw new JsonException("Use an approximate local time in HH:mm format.");
        return result;
    }
    public override void Write(Utf8JsonWriter writer,TimeOnly? value,JsonSerializerOptions options)
    {if(value is null)writer.WriteNullValue();else writer.WriteStringValue(value.Value.ToString("HH:mm",CultureInfo.InvariantCulture));}
}
public sealed record IncidentOccurrenceWindow(DateTimeOffset From,DateTimeOffset To,bool IsExact);
public sealed record IncidentOccurrenceSlice(Guid VersionId,string SourceHash,DateTimeOffset From,DateTimeOffset To);
public sealed class IncidentOccurrenceException(string code):Exception("The incident occurrence needs review.")
{ public string Code {get;}=code; }
public static class IncidentOccurrenceRules
{
    private static readonly TimeZoneInfo London=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
    public static IncidentOccurrenceWindow Window(IncidentOccurrence occurrence)
    {
        if(occurrence is null||occurrence.TimeZone!="Europe/London"||occurrence.OccurredOn==DateOnly.MinValue||occurrence.OccurredOn==DateOnly.MaxValue)
            throw Invalid("occurrence-date-invalid");
        if(occurrence.Precision=="exact")
        {
            if(occurrence.OccurredAt is not DateTimeOffset instant||occurrence.ApproximateLocalTime is not null||
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant,London).DateTime)!=occurrence.OccurredOn)throw Invalid("occurrence-exact-invalid");
            return new(instant.ToUniversalTime(),instant.ToUniversalTime(),true);
        }
        if(occurrence.OccurredAt is not null||occurrence.Precision is not("date" or "approximate")||
            (occurrence.Precision=="date")!=(occurrence.ApproximateLocalTime is null)||
            occurrence.ApproximateLocalTime is TimeOnly approximate&&(approximate.Second!=0||approximate.Ticks%TimeSpan.TicksPerMinute!=0))throw Invalid("occurrence-precision-invalid");
        // Approximate time is retained as a factual hint, not a fabricated tolerance
        // around an exact instant. Applicability still considers the whole local day.
        return new(ExactLocal(occurrence.OccurredOn,TimeOnly.MinValue),ExactLocal(occurrence.OccurredOn.AddDays(1),TimeOnly.MinValue),false);
    }
    public static DateTimeOffset ExactLocal(DateOnly date,TimeOnly time,TimeSpan? offset=null)
    {
        var local=DateTime.SpecifyKind(date.ToDateTime(time),DateTimeKind.Unspecified);
        if(London.IsInvalidTime(local))throw Invalid("occurrence-dst-gap");
        if(London.IsAmbiguousTime(local))
        {
            if(offset is null||!London.GetAmbiguousTimeOffsets(local).Contains(offset.Value))throw Invalid("occurrence-dst-offset-required");
            return new DateTimeOffset(local,offset.Value).ToUniversalTime();
        }
        var expected=London.GetUtcOffset(local);
        if(offset is not null&&offset!=expected)throw Invalid("occurrence-offset-invalid");
        return new DateTimeOffset(local,expected).ToUniversalTime();
    }
    public static string State(IncidentOccurrenceWindow window,DateTimeOffset knownAt,IReadOnlyList<IncidentOccurrenceSlice> slices)
    {
        if(window.From>window.To||!window.IsExact&&window.From==window.To)throw Invalid("occurrence-window-invalid");
        if(window.IsExact?window.From>knownAt:window.To>knownAt)return "incomplete";
        var relevant=slices.Where(x=>window.IsExact?(x.From==x.To?x.From==window.From:x.From<=window.From&&window.From<x.To):x.From<window.To&&x.To>window.From).OrderBy(x=>x.From).ToArray();
        if(relevant.Length==0)return "uncovered";
        var complete=window.IsExact;var cursor=window.From;
        if(!complete)
        {
            complete=true;
            foreach(var slice in relevant)
            {
                if(slice.From>cursor){complete=false;break;}
                if(slice.To>cursor)cursor=slice.To;
            }
            complete=complete&&cursor>=window.To;
        }
        if(!complete)return "partly-uncovered";
        return relevant.Select(x=>x.VersionId).Distinct().Count()==1?"resolved":"ambiguous";
    }
    private static IncidentOccurrenceException Invalid(string code)=>new(code);
}
