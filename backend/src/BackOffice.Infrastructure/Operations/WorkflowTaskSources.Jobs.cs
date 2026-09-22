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
            "file-finalization" => await FileParent(db, work.Id, token),
            "operational-delivery" => await DeliveryParent(db, work.Id, token),
            _ => null
        };
        if (parent is null) throw new OperationalAccessException(409, "workflow-source-parent-unavailable");
        if (work.CreatedBy is not Guid actor) throw new OperationalAccessException(409, "workflow-source-actor-unavailable");
        var originalActor=actor;
        if(work.Kind=="operational-delivery")actor=await DeliveryExceptionOwner(db,parent,actor,token);
        var resolved = work.State == "succeeded";
        object details=work.Kind=="operational-delivery"
            ? new { exception.WorkId, exception.Code, exception.OccurredAt, work.Kind, work.State, work.Attempts, work.ErrorCode, originalActorId=originalActor }
            : new { exception.WorkId, exception.Code, exception.OccurredAt, work.Kind, work.State, work.Attempts, work.ErrorCode };
        return new(parent, actor, LocalDate(exception.OccurredAt).AddDays(rule.DueDays), !resolved, resolved,
            JsonSerializer.Serialize(new { sourceKind = "job-exception", sourceEventId = id, parent, actorId = actor,
                details }, Json));
    }

    private static async Task<OperationalParent?> FileParent(BackOfficeDbContext db, Guid workId, CancellationToken token)
    {
        var subject = await (from file in db.Set<FileObject>() join parent in db.Set<OperationalSubject>() on file.SubjectId equals parent.Id
            where file.WorkId == workId && file.StorageKind == "local" select parent).AsNoTracking().SingleOrDefaultAsync(token);
        return subject is null ? null : OperationalScope.Parent(subject);
    }

    private static async Task<OperationalParent?> DeliveryParent(BackOfficeDbContext db, Guid workId, CancellationToken token)
    {
        var subject=await(from delivery in db.Set<OperationalDelivery>() join parent in db.Set<OperationalSubject>() on delivery.SubjectId equals parent.Id
            where delivery.WorkId==workId select parent).AsNoTracking().SingleOrDefaultAsync(token);
        return subject is null?null:OperationalScope.Parent(subject);
    }
    private static async Task<Guid> DeliveryExceptionOwner(BackOfficeDbContext db,OperationalParent parent,Guid original,CancellationToken token)
    {
        // A revoked sender cannot execute the delivery. Its failure still needs
        // an operational owner with existing current parent authority. This is an
        // automatic workflow task, not an action attributed to a human sender;
        // the retained source snapshot records the original actor separately.
        string[] roles=parent.Kind=="agency"?["underwriter","senior-underwriter","agency-admin","system-admin"]:
            parent.Kind=="relationship"?["servicing","underwriter","senior-underwriter","agency-admin"]:["servicing","underwriter","senior-underwriter"];
        var candidates=from user in db.Set<StaffUser>() where user.State=="active"&&user.AgencyId==null&&
            (from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id&&role.Scope=="internal"&&roles.Contains(role.Code) select link).Any()&&
            !(from link in db.Set<UserRole>() join role in db.Set<Role>() on link.RoleId equals role.Id where link.UserId==user.Id&&role.Scope!="internal" select link).Any()
            orderby user.Id==original descending,user.Id select user.Id;
        return await candidates.Select(x=>(Guid?)x).FirstOrDefaultAsync(token)??throw new OperationalAccessException(409,"workflow-source-actor-unavailable");
    }
}
