using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record TaskSubjectView(Guid Id, string Kind, Guid ParentId, string Label, string Href);
public sealed record TaskRelatedRecord(string Kind, Guid Id, string Label, string Href);
internal sealed record TaskPresentation(IReadOnlyDictionary<Guid, TaskSubjectView> Subjects, IReadOnlyDictionary<Guid, TaskRelatedRecord[]> RelatedRecords, IReadOnlyDictionary<Guid, string> Users, IReadOnlyDictionary<Guid, string> Teams)
{
    // Caller supplies only already-authorized task rows in its held transaction.
    // Query each record family once, rather than fetching parents per table row.
    public static async Task<TaskPresentation> Load(BackOfficeDbContext db, IReadOnlyList<OperationalTask> rows, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Task presentation requires held authority.");
        var subjectIds = rows.Select(x => x.SubjectId).Distinct().ToArray();
        var subjects = await db.Set<OperationalSubject>().AsNoTracking().Where(x => subjectIds.Contains(x.Id)).ToArrayAsync(token);
        var relationshipIds = subjects.Where(x => x.RelationshipId != null).Select(x => x.RelationshipId!.Value).ToArray();
        var relationships = await (from r in db.Set<ClientAgencyRelationship>() join c in db.Set<ClientAccount>() on r.ClientId equals c.Id join a in db.Set<Agency>() on r.AgencyId equals a.Id
            where relationshipIds.Contains(r.Id) select new { r.Id, r.ClientId, r.AgencyId, Label = c.Reference + " · " + a.Reference }).ToDictionaryAsync(x => x.Id, token);
        var quoteIds = subjects.Where(x => x.QuoteId != null).Select(x => x.QuoteId!.Value).ToArray();
        var quotes = await db.Set<Quote>().Where(x => quoteIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference, x.ClientId, x.AgencyId, x.RelationshipId, x.BoundPolicyId }).ToDictionaryAsync(x => x.Id, token);
        var draftIds = subjects.Where(x => x.ServicingDraftId != null).Select(x => x.ServicingDraftId!.Value).ToArray();
        var drafts = await (from d in db.Set<ServicingDraft>() join p in db.Set<Policy>() on d.PolicyId equals p.Id where draftIds.Contains(d.Id)
            select new { d.Id, d.Kind, d.PolicyId, p.Reference }).ToDictionaryAsync(x => x.Id, token);
        var policyIds = subjects.Where(x => x.PolicyId != null).Select(x => x.PolicyId!.Value)
            .Concat(drafts.Values.Select(x => x.PolicyId)).Concat(quotes.Values.Where(x => x.BoundPolicyId != null).Select(x => x.BoundPolicyId!.Value)).Distinct().ToArray();
        var policies = await db.Set<Policy>().Where(x => policyIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference, x.ClientId, x.AgencyId, x.RelationshipId }).ToDictionaryAsync(x => x.Id, token);
        var agencyIds = subjects.Where(x => x.AgencyId != null).Select(x => x.AgencyId!.Value)
            .Concat(relationships.Values.Select(x => x.AgencyId)).Concat(quotes.Values.Select(x => x.AgencyId)).Concat(policies.Values.Select(x => x.AgencyId)).Distinct().ToArray();
        var clientIds = relationships.Values.Select(x => x.ClientId).Concat(quotes.Values.Select(x => x.ClientId)).Concat(policies.Values.Select(x => x.ClientId)).Distinct().ToArray();
        var agencies = await db.Set<Agency>().Where(x => agencyIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference }).ToDictionaryAsync(x => x.Id, x => x.Reference, token);
        var clients = await db.Set<ClientAccount>().Where(x => clientIds.Contains(x.Id)).Select(x => new { x.Id, x.Reference }).ToDictionaryAsync(x => x.Id, x => x.Reference, token);
        var labels = new Dictionary<Guid, TaskSubjectView>();
        var related = new Dictionary<Guid, TaskRelatedRecord[]>();
        foreach (var subject in subjects)
        {
            var parent = OperationalScope.Parent(subject);
            var (label, href) = parent.Kind switch
            {
                "agency" => (agencies[parent.ParentId], $"/agents/{parent.ParentId}"),
                "relationship" => (relationships[parent.ParentId].Label, $"/clients/{relationships[parent.ParentId].ClientId}"),
                "quote" => (quotes[parent.ParentId].Reference, $"/quotes/{parent.ParentId}"),
                "policy" => (policies[parent.ParentId].Reference, $"/policies/{parent.ParentId}"),
                "servicing-draft" => (drafts[parent.ParentId].Kind + " · " + drafts[parent.ParentId].Reference, $"/drafts/{parent.ParentId}"),
                _ => throw new OperationalAccessException(404, "operational-subject-not-found")
            };
            labels.Add(subject.Id, new(subject.Id, parent.Kind, parent.ParentId, label, href));
            Guid? agencyId=null, clientId=null, policyId=null;
            if(parent.Kind=="relationship") { var r=relationships[parent.ParentId]; agencyId=r.AgencyId; clientId=r.ClientId; }
            if(parent.Kind=="quote")
            {
                var q=quotes[parent.ParentId]; agencyId=q.AgencyId; clientId=q.ClientId;
                if(q.BoundPolicyId is Guid bound && policies.TryGetValue(bound,out var p) && p.RelationshipId==q.RelationshipId && p.AgencyId==q.AgencyId && p.ClientId==q.ClientId) policyId=bound;
            }
            if(parent.Kind is "policy" or "servicing-draft")
            {
                policyId=parent.Kind=="policy"?parent.ParentId:drafts[parent.ParentId].PolicyId;
                var p=policies[policyId.Value]; agencyId=p.AgencyId; clientId=p.ClientId;
            }
            var links=new List<TaskRelatedRecord>();
            if(agencyId is Guid agency)links.Add(new("agency",agency,agencies[agency],$"/agents/{agency}"));
            if(clientId is Guid client)links.Add(new("client",client,clients[client],$"/clients/{client}"));
            if(policyId is Guid policy && parent.Kind!="policy")links.Add(new("policy",policy,policies[policy].Reference,$"/policies/{policy}"));
            related.Add(subject.Id,links.ToArray());
        }
        var userIds = rows.SelectMany(x => new[] { x.CreatedBy, x.OwnerId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
        var users = await db.Set<StaffUser>().Where(x => userIds.Contains(x.Id)).Select(x => new { x.Id, x.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, token);
        var teamIds = rows.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var teams = await db.Set<Team>().Where(x => teamIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, token);
        return new(labels, related, users, teams);
    }
}
