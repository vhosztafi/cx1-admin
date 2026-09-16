using System.Text.Json;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

public static class QuoteLookupProvenance
{
    // Call only within the held quote read scope. A decision survives unrelated
    // edits, but any change to its own vehicle invalidates the capture context.
    public static async Task<IReadOnlyDictionary<Guid, string>> VehicleModesAsync(BackOfficeDbContext db,
        QuoteRevision current, CancellationToken token)
    {
        var decisions = await (from selection in db.Set<QuoteLookupSelection>().AsNoTracking()
            join lookup in db.Set<QuoteLookup>().AsNoTracking() on selection.LookupId equals lookup.Id
            join revision in db.Set<QuoteRevision>().AsNoTracking() on selection.NewRevisionId equals revision.Id
            where selection.QuoteId == current.QuoteId && lookup.Kind == "vehicle" && revision.Number <= current.Number
            orderby revision.Number descending
            select new { Item = lookup.RiskItemId!.Value, selection.CandidateId, Revision = revision }).ToArrayAsync(token);
        var modes = new Dictionary<Guid, string>(); var seen = new HashSet<Guid>();
        using var proposal = JsonDocument.Parse(current.ProposalJson);
        foreach (var decision in decisions)
        {
            if (!seen.Add(decision.Item)) continue;
            using var selected = JsonDocument.Parse(decision.Revision.ProposalJson);
            var target = new QuoteLookupTarget("vehicle", "vehicle", decision.Item);
            try
            {
                var then = QuoteLookupRules.Prepare(selected.RootElement, target, QuoteService.Pins(decision.Revision));
                var now = QuoteLookupRules.Prepare(proposal.RootElement, target, QuoteService.Pins(current));
                if (then.InputFingerprint == now.InputFingerprint) modes[decision.Item] = decision.CandidateId is null ? "manual" : "lookup";
            }
            catch (QuoteInputException) { /* Removed or changed targets have no current provenance. */ }
        }
        return modes;
    }
}
