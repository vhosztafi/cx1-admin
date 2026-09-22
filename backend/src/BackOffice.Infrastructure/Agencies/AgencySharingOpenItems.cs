using System.Data;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Agencies;

public sealed record AgencySharedOpenItem(Guid Id,string Reference,string Kind,string Subject,string Instruction,string State,DateTimeOffset CreatedAt);

public static partial class AgencySharingService
{
    private static async Task<AgencySharingPage<AgencySharedOpenItem>> ReadOpenItems(BackOfficeDbContext db,ActorContext actor,Guid agencyId,AgencySharingQuery query,bool preview,CancellationToken token)
    {
        if(query.Size is <1 or >100||query.Offset<0||query.Offset>int.MaxValue-query.Size||query.Search?.Length>200||query.RelationshipId==Guid.Empty)
            throw new AgencyCommandException(400,"invalid-sharing-query");
        RequireSerializableIfPresent(db);
        await using var transaction=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token):null;
        if(preview)await AuthorizePreview(db,actor,agencyId,token);else await AgencyScope.Resolve(db,actor,agencyId,"agency-sharing-read",token);
        var rows=await OpenItemRows(db,agencyId,query with{Search=query.Search?.Trim()},token);
        if(preview)
        {
            db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,OccurredAt=query.At??DateTimeOffset.UtcNow,EventType="agency.sharing-preview",CorrelationId=Guid.NewGuid(),After=JsonSerializer.Serialize(new{agencyId,section="open-items"})});
            await db.SaveChangesAsync(token);
        }
        if(transaction is not null)await transaction.CommitAsync(token);
        return new(rows.Skip(query.Offset).Take(query.Size).ToList(),rows.Count,query.Offset,query.Size);
    }
    public static Task<AgencySharingPage<AgencySharedOpenItem>> OpenItems(BackOfficeDbContext db,ActorContext actor,Guid agencyId,AgencySharingQuery query,CancellationToken token=default)
        =>ReadOpenItems(db,actor,agencyId,query,false,token);
    public static Task<AgencySharingPage<AgencySharedOpenItem>> PreviewOpenItems(BackOfficeDbContext db,ActorContext actor,Guid agencyId,AgencySharingQuery query,CancellationToken token=default)
        =>ReadOpenItems(db,actor,agencyId,query,true,token);

    private static async Task<List<AgencySharedOpenItem>> OpenItemRows(BackOfficeDbContext db,Guid agencyId,AgencySharingQuery query,CancellationToken token)
    {
        var relationships=Relationships(db,agencyId,query);
        var now=query.At??DateTimeOffset.UtcNow;
        // Only explicit public requests, never internal task titles or comments.
        var requests=db.Set<AgencyResponseRequest>().AsNoTracking()
            .Where(x=>x.State=="awaiting-response"&&relationships.Any(r=>r.Id==x.RelationshipId)&&
                db.Set<OperationalSubject>().Any(s=>s.Id==x.SubjectId&&(
                    s.Kind=="agency"&&s.AgencyId==agencyId||s.Kind=="relationship"&&s.RelationshipId==x.RelationshipId||
                    s.Kind=="quote"&&db.Set<Quote>().Any(q=>q.Id==s.QuoteId&&q.RelationshipId==x.RelationshipId)||
                    s.Kind=="policy"&&db.Set<Policy>().Any(p=>p.Id==s.PolicyId&&p.RelationshipId==x.RelationshipId)||
                    s.Kind=="servicing-draft"&&db.Set<ServicingDraft>().Any(d=>d.Id==s.ServicingDraftId&&db.Set<Policy>().Any(p=>p.Id==d.PolicyId&&p.RelationshipId==x.RelationshipId)))))
            .Select(x=>new AgencySharedOpenItem(x.Id,x.Reference,"requested-response",x.Subject,x.Instruction,x.State,x.CreatedAt));
        var quotes=from quote in db.Set<Quote>().AsNoTracking()
            join cycle in db.Set<UnderwritingCycle>() on quote.CurrentUnderwritingCycleId equals cycle.Id
            join terms in db.Set<QuoteTermsVersion>() on cycle.CurrentTermsVersionId equals terms.Id
            join rating in db.Set<QuoteRatingResult>() on terms.RatingId equals rating.Id
            join delivery in db.Set<QuoteTermsDelivery>() on cycle.CurrentDeliveryId equals delivery.Id
            where quote.AgencyId==agencyId&&relationships.Any(r=>r.Id==quote.RelationshipId&&r.ClientId==quote.ClientId)&&
                quote.BoundPolicyId==null&&quote.State=="sent"&&quote.CurrentRevisionId==cycle.QuoteRevisionId&&cycle.SupersededAt==null&&
                cycle.RelationshipId==quote.RelationshipId&&cycle.CurrentAcceptanceId==null&&cycle.CurrentRatingId==rating.Id&&rating.ExpiresAt>now&&
                terms.CycleId==cycle.Id&&terms.QuoteId==quote.Id&&delivery.CycleId==cycle.Id&&delivery.TermsVersionId==terms.Id&&delivery.State=="delivered"
            select new AgencySharedOpenItem(terms.Id,quote.Reference,"quotation-acceptance","Quotation terms","Review the delivered quotation terms and confirm acceptance.","awaiting-acceptance",delivery.CreatedAt);
        var servicing=from draft in db.Set<ServicingDraft>().AsNoTracking()
            join policy in db.Set<Policy>() on draft.PolicyId equals policy.Id
            join term in db.Set<PolicyTerm>() on draft.BaseTermId equals term.Id
            join cycle in db.Set<ServicingCycle>() on draft.CurrentCycleId equals cycle.Id
            join terms in db.Set<ServicingTermsVersion>() on cycle.CurrentTermsVersionId equals terms.Id
            join rating in db.Set<ServicingRatingResult>() on terms.RatingId equals rating.Id
            join delivery in db.Set<ServicingTermsDelivery>() on cycle.CurrentDeliveryId equals delivery.Id
            where policy.AgencyId==agencyId&&relationships.Any(r=>r.Id==policy.RelationshipId&&r.ClientId==policy.ClientId)&&
                draft.State=="draft"&&draft.IssuedTransactionId==null&&draft.CurrentRevisionId==cycle.RevisionId&&cycle.SupersededAt==null&&
                (draft.Kind!="renewal"||!db.Set<RenewalLapseEvent>().Any(x=>x.TermId==draft.BaseTermId))&&
                term.CurrentVersionId==draft.BaseVersionId&&cycle.BaseVersionId==draft.BaseVersionId&&cycle.CurrentAcceptanceId==null&&
                cycle.CurrentRatingId==rating.Id&&rating.ExpiresAt>now&&terms.CycleId==cycle.Id&&terms.RevisionId==draft.CurrentRevisionId&&
                delivery.CycleId==cycle.Id&&delivery.TermsVersionId==terms.Id&&delivery.State=="delivered"
            select new AgencySharedOpenItem(terms.Id,policy.Reference,"servicing-acceptance","Policy change terms","Review the delivered policy change terms and confirm acceptance.","awaiting-acceptance",delivery.CreatedAt);
        var rows=await requests.ToListAsync(token); rows.AddRange(await quotes.ToListAsync(token)); rows.AddRange(await servicing.ToListAsync(token));
        return rows
            .Where(x=>string.IsNullOrEmpty(query.Search)||x.Reference.Contains(query.Search,StringComparison.OrdinalIgnoreCase)||x.Subject.Contains(query.Search,StringComparison.OrdinalIgnoreCase)||x.Instruction.Contains(query.Search,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToList();
    }
}
