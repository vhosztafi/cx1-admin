using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class PolicyHistoryService
{
    private sealed record DecisionLabels(string Binder, string Authority);
    private static async Task<Dictionary<Guid, DecisionLabels>> DecisionLabelsAsync(BackOfficeDbContext db, Guid policyId, CancellationToken token)
    {
        var rows = await (from transaction in db.Set<PolicyTransaction>().AsNoTracking()
            join cycle in db.Set<UnderwritingCycle>() on transaction.CycleId equals cycle.Id into quoteCycles
            from cycle in quoteCycles.DefaultIfEmpty()
            join servicing in db.Set<ServicingCycle>() on transaction.ServicingCycleId equals servicing.Id into servicingCycles
            from servicing in servicingCycles.DefaultIfEmpty()
            join cancellation in db.Set<CancellationIssueDecision>() on transaction.CancellationIssueDecisionId equals cancellation.Id into cancellations
            from cancellation in cancellations.DefaultIfEmpty()
            join authority in db.Set<AuthorityVersion>() on
                (cancellation != null ? (Guid?)cancellation.AuthorityVersionId : servicing != null ? (Guid?)servicing.AuthorityVersionId : cycle != null ? (Guid?)cycle.AuthorityVersionId : null) equals (Guid?)authority.Id
            join binder in db.Set<BinderVersion>() on authority.BinderVersionId equals binder.Id
            where transaction.PolicyId == policyId
            select new { transaction.Id, Binder = binder.Version, Authority = authority.Version }).ToArrayAsync(token);
        return rows.ToDictionary(x => x.Id, x => new DecisionLabels(x.Binder, x.Authority));
    }
}
