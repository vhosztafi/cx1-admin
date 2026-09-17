using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Underwriting;
public sealed partial class CapacityReadModel
{
    private static async Task AddCaseDetails(BackOfficeDbContext db, CapacityEscalation row, Quote quote,
        QuoteReferral referral, Dictionary<string, object> result, CancellationToken token)
    {
        result["assignmentOptions"] = await CapacityService.SeniorUsers(db).OrderBy(x => x.DisplayName).ThenBy(x => x.Id)
            .Select(x => new { x.Id, label = x.DisplayName }).ToArrayAsync(token);
        var actions = await (from a in db.Set<AuditEvent>().AsNoTracking()
                             join u in db.Set<StaffUser>() on a.ActorId equals u.Id
                             where a.SubjectRecordId == row.Id && a.EventType == "capacity.action-recorded"
                             orderby a.OccurredAt descending, a.Id
                             select new { a.Id, a.OccurredAt, a.Reason, a.After, actorLabel = u.DisplayName }).Take(50).ToArrayAsync(token);
        result["actionHistory"] = actions.Select(x => {
            using var details = JsonDocument.Parse(x.After!);
            return new { x.Id, x.OccurredAt, x.Reason, x.actorLabel, action = details.RootElement.GetProperty("action").GetString() };
        }).ToArray();
        // Internal comparison is constrained to the already authorised agency,
        // product, provider and rule. Previous decisions confer no authority.
        var similar = await (from e in db.Set<CapacityEscalation>().AsNoTracking()
                             join q in db.Set<Quote>() on e.QuoteId equals q.Id
                             join r in db.Set<QuoteReferral>() on e.ReferralId equals r.Id
                             where e.Id != row.Id && q.AgencyId == quote.AgencyId && q.ProductId == quote.ProductId &&
                                 e.ProviderId == row.ProviderId && r.RuleCode == referral.RuleCode && r.Dimension == referral.Dimension
                             orderby e.CreatedAt descending, e.Id
                             select new { e.Id, e.QuoteId, e.ReferralId, quoteReference = q.Reference, e.CreatedAt, e.State, request = r.Reason,
                                 policyId = db.Set<Policy>().Where(p => p.SourceQuoteId == q.Id).Select(p => (Guid?)p.Id).FirstOrDefault(),
                                 outcome = db.Set<CapacityMessage>().Where(m => m.Id == e.CurrentResponseId).Select(m => m.Outcome).FirstOrDefault(),
                                 response = db.Set<CapacityMessage>().Where(m => m.Id == e.CurrentResponseId).Select(m => m.DefinitionJson).FirstOrDefault() }).Take(10).ToArrayAsync(token);
        result["similarReferrals"] = similar.Select(x => {
            using var response = JsonDocument.Parse(x.response ?? "{}");
            var conditions = response.RootElement.TryGetProperty("conditions", out var list)
                ? list.EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToArray() : [];
            return new { x.Id, x.QuoteId, x.ReferralId, x.quoteReference, x.CreatedAt, x.State, x.request, x.policyId, x.outcome, conditions };
        }).ToArray();
    }
}
