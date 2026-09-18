using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed partial class ServicingCapacityService
{
    public Task<CommandOutcome> RecordResponseAsync(ActorContext actor,Guid draftId,Guid cycleId,Guid caseId,Guid submissionId,
        byte[] version,byte[] caseVersion,Guid lease,Guid associationId,ServicingCapacityResponseDefinition definition,
        string body,string providerUnderwriter,string providerReference,DateTimeOffset receivedAt,string reason,string key,
        Guid correlation,CancellationToken token=default)
    {
        if(draftId==Guid.Empty || cycleId==Guid.Empty || caseId==Guid.Empty || submissionId==Guid.Empty || associationId==Guid.Empty ||
            lease==Guid.Empty || version is null || version.Length!=8 || caseVersion is null || caseVersion.Length!=8 || definition is null ||
            !ResponseText(body,10000) || !ResponseText(providerUnderwriter,200) || !ResponseText(providerReference,100) ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length<10)
            throw new QuoteOperationException(422,"servicing-capacity-response-invalid");
        reason=QuoteRatingService.Reason(reason);body=body.Trim();providerUnderwriter=providerUnderwriter.Trim();providerReference=providerReference.Trim();
        version=version.ToArray();caseVersion=caseVersion.ToArray();
        definition=JsonSerializer.Deserialize<ServicingCapacityResponseDefinition>(JsonSerializer.Serialize(definition))!;
        ServicingDecisionContext? held=null;ServicingCapacityCase? capacity=null;ServicingCapacitySubmission? submission=null;
        ServicingParsedCapacityResponse? parsed=null;ServicingEvidenceAssociation? proof=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/drafts/{draftId:D}/capacity/{caseId:D}/responses",key,correlation),
            new{draftId,cycleId,caseId,submissionId,version=Convert.ToBase64String(version),caseVersion=Convert.ToBase64String(caseVersion),lease,
                associationId,definition,body,providerUnderwriter,providerReference,receivedAt,reason},"servicing.capacity-response-recorded",
            async(db,ct)=>
            {
                held=await HoldEscalationAuthority(db,actor,draftId,cycleId,lease,ct);
                if(!actor.HasCapability("underwriting-record-capacity") || !held.Scope.Source.Scope.Actor.HasCapability("underwriting-record-capacity"))
                    throw new QuoteOperationException(403,"servicing-capacity-response-denied");
                capacity=await db.Set<ServicingCapacityCase>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCase WITH(UPDLOCK,HOLDLOCK) WHERE Id={caseId} AND DraftId={draftId} AND CycleId={cycleId}").SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-capacity-case-not-found");
                submission=await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==submissionId && x.CaseId==caseId,ct)
                    ??throw new QuoteOperationException(404,"servicing-capacity-submission-not-found");
                if(capacity.State is "draft" or "superseded" || capacity.CurrentSubmissionId!=submissionId || capacity.RatingId!=held.Rating.Id ||
                    capacity.ProviderId!=held.Scope.Eligible.BinderVersion.ProviderId || capacity.BinderVersionId!=held.Cycle.BinderVersionId)
                    throw new QuoteOperationException(409,"servicing-capacity-case-stale");
                var referral=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==capacity.ReferralId,ct);
                if(referral.State is "declined" or "superseded") throw new QuoteOperationException(409,"servicing-referral-reopen-required");
                try { parsed=ServicingCapacityResponseRules.Parse(definition,referral.RuleCode,referral.Dimension,submission.SubmittedAt,receivedAt,time.GetUtcNow(),ServicingEvidenceProjection.Slices(held)); }
                catch(ArgumentException) { throw new QuoteOperationException(422,"servicing-capacity-response-invalid"); }
                proof=await db.Set<ServicingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM ServicingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={associationId} AND DraftId={draftId} AND CycleId={cycleId}").AsNoTracking().SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"servicing-evidence-not-found");
                var required=(await ServicingEvidenceProjection.RequirementsAsync(db,held,ct)).SingleOrDefault(x=>x.Code=="capacity-response" && x.CapacitySubmissionId==submissionId);
                if(required is null || proof.CapacitySubmissionId!=submissionId || proof.RequirementCode!=required.Code || proof.RiskItemId is not null ||
                    proof.InputFingerprint!=required.InputFingerprint || !await ReviewedProofCurrent(db,proof,ct))
                    throw new QuoteOperationException(409,"servicing-capacity-response-proof-required");
                await RequireSelectedProof(db,submissionId,ct);
            },
            async(db,ct)=>
            {
                await held!.Current(db,factory,time,version,lease,ct);
                if(!CryptographicOperations.FixedTimeEquals(capacity!.RowVersion,caseVersion)) throw new QuoteOperationException(412,"servicing-capacity-case-stale");
                var now=time.GetUtcNow();
                var json=ResponseDefinitionJson(capacity,submission!,definition,parsed!);
                var response=new ServicingCapacityResponseRecord{SubmissionId=submissionId,CaseId=caseId,DraftId=draftId,CycleId=cycleId,
                    RevisionId=held.Cycle.RevisionId,RatingId=held.Rating.Id,ProviderId=capacity.ProviderId,
                    Sequence=checked((await db.Set<ServicingCapacityResponseRecord>().Where(x=>x.CaseId==caseId).MaxAsync(x=>(int?)x.Sequence,ct)??0)+1),
                    Outcome=definition.Outcome,Body=body,DefinitionJson=json,ContentHash=SHA256.HashData(Encoding.UTF8.GetBytes(json)),
                    ProviderUnderwriter=providerUnderwriter,ProviderReference=providerReference,ReceivedAt=receivedAt,RecordedAt=now,
                    RecordedBy=actor.UserId,EvidenceAssociationId=associationId,EvidenceReviewId=proof!.LatestReviewId,CreatedAt=now,CreatedBy=actor.UserId};
                db.Add(response);await db.SaveChangesAsync(ct);
                await StoreResponseConditions(db,response,parsed!,ct);
                capacity.CurrentResponseId=response.Id;capacity.State=ResponseState(definition.Outcome);capacity.UpdatedAt=now;
                db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,SubjectRecordId=caseId,
                    EventType="servicing.capacity-response-detail",Reason=reason,After=JsonSerializer.Serialize(new{response.Id,submissionId,definition.Outcome,associationId,response.EvidenceReviewId}),CorrelationId=correlation});
                return await held.Receipt(db,response.Id,201,now,ct);
            },token);
    }

    private static bool ResponseText(string? value,int maximum)=>!string.IsNullOrWhiteSpace(value) && value.Length<=maximum && !value.Any(c=>char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
    internal static string ResponseState(string outcome)=>outcome switch {"approve"=>"approved","approve-with-conditions"=>"conditional","query"=>"queried","decline"=>"declined",_=>throw new ArgumentException("Unknown carrier outcome.")};

    private static string ResponseDefinitionJson(ServicingCapacityCase capacity,ServicingCapacitySubmission submission,
        ServicingCapacityResponseDefinition definition,ServicingParsedCapacityResponse parsed)=>JsonSerializer.Serialize(new{
            format="servicing-capacity-response-1",draftId=capacity.DraftId,revisionId=capacity.RevisionId,cycleId=capacity.CycleId,ratingId=capacity.RatingId,
            caseId=capacity.Id,referralId=capacity.ReferralId,providerId=capacity.ProviderId,submissionId=submission.Id,
            submissionHash=Convert.ToHexStringLower(submission.ContextHash),outcome=definition.Outcome,validFrom=definition.ValidFrom,validTo=definition.ValidTo,
            authorisedLimits=definition.AuthorisedLimits,conditions=parsed.Conditions.Select(x=>new{definition=JsonSerializer.Deserialize<JsonElement>(x.DefinitionJson),effectiveDates=x.EffectiveDates}).ToArray()});

    internal static async Task StoreResponseConditions(BackOfficeDbContext db,ServicingCapacityResponseRecord response,ServicingParsedCapacityResponse parsed,CancellationToken token)
    {
        using var manifest=JsonDocument.Parse(response.DefinitionJson);
        for(var i=0;i<parsed.Conditions.Count;i++)
        {
            var condition=parsed.Conditions[i];var value=condition.Slices[0].Condition;
            db.Add(new ServicingCapacityCondition{ResponseId=response.Id,SubmissionId=response.SubmissionId,CaseId=response.CaseId,DraftId=response.DraftId,
                CycleId=response.CycleId,RevisionId=response.RevisionId,RatingId=response.RatingId,Sequence=i+1,Code=value.Code,Kind=value.Kind,
                DefinitionJson=condition.DefinitionJson,EffectiveDatesJson=manifest.RootElement.GetProperty("conditions")[i].GetProperty("effectiveDates").GetRawText(),
                CreatedBy=response.RecordedBy,CreatedAt=response.RecordedAt,UpdatedAt=response.RecordedAt});
        }
        await db.SaveChangesAsync(token);
    }

    private static async Task<bool> ReviewedProofCurrent(BackOfficeDbContext db,ServicingEvidenceAssociation proof,CancellationToken token)=>
        proof.WithdrawnEventId is null && proof.LatestReviewId is Guid review &&
        await db.Set<ServicingEvidenceEvent>().AnyAsync(x=>x.Id==review && x.AssociationId==proof.Id && x.Kind=="review" && x.Outcome=="accepted",token) &&
        await db.Set<ServicingEvidenceFile>().AnyAsync(x=>x.Id==proof.FileId && x.ScreeningState=="accepted",token);

    internal static async Task RequireSelectedProof(BackOfficeDbContext db,Guid submissionId,CancellationToken token)
    {
        var selected=await db.Set<ServicingCapacitySubmissionEvidence>().AsNoTracking().Where(x=>x.SubmissionId==submissionId).ToListAsync(token);
        foreach(var item in selected)
        {
            var proof=await db.Set<ServicingEvidenceAssociation>().FromSqlInterpolated($"SELECT * FROM ServicingEvidenceAssociation WITH(HOLDLOCK) WHERE Id={item.AssociationId}").AsNoTracking().SingleAsync(token);
            if(proof.LatestReviewId!=item.ReviewId || !await ReviewedProofCurrent(db,proof,token))
                throw new QuoteOperationException(409,"servicing-capacity-evidence-unavailable");
        }
    }
}
