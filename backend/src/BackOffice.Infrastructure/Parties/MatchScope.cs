using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

public sealed class MatchScope(ActorContext actor)
{
    public IQueryable<MatchReview> Reviews(BackOfficeDbContext db)
    {
        var query=db.Set<MatchReview>().AsNoTracking();
        if(!actor.HasCapability("match-read"))return query.Where(x=>false);
        var parties=new PartyScope(actor);var candidates=parties.Clients(db);var agencies=parties.Agencies(db);
        return query.Where(x=>candidates.Any(c=>c.Id==x.CandidateClientId) && db.Set<MatchSubmission>().Any(s=>s.Id==x.SubmissionId && agencies.Any(a=>a.Id==s.AgencyId)));
    }
    public IQueryable<MatchDecision> Decisions(BackOfficeDbContext db)
    {
        var reviews=Reviews(db);return db.Set<MatchDecision>().AsNoTracking().Where(x=>reviews.Any(r=>r.Id==x.MatchId));
    }
    public IQueryable<MatchInformationRequest> InformationRequests(BackOfficeDbContext db)
    {
        var reviews=Reviews(db);return db.Set<MatchInformationRequest>().AsNoTracking().Where(x=>reviews.Any(r=>r.Id==x.MatchId));
    }
    // Future submission-side projection: no candidate, association, evidence, reason or hidden totals.
    // No HTTP endpoint or external login is enabled by this trusted-scope helper.
    public IQueryable<SafeMatchOutcome> SubmissionOutcomes(BackOfficeDbContext db)
    {
        var submissions=db.Set<MatchSubmission>().AsNoTracking().Where(x=>actor.AgencyId!=null && actor.Roles.Contains("agency-admin") && x.AgencyId==actor.AgencyId);
        return from submission in submissions join review in db.Set<MatchReview>().AsNoTracking() on submission.Id equals review.SubmissionId
            select new SafeMatchOutcome {SubmissionId=submission.Id,State=review.State};
    }
}
public sealed record SafeMatchOutcome {public Guid SubmissionId{get;init;}public string State{get;init;}="";}
