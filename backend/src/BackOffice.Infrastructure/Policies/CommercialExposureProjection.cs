using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public static class CommercialExposureProjection
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The issue writer owns the transaction and policy/book fences. This method
    // never commits or repairs a partial issue independently.
    public static async Task<CommercialExposureVersion> AppendAsync(BackOfficeDbContext db, Guid versionId,
        Guid binderVersionId, Guid actorId, CancellationToken token = default)
    {
        RequireTransaction(db);
        var source = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x => x.Id == versionId, token);
        var transaction = await db.Set<PolicyTransaction>().AsNoTracking().SingleAsync(x => x.Id == source.TransactionId, token);
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x => x.Id == source.TermId, token);
        var mapping = await db.Set<CommercialExposureBinder>().AsNoTracking().SingleAsync(x => x.BinderVersionId == binderVersionId, token);
        using var snapshot = JsonDocument.Parse(source.SnapshotJson);
        if (snapshot.RootElement.GetProperty("productCode").GetString() != CommercialCaptureRules.ProductCode ||
            !SHA256.HashData(Encoding.UTF8.GetBytes(source.SnapshotJson)).SequenceEqual(source.ContentHash))
            throw new InvalidOperationException("Commercial exposure requires the exact immutable commercial source.");
        var locations = transaction.Kind == "cancellation" ? [] : Locations(snapshot.RootElement);
        var row = new CommercialExposureVersion { BookId = mapping.BookId, BinderVersionId = binderVersionId,
            PolicyId = source.PolicyId, TermId = source.TermId, TransactionId = source.TransactionId, VersionId = source.Id,
            SourceHash = source.ContentHash, TermStartsAt = term.StartsAt, TermEndsAt = term.EndsAt,
            EffectiveAt = source.EffectiveAt, ProcessedAt = source.ProcessedAt, TransactionKind = transaction.Kind,
            TransactionSequence = transaction.Sequence, SliceOrdinal = source.SliceOrdinal,
            LocationsJson = JsonSerializer.Serialize(locations, Json), CreatedBy = actorId };
        // Exercise the same validation as later assessments, including zero headers.
        CommercialExposureRules.Snapshot([Slice(row, locations)], row.BookId, row.EffectiveAt, row.ProcessedAt);
        db.Add(row); await db.SaveChangesAsync(token); return row;
    }

    public static async Task<IReadOnlyList<CommercialExposureSlice>> ReadAsync(BackOfficeDbContext db, Guid bookId,
        DateTimeOffset knownAt, CancellationToken token = default)
    {
        var headers = await db.Set<CommercialExposureVersion>().AsNoTracking().Where(x => x.BookId == bookId && x.ProcessedAt <= knownAt).ToArrayAsync(token);
        // Read immutable headers/children only: no foreign Policy row dependency.
        var children = await (from location in db.Set<CommercialExposureLocationRecord>().AsNoTracking()
            join version in db.Set<CommercialExposureVersion>().AsNoTracking() on location.ExposureVersionId equals version.Id
            where version.BookId == bookId && version.ProcessedAt <= knownAt select location).ToArrayAsync(token);
        var grouped = children.ToLookup(x => x.ExposureVersionId);
        return headers.Select(x => Slice(x, grouped[x.Id].Select(y => new CommercialExposureLocation(y.RiskItemId, y.District, y.SumInsured)).ToArray())).ToArray();
    }

    public static async Task<IReadOnlyList<CommercialExposureLimit>> LimitsAsync(BackOfficeDbContext db, Guid bookId,
        DateTimeOffset knownAt, CancellationToken token = default) => (await db.Set<CommercialExposureLimitVersion>().AsNoTracking()
            .Where(x => x.BookId == bookId && x.PublishedAt <= knownAt).ToArrayAsync(token))
        .Select(x => new CommercialExposureLimit(x.Id, x.BookId, x.District, x.Version, x.Amount, x.EffectiveFrom,
            x.EffectiveTo, x.PublishedAt, Convert.ToHexString(x.ContentHash).ToLowerInvariant(), x.SupersedesLimitId)).ToArray();

    public static CommercialExposureLocation[] Locations(JsonElement snapshot) => snapshot.GetProperty("risk").GetProperty("locations").EnumerateArray()
        .Select(location => new CommercialExposureLocation(location.GetProperty("id").GetGuid(),
            CommercialCaptureRules.NormalizePostcode(location.GetProperty("address").GetProperty("postcode").GetString()!)?.District
                ?? throw new InvalidOperationException("Issued location postcode is invalid."),
            Amount(location, "buildings") + Amount(location, "contents") + Amount(location, "stock"))).ToArray();

    private static decimal Amount(JsonElement location, string name) => decimal.Parse(location.GetProperty(name).GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static CommercialExposureSlice Slice(CommercialExposureVersion x, IReadOnlyList<CommercialExposureLocation> locations) =>
        new(x.BookId, x.PolicyId, x.TermId, x.VersionId, x.TermStartsAt, x.TermEndsAt, x.EffectiveAt, x.ProcessedAt,
            x.TransactionSequence, x.SliceOrdinal, x.TransactionKind, locations);
    private static void RequireTransaction(BackOfficeDbContext db)
    { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Exposure publication requires a held transaction."); }
}
