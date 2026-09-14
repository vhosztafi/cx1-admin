using System.Text.Json;
using System.Text.Json.Serialization;
using BackOffice.Application.Parties;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Persistence;

public static class MatchDemoSeed
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) {DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull};
    public static readonly Guid RuleId=Guid.Parse("38000000-0000-4000-8000-000000000001");
    public static Guid SubmissionId(int index)=>Guid.Parse($"39000000-0000-4000-8000-{index:D12}");
    public static Guid ReviewId(int index)=>Guid.Parse($"3a000000-0000-4000-8000-{index:D12}");
    public static async Task SeedAsync(BackOfficeDbContext db,CancellationToken token=default)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Match demo seed requires the shared seed transaction.");
        var actor=await db.Set<StaffUser>().Where(x=>x.Email=="underwriter@cover.example").Select(x=>x.Id).SingleAsync(token);
        var now=DateTimeOffset.UtcNow;
        var setting=await db.Set<SettingVersion>().SingleOrDefaultAsync(x=>x.Scope=="matching-rule" && x.Version==1,token);
        if(setting is null)
        {
            var snapshot=new MatchRuleSnapshot(RuleId,1,"refer",true,"Fictional demo rule: compare submitted identity with the recorded candidate. An underwriter must decide; no accounts are automatically merged.");
            setting=new SettingVersion {Id=RuleId,Scope="matching-rule",Version=1,Values=JsonSerializer.Serialize(snapshot,Json),EffectiveFrom=now,CreatedBy=actor};
            db.Add(setting);await db.SaveChangesAsync(token);
        }
        var pinned=JsonSerializer.Deserialize<MatchRuleSnapshot>(setting.Values,Json) ?? throw new InvalidOperationException("The demo matching rule snapshot is unavailable.");
        if(pinned.Id!=setting.Id || pinned.Version!=setting.Version)throw new InvalidOperationException("The demo matching rule identity is inconsistent.");
        var candidate=await db.Set<ClientAccount>().SingleOrDefaultAsync(x=>x.Id==PartyDemoSeed.ClientId(3),token);
        var relationship=await db.Set<ClientAgencyRelationship>().SingleOrDefaultAsync(x=>x.Id==PartyDemoSeed.RelationshipId(3,1) && x.State=="active",token);
        if(candidate is null || relationship is null)return;
        for(var index=1;index<=6;index++)
        {
            var submissionId=SubmissionId(index);var reviewId=ReviewId(index);
            if(await db.Set<MatchSubmission>().AnyAsync(x=>x.Id==submissionId,token) || await db.Set<MatchReview>().AnyAsync(x=>x.Id==reviewId,token))continue;
            var identity=new ClientWrite($"Fictional Intake Traders {index:00}","sole-trader",new("1 Fictional Workshop Road","Sheffield","S1 1AA","GB"));
            var candidatePostcode=JsonSerializer.Deserialize<AddressWrite>(candidate.Address,Json)!.Postcode!;
            var evidence=MatchRules.ValidateEvidence(identity,[
                new("legal-name","Fictional similar trading name requiring review",identity.LegalName!,candidate.LegalName,"strong","near-match"),
                new("postcode","Comparison with the recorded candidate postcode",identity.Address!.Postcode!,candidatePostcode,"moderate",string.Equals(identity.Address.Postcode,candidatePostcode,StringComparison.OrdinalIgnoreCase) ? "match" : "different")],pinned,index%3==0 ? "low" : index%3==1 ? "high" : "medium");
            var intake=new MatchSubmission {Id=submissionId,Reference=$"MI-DEMO-{index:0000}",AgencyId=index%2==0 ? PartyDemoSeed.FirstAgencyId : PartyDemoSeed.SecondAgencyId,IdentitySnapshot=JsonSerializer.Serialize(evidence.Identity,Json),CreatedAt=now,UpdatedAt=now,CreatedBy=actor};
            var review=new MatchReview {Id=reviewId,SubmissionId=submissionId,CandidateClientId=candidate.Id,CandidateRelationshipId=relationship.Id,RuleVersionId=setting.Id,RuleSnapshot=JsonSerializer.Serialize(pinned,Json),Signals=JsonSerializer.Serialize(evidence.Signals,Json),Confidence=evidence.Confidence,State=index==5 ? "declined" : index==6 ? "queried" : "pending",CreatedAt=now,UpdatedAt=now,CreatedBy=actor};
            db.AddRange(intake,review);
            if(index>=5)
            {
                // Historical fictional fixtures carry their real stored trail; no delivery is implied.
                var reason=index==5 ? "Fictional demo intake declined after identity review." : "Fictional demo request to confirm the submitted trading name.";
                MatchInformationRequest? request=null;
                if(index==6){request=new MatchInformationRequest {MatchId=reviewId,ActorId=actor,Description=reason,RecordedAt=now,CreatedAt=now,CreatedBy=actor};db.Add(request);}
                db.Add(new MatchDecision {MatchId=reviewId,Outcome=index==5 ? "decline" : "query",Reason=reason,ActorId=actor,InformationRequestId=request?.Id,OccurredAt=now,CreatedAt=now,CreatedBy=actor});
            }
            db.Add(new AuditEvent {ActorId=actor,CreatedBy=actor,OccurredAt=now,EventType="match.demo-created",CorrelationId=Guid.NewGuid(),After=JsonSerializer.Serialize(new {resourceId=reviewId})});
            await db.SaveChangesAsync(token);
        }
    }
}
