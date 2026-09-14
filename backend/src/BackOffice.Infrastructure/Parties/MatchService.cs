using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Parties;

public sealed class MatchOperationException(int status,string code):Exception("The match decision cannot be applied.")
{public int Status{get;}=status;public string Code{get;}=code;}

public static class MatchService
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public static async Task<MatchReview> AuthorizeAsync(BackOfficeDbContext db,ActorContext actor,Guid id,ValidatedMatchDecision input,CancellationToken token=default)
    {
        if(!actor.HasCapability("match-review"))throw new MatchOperationException(403,"match-decision-forbidden");
        var review=await new MatchScope(actor).Reviews(db).SingleOrDefaultAsync(x=>x.Id==id,token) ?? throw new MatchOperationException(404,"match-not-found");
        if(input.CandidateClientId is Guid candidate && candidate!=review.CandidateClientId)throw new MatchOperationException(422,"invalid-candidate");
        return review;
    }
    public static async Task<MatchReview> DecideAsync(BackOfficeDbContext db,ActorContext actor,Guid id,byte[] expected,ValidatedMatchDecision input,DateTimeOffset now,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Match decisions require the caller's audited transaction.");
        var authorized=await AuthorizeAsync(db,actor,id,input,token);
        // All decisions use intake -> review -> client -> relationship ordering, including reopen/query.
        var intake=await db.Set<MatchSubmission>().FromSqlInterpolated($"SELECT * FROM [MatchSubmission] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={authorized.SubmissionId}").SingleAsync(token);
        var review=await db.Set<MatchReview>().FromSqlInterpolated($"SELECT * FROM [MatchReview] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={id}").SingleAsync(token);
        if(!CryptographicOperations.FixedTimeEquals(review.RowVersion,expected))throw new MatchOperationException(412,"stale-match");
        var next=MatchRules.NextState(review.State,input.Outcome);
        // Phase 5 must add a real progressed-quote guard here before attaching quotes to intake.
        if(input.Outcome is "link" or "separate")
        {
            var clientId=input.Outcome=="link" ? review.CandidateClientId : intake.SeparateClientId;
            ClientAccount client;
            if(clientId is Guid existing)
            {
                client=await db.Set<ClientAccount>().FromSqlInterpolated($"SELECT * FROM [ClientAccount] WITH (UPDLOCK,ROWLOCK) WHERE [Id]={existing}").SingleAsync(token);
                if(client.IdentityState!="active")throw new MatchOperationException(409,"client-unavailable");
            }
            else
            {
                var identity=ClientIdentity.Validate(JsonSerializer.Deserialize<ClientWrite>(intake.IdentitySnapshot,Json)!);
                client=new ClientAccount {Reference=await ClientReferences.NextAsync(db,token),LegalName=identity.LegalName,NormalizedName=identity.NormalizedName,
                    EntityType=identity.EntityType,CompanyNumber=identity.CompanyNumber,Address=JsonSerializer.Serialize(identity.Address,Json),CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
                db.Add(client);intake.SeparateClientId=client.Id;
                Activity(db,actor,client.Id,null,"client.created",client.Id,"client",now);
            }
            var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==intake.AgencyId,token);
            if(agency.State is "suspended" or "abandoned")throw new MatchOperationException(409,"agency-unavailable");
            var relationship=await db.Set<ClientAgencyRelationship>().FromSqlInterpolated($"SELECT * FROM [ClientAgencyRelationship] WITH (UPDLOCK,ROWLOCK) WHERE [ClientId]={client.Id} AND [AgencyId]={intake.AgencyId}").SingleOrDefaultAsync(token);
            if(relationship is null)
            {
                relationship=new ClientAgencyRelationship {ClientId=client.Id,AgencyId=intake.AgencyId,CreatedAt=now,UpdatedAt=now,CreatedBy=actor.UserId};db.Add(relationship);
                client.UpdatedAt=now;if(db.Entry(client).State!=EntityState.Added)db.Entry(client).Property(x=>x.UpdatedAt).IsModified=true;
                Activity(db,actor,client.Id,relationship.Id,"client.relationship-created",client.Id,"client",now);
            }
            else if(relationship.State!="active")throw new MatchOperationException(409,"relationship-unavailable");
            intake.LinkedClientId=client.Id;intake.LinkedRelationshipId=relationship.Id;
        }
        MatchInformationRequest? request=null;
        if(input.Outcome=="query")
        {
            request=new MatchInformationRequest {MatchId=id,ActorId=actor.UserId,Description=input.Reason,CreatedBy=actor.UserId,CreatedAt=now,RecordedAt=now};db.Add(request);
        }
        // Reopen, Query and Decline preserve the prior association as history; state governs progression.
        review.State=next;review.UpdatedAt=now;intake.UpdatedAt=now;
        db.Entry(review).Property(x=>x.UpdatedAt).IsModified=true;db.Entry(intake).Property(x=>x.UpdatedAt).IsModified=true;
        db.Add(new MatchDecision {MatchId=id,Outcome=input.Outcome,Reason=input.Reason,ActorId=actor.UserId,OccurredAt=now,CreatedBy=actor.UserId,CreatedAt=now,
            ClientId=intake.LinkedClientId,RelationshipId=intake.LinkedRelationshipId,InformationRequestId=request?.Id});
        Activity(db,actor,intake.LinkedClientId ?? review.CandidateClientId,intake.LinkedRelationshipId ?? review.CandidateRelationshipId,"match."+input.Outcome,id,"match",now);
        await db.SaveChangesAsync(token);return review;
    }
    private static void Activity(BackOfficeDbContext db,ActorContext actor,Guid client,Guid? relationship,string eventType,Guid recordId,string kind,DateTimeOffset now)
        =>db.Add(new ClientActivity {ClientId=client,RelationshipId=relationship,ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,EventType=eventType,RecordId=recordId,RecordKind=kind});
}
