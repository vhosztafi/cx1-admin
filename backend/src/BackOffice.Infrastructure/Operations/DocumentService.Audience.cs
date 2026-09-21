using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class DocumentService
{
    private static IQueryable<ClientAgencyRelationship> AllowedAudience(BackOfficeDbContext db,OperationalSubject subject)
    {
        var query=db.Set<ClientAgencyRelationship>().Where(x=>x.State=="active");
        return subject.Kind switch
        {
            "agency"=>query.Where(x=>x.AgencyId==subject.AgencyId),
            "relationship"=>query.Where(x=>x.Id==subject.RelationshipId),
            "quote"=>query.Where(x=>db.Set<Quote>().Any(q=>q.Id==subject.QuoteId && q.RelationshipId==x.Id)),
            "policy"=>query.Where(x=>db.Set<Policy>().Any(p=>p.Id==subject.PolicyId && p.RelationshipId==x.Id)),
            "servicing-draft"=>query.Where(x=>(from draft in db.Set<ServicingDraft>() join policy in db.Set<Policy>() on draft.PolicyId equals policy.Id
                where draft.Id==subject.ServicingDraftId && policy.RelationshipId==x.Id select draft.Id).Any()),
            _=>query.Where(x=>false)
        };
    }

    private static async Task HoldAudience(BackOfficeDbContext db,OperationalSubject subject,Guid? relationshipId,CancellationToken token)
    {
        if(relationshipId is not Guid id)return;
        if(!await AllowedAudience(db,subject).AnyAsync(x=>x.Id==id,token))throw MissingDocument();
        var relationship=await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM ClientAgencyRelationship WITH(HOLDLOCK,ROWLOCK) WHERE Id={id}")
            .AsNoTracking().SingleAsync(token);
        if(relationship.State!="active")throw MissingDocument();
    }
}
