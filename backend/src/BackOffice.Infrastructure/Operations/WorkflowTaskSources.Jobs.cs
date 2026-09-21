using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

internal static partial class WorkflowTaskSources
{
    private static async Task<WorkflowTaskSource> Job(BackOfficeDbContext db, WorkflowTaskDefinition rule, Guid id, CancellationToken token)
    {
        var exception = await db.Set<JobException>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw Missing();
        var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == exception.WorkId, token);
        // A diagnostic or unknown job cannot be attached to an arbitrary agency.
        // WorkId is joined to the owning request for every supported job kind.
        OperationalParent? parent = work.Kind switch
        {
            "quote-lookup" => await db.Set<QuoteLookup>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("quote", x.QuoteId)).SingleOrDefaultAsync(token),
            "quote-rating" => await db.Set<UnderwritingCycle>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("quote", x.QuoteId)).SingleOrDefaultAsync(token),
            "capacity-escalation" => await db.Set<CapacitySubmission>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("quote", x.QuoteId)).SingleOrDefaultAsync(token),
            "quote-delivery" => await db.Set<QuoteTermsDelivery>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("quote", x.QuoteId)).SingleOrDefaultAsync(token),
            "servicing-rating" => await db.Set<ServicingCycle>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("servicing-draft", x.DraftId)).SingleOrDefaultAsync(token),
            "servicing-capacity" => await db.Set<ServicingCapacitySubmission>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("servicing-draft", x.DraftId)).SingleOrDefaultAsync(token),
            "servicing-delivery" => await db.Set<ServicingTermsDelivery>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("servicing-draft", x.DraftId)).SingleOrDefaultAsync(token),
            "cancellation-notice" => await db.Set<CancellationConsequence>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("policy", x.PolicyId)).SingleOrDefaultAsync(token),
            "renewal-lapse-notification" => await db.Set<RenewalLapseEvent>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("policy", x.PolicyId)).SingleOrDefaultAsync(token),
            "agency-notification" => await db.Set<AgencyNotification>().Where(x => x.WorkId == work.Id).Select(x => new OperationalParent("agency", x.AgencyId)).SingleOrDefaultAsync(token),
            _ => null
        };
        if (parent is null) throw new OperationalAccessException(409, "workflow-source-parent-unavailable");
        if (work.CreatedBy is not Guid actor) throw new OperationalAccessException(409, "workflow-source-actor-unavailable");
        var resolved = work.State == "succeeded";
        return new(parent, actor, LocalDate(exception.OccurredAt).AddDays(rule.DueDays), !resolved, resolved,
            JsonSerializer.Serialize(new { sourceKind = "job-exception", sourceEventId = id, parent, actorId = actor,
                details = new { exception.WorkId, exception.Code, exception.OccurredAt, work.Kind, work.State, work.Attempts, work.ErrorCode } }, Json));
    }
}
