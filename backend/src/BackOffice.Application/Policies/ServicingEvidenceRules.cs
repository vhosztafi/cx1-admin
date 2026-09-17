using System.Text.Json;
using System.Security.Cryptography;
using BackOffice.Application.Underwriting;
using BackOffice.Application.Quotes;

namespace BackOffice.Application.Policies;

public sealed record ServicingProofContext(Guid DraftId, Guid CycleId, Guid RevisionId, Guid RatingId, string InputHash, QuoteVersionPins Pins);
public sealed record ServicingEvidenceSlice(DateTimeOffset EffectiveAt, JsonElement Proposal, int TradingYears);
public sealed record ServicingProofRequirement(string Code, string Label, string Path, Guid? RiskItemId, IReadOnlyList<DateTimeOffset> EffectiveDates, string InputFingerprint)
{
    public required ServicingProofContext Context { get; init; }
}
public sealed record ServicingReviewedProof(Guid DraftId, Guid CycleId, Guid RevisionId, Guid RatingId, string Code, Guid? RiskItemId,
    string InputFingerprint, string ScreeningState, string ReviewState, bool Withdrawn);
// Trusted immutable full-risk slices only. SQL ownership, current grants, review
// identity and file screening are supplied by the servicing command/read boundary.
// A received flag in captured risk is never evidence of an accepted review.
public static class ServicingEvidenceRules
{
    public static IReadOnlyList<ServicingProofRequirement> Requirements(ServicingProofContext context, IReadOnlyList<ServicingEvidenceSlice> slices,
        IReadOnlyList<DateTimeOffset>? requestedTradingHistoryDates=null)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(slices);
        requestedTradingHistoryDates??=[];
        if(requestedTradingHistoryDates.Count>100) throw Invalid();
        DateTimeOffset? priorRequested=null;
        foreach(var date in requestedTradingHistoryDates)
        {
            if(date.Offset!=TimeSpan.Zero || priorRequested is not null && date<=priorRequested || !slices.Any(x=>x is not null && x.EffectiveAt==date)) throw Invalid();
            priorRequested=date;
        }
        if (new[] { context.DraftId, context.CycleId, context.RevisionId, context.RatingId }.Contains(Guid.Empty) ||
            !ReferralRules.Hash(context.InputHash) || context.Pins is null || slices.Count is < 1 or > 100) throw Invalid();
        var requirements = new Dictionary<(string Code, Guid? Id), (string Label, string Path, List<DateTimeOffset> Dates)>();
        var schedule = new List<object>(); DateTimeOffset? previous = null; long totalBytes = 0;
        foreach (var slice in slices)
        {
            if (slice is null || slice.Proposal.ValueKind != JsonValueKind.Object || slice.TradingYears is < 0 or > 100 ||
                slice.EffectiveAt.Offset != TimeSpan.Zero || previous is not null && slice.EffectiveAt <= previous ||
                !slice.Proposal.TryGetProperty("risk", out var risk) || risk.ValueKind != JsonValueKind.Object) throw Invalid();
            previous = slice.EffectiveAt;
            CanonicalQuoteInput canonical;
            try { canonical = QuoteCanonicalJson.Create(slice.Proposal.GetRawText(), context.Pins); }
            catch (QuoteInputException) { throw Invalid(); }
            totalBytes += System.Text.Encoding.UTF8.GetByteCount(canonical.Json);
            if (totalBytes > ServicingRatingInput.MaximumBytes) throw Invalid();
            schedule.Add(new { slice.EffectiveAt, slice.TradingYears, canonical.ContentHash });
            HashSet<Guid> Items(string name)
            {
                if (!risk.TryGetProperty(name, out var items)) return [];
                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 1000) throw Invalid();
                var ids = new HashSet<Guid>();
                foreach (var item in items.EnumerateArray())
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var key) || key.ValueKind != JsonValueKind.String ||
                        !key.TryGetGuid(out var id) || id == Guid.Empty || !ids.Add(id)) throw Invalid();
                return ids;
            }
            _ = Items("drivers"); var premisesIds = Items("premises");
            void Add(string code, string label, string path, Guid? id = null)
            {
                if (!requirements.TryGetValue((code,id),out var required))
                {
                    if (requirements.Count >= 5000) throw Invalid();
                    required = (label,path,[]); requirements.Add((code,id),required);
                }
                if (!required.Dates.Contains(slice.EffectiveAt)) required.Dates.Add(slice.EffectiveAt);
            }
            foreach (var item in QuoteEvidenceRequirements.ForProposal(slice.Proposal)) Add(item.Code,item.Label,item.Path,item.RiskItemId);
            if (slice.TradingYears < 5 || requestedTradingHistoryDates.Contains(slice.EffectiveAt)) Add("trading-history","Business trading history and experience","/risk/business");
            if (slice.Proposal.TryGetProperty("cover",out var cover) && cover.TryGetProperty("requestedSections",out var sections))
            {
                if (sections.ValueKind != JsonValueKind.Array) throw Invalid();
                foreach (var section in sections.EnumerateArray())
                {
                    if (!section.TryGetProperty("code",out var code) || code.GetString() != "premises" ||
                        !section.TryGetProperty("selected",out var selected) || selected.ValueKind != JsonValueKind.True) continue;
                    if (!section.TryGetProperty("premisesIds",out var targets) || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() == 0) throw Invalid();
                    var seen = new HashSet<Guid>();
                    foreach (var target in targets.EnumerateArray())
                    {
                        if (target.ValueKind != JsonValueKind.String || !target.TryGetGuid(out var id) || !premisesIds.Contains(id) || !seen.Add(id)) throw Invalid();
                        Add("premises-security","Security evidence for the insured premises","/risk/premises",id);
                    }
                }
            }
        }
        // Bind the whole cumulative schedule, including changes outside the proof's
        // target. Approval of a different revision/rating must never carry forward.
        var scheduleHash = Hash(new { format = "servicing-proof-schedule-1", schedule });
        return requirements.OrderBy(x=>x.Key.Code,StringComparer.Ordinal).ThenBy(x=>x.Key.Id).Select(x=>new ServicingProofRequirement(
            x.Key.Code,x.Value.Label,x.Value.Path,x.Key.Id,x.Value.Dates.AsReadOnly(),
            Hash(new { format = "servicing-proof-1", context.DraftId, context.CycleId, context.RevisionId, context.RatingId,
                context.InputHash, scheduleHash, code = x.Key.Code, riskItemId = x.Key.Id, effectiveDates = x.Value.Dates })) { Context = context }).ToArray();
    }

    public static bool Satisfied(ServicingProofContext context, ServicingProofRequirement requirement, ServicingReviewedProof proof) =>
        requirement.Context == context && proof.DraftId == context.DraftId && proof.CycleId == context.CycleId && proof.RevisionId == context.RevisionId && proof.RatingId == context.RatingId &&
        proof.Code == requirement.Code && proof.RiskItemId == requirement.RiskItemId &&
        ReferralRules.EvidenceSatisfied(proof.ScreeningState,proof.ReviewState,proof.Withdrawn,requirement.InputFingerprint,proof.InputFingerprint);

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static ArgumentException Invalid() => new("Current servicing proof identities and complete ordered risk slices are required.");
}
