using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record TaskSubjectView(Guid Id, string Kind, Guid ParentId, string Label, string Href);
internal sealed record TaskPresentation(IReadOnlyDictionary<Guid, TaskSubjectView> Subjects, IReadOnlyDictionary<Guid, string> Users, IReadOnlyDictionary<Guid, string> Teams)
{
    // Caller supplies only already-authorized task rows in its held transaction.
    // Query each record family once, rather than fetching parents per table row.
    public static async Task<TaskPresentation> Load(BackOfficeDbContext db, IReadOnlyList<OperationalTask> rows, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Task presentation requires held authority.");
        var subjectIds = rows.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await db.Set<OperationalSubject>().AsNoTracking().Where(x => subjectIds.Contains(x.Id)).ToArrayAsync(token);
        var agencyIds = subjects.Where(x => x.AgencyId != null).Select(x => x.AgencyId!.Value).ToArray();
        var agencies = await db.Set<Agency>().Where(x => agencyIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference }).ToDictionaryAsync(x => x.Id, x => x.Reference, token);
        var relationshipIds = subjects.Where(x => x.RelationshipId != null).Select(x => x.RelationshipId!.Value).ToArray();
        var relationships = await (from r in db.Set<ClientAgencyRelationship>() join c in db.Set<ClientAccount>() on r.ClientId equals c.Id join a in db.Set<Agency>() on r.AgencyId equals a.Id
            where relationshipIds.Contains(r.Id) select new { r.Id, r.ClientId, Label = c.Reference + " · " + a.Reference }).ToDictionaryAsync(x => x.Id, token);
        var quoteIds = subjects.Where(x => x.QuoteId != null).Select(x => x.QuoteId!.Value).ToArray();
        var quotes = await db.Set<Quote>().Where(x => quoteIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference }).ToDictionaryAsync(x => x.Id, x => x.Reference, token);
        var policyIds = subjects.Where(x => x.PolicyId != null).Select(x => x.PolicyId!.Value).ToArray();
        var policies = await db.Set<Policy>().Where(x => policyIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference }).ToDictionaryAsync(x => x.Id, x => x.Reference, token);
        var draftIds = subjects.Where(x => x.ServicingDraftId != null).Select(x => x.ServicingDraftId!.Value).ToArray();
        var drafts = await (from d in db.Set<ServicingDraft>() join p in db.Set<Policy>() on d.PolicyId equals p.Id where draftIds.Contains(d.Id)
            select new { d.Id, d.Kind, p.Reference }).ToDictionaryAsync(x => x.Id, token);
        var labels = new Dictionary<Guid, TaskSubjectView>();
        foreach (var subject in subjects)
        {
            var parent = OperationalScope.Parent(subject);
            var (label, href) = parent.Kind switch
            {
                "agency" => (agencies[parent.ParentId], $"/agents/{parent.ParentId}"),
                "relationship" => (relationships[parent.ParentId].Label, $"/clients/{relationships[parent.ParentId].ClientId}"),
                "quote" => (quotes[parent.ParentId], $"/quotes/{parent.ParentId}"),
                "policy" => (policies[parent.ParentId], $"/policies/{parent.ParentId}"),
                "servicing-draft" => (drafts[parent.ParentId].Kind + " · " + drafts[parent.ParentId].Reference, $"/drafts/{parent.ParentId}"),
                _ => throw new OperationalAccessException(404, "operational-subject-not-found")
            };
            labels.Add(subject.Id, new(subject.Id, parent.Kind, parent.ParentId, label, href));
        }
        var userIds = rows.SelectMany(x => new[] { x.CreatedBy, x.OwnerId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
        var users = await db.Set<StaffUser>().Where(x => userIds.Contains(x.Id)).Select(x => new { x.Id, x.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, token);
        var teamIds = rows.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var teams = await db.Set<Team>().Where(x => teamIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, token);
        return new(labels, users, teams);
    }
}
