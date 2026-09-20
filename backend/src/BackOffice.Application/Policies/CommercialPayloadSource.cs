using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace BackOffice.Application.Policies;

// The caller authorizes and selects the immutable version and its applicability interval.
public sealed record CommercialPayloadSource(Guid PolicyId, Guid VersionId, string SourceContentHash,
    string SnapshotJson, DateTimeOffset EffectiveFrom, DateTimeOffset EffectiveUntil)
{
    internal JsonElement Read()
    {
        if (PolicyId == Guid.Empty || VersionId == Guid.Empty || SnapshotJson is null || Encoding.UTF8.GetByteCount(SnapshotJson) > 1048576 ||
            SourceContentHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SnapshotJson))))
            throw new ArgumentException("commercial-payload-source-invalid");
        JsonElement snapshot;
        try { using var parsed = JsonDocument.Parse(SnapshotJson); snapshot = parsed.RootElement.Clone(); }
        catch (JsonException) { throw new ArgumentException("commercial-payload-source-invalid"); }
        if (!Unique(snapshot) || !PolicySnapshotShape.Valid(snapshot) || snapshot.GetProperty("productCode").GetString() != "commercial-combined")
            throw new ArgumentException("commercial-payload-source-invalid");
        var term = snapshot.GetProperty("term"); var start = term.GetProperty("startsAt").GetDateTimeOffset(); var end = term.GetProperty("endsAt").GetDateTimeOffset();
        var expectedFrom = snapshot.GetProperty("provenance").TryGetProperty("effectiveAt", out var effective) ? effective.GetDateTimeOffset() : start;
        if (EffectiveFrom != expectedFrom || EffectiveFrom < start || EffectiveUntil > end || EffectiveFrom >= EffectiveUntil)
            throw new ArgumentException("commercial-payload-interval-invalid");
        return snapshot;
    }

    internal static bool Unique(JsonElement value) => value.ValueKind switch {
        JsonValueKind.Object => value.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count() && value.EnumerateObject().All(x => Unique(x.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(Unique), _ => true
    };
}
