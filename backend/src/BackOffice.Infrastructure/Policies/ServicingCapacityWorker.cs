using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Underwriting;
using BackOffice.Infrastructure.Identity;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed class ServicingCapacityWorker(IDbContextFactory<BackOfficeDbContext> factory,TimeProvider time)
{
    private static readonly JsonSerializerOptions Json=ServicingRatingService.Json;

    public async Task<CapacityProviderOutcome> ExecuteProviderAsync(JobLease lease,CancellationToken token=default)
    {
        if(lease.Kind!=ServicingCapacityService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var submission=await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)
            ??throw Failure(JobFailure.InvalidPayload);
        if(lease.OperationKey!=$"servicing-capacity/{submission.Id:N}" || lease.ScenarioVersionId!=submission.ScenarioVersionId) throw Failure(JobFailure.ProviderConflict);
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==submission.ScenarioVersionId,token);
        var scenario=CapacitySeed.Parse(setting)?.Scenario??throw Failure(JobFailure.InvalidPayload);
        var inputCycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==submission.CycleId,token);
        if(!CapacitySeed.ForProduct(scenario,ServicingRatingInput.Read(inputCycle.InputJson,inputCycle.InputHash).IsCommercial)) throw Failure(JobFailure.InvalidPayload);
        await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var operation=await db.Set<DemoProviderOperation>().FromSqlInterpolated($"SELECT * FROM DemoProviderOperation WITH(UPDLOCK,HOLDLOCK) WHERE Kind={lease.Kind} AND OperationKey={lease.OperationKey}").SingleOrDefaultAsync(token);
        var existed=operation is not null;
        if(operation is not null && (operation.ScenarioVersionId!=lease.ScenarioVersionId || !CryptographicOperations.FixedTimeEquals(operation.RequestHash,submission.ContextHash))) throw Failure(JobFailure.ProviderConflict);
        if(operation?.Result is not null)
        {
            var previous=JsonSerializer.Deserialize<CapacityProviderOutcome>(operation.Result,Json)??throw Failure(JobFailure.ProviderConflict);
            await tx.CommitAsync(token);return previous;
        }
        if(operation is null)
        {
            operation=new DemoProviderOperation{Kind=lease.Kind,OperationKey=lease.OperationKey,ScenarioVersionId=lease.ScenarioVersionId,
                RequestHash=submission.ContextHash,CreatedAt=time.GetUtcNow()};db.Add(operation);
        }
        if(!existed && scenario is "transient-then-approve" or "cc-transient-then-approve")
        {
            operation.State="transient-failed";await db.SaveChangesAsync(token);await tx.CommitAsync(token);throw Failure(JobFailure.ProviderUnavailable);
        }
        using var document=JsonDocument.Parse(submission.ContextJson);var context=document.RootElement;
        var commercial=context.TryGetProperty("productCode",out var product) && product.GetString()=="commercial-combined";
        var dimension=commercial?context.GetProperty("dimension").GetString()!:CapacityRules.Dimension(context.GetProperty("ruleCode").GetString()!,context.GetProperty("dimension").GetString()!);
        var targets=context.TryGetProperty("conditionTargets",out var targetList)?targetList.EnumerateArray().ToArray():[];
        var outcome=scenario switch {"query-proof"=>"query","decline-trade"=>"decline",
            "conditional-security" when dimension=="stock-limit" && targets.Length is >0 and <=20=>"approve-with-conditions",
            "conditional-security"=>"query",_ when dimension=="stock-limit"=>"approve",_=>"query"};
        var now=time.GetUtcNow();
        var conditions=outcome=="approve-with-conditions"?targets.Select(x=>JsonSerializer.SerializeToElement(new{
            definition=new{code="overnight-security",premisesId=x.GetProperty("premisesId").GetGuid(),wordingVersion="1"},
            effectiveDates=x.GetProperty("effectiveDates").EnumerateArray().Select(d=>d.GetDateTimeOffset()).ToArray()})).ToArray():[];
        JsonElement[] limits=outcome is "approve" or "approve-with-conditions"?[JsonSerializer.SerializeToElement(new{dimension="stock-limit",maximumAmount="150000.00"})]:[];
        if(commercial)
        {
            var triggers=context.GetProperty("requiredAuthority").GetProperty("triggers").Deserialize<ServicingReferralTrigger[]>(Json)??[];
            var target=context.GetProperty("targetId").ValueKind==JsonValueKind.Null?(Guid?)null:context.GetProperty("targetId").GetGuid();
            var relevant=triggers.Where(x=>x.Requirement.Dimension==dimension && x.Requirement.TargetId==target).ToArray();
            var requested=relevant.Select(x=>x.Requirement.RequestedAmount).Max();
            var applicable=CommercialCapacityRules.SupportedDimension(dimension) && requested is >0 &&
                (CommercialCapacityRules.LocationDimension(dimension)?target is not null:target is null);
            outcome=scenario switch {"cc-decline"=>"decline","cc-query-proof"=>"query",_ when !applicable=>"query","cc-conditional-proof"=>"approve-with-conditions",_=>"approve"};
            conditions=[];limits=[];
            if(outcome is "approve" or "approve-with-conditions")
            {
                var extent=new Dictionary<string,object>{{"dimension",dimension},{"maximumAmount",requested!.Value.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)}};
                if(target is {} id)extent["riskItemId"]=id;
                var serialized=JsonSerializer.SerializeToElement(extent,Json);_=CommercialCapacityRules.Extension(serialized);limits=[serialized];
                if(outcome=="approve-with-conditions")
                {
                    var proof=target is {} location?JsonSerializer.SerializeToElement(new{code="provide-cc-location-proof",riskItemId=location}):JsonSerializer.SerializeToElement(new{code="provide-cc-liability-proof"});
                    conditions=[JsonSerializer.SerializeToElement(new{definition=proof,effectiveDates=relevant.Select(x=>x.EffectiveAt).Distinct().Order().ToArray()})];
                }
            }
        }
        var approving=outcome is "approve" or "approve-with-conditions";
        var startsAt=context.GetProperty("startsAt").GetDateTimeOffset();
        var definition=JsonSerializer.Serialize(new{format="servicing-capacity-response-1",draftId=submission.DraftId,revisionId=submission.RevisionId,
            cycleId=submission.CycleId,ratingId=submission.RatingId,caseId=submission.CaseId,referralId=context.GetProperty("referralId").GetGuid(),
            providerId=context.GetProperty("providerId").GetGuid(),submissionId=submission.Id,submissionHash=Convert.ToHexStringLower(submission.ContextHash),outcome,
            validFrom=approving?(DateTimeOffset?)(startsAt<now?startsAt:now):null,
            validTo=approving?(DateTimeOffset?)context.GetProperty("endsAt").GetDateTimeOffset():null,
            authorisedLimits=limits,conditions},Json);
        var result=new CapacityProviderOutcome(operation.Id,"servicing-capacity-"+operation.Id.ToString("N"),outcome,
            "Fictional demo provider: "+outcome+". Scenario: "+scenario+". Applies only to the retained servicing submission.",now,definition);
        operation.State="succeeded";operation.CompletedAt=now;operation.Result=JsonSerializer.Serialize(result,Json);
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);return result;
    }

    public async Task<bool> ApplyAsync(JobLease lease,CapacityProviderOutcome outcome,CancellationToken token=default)
    {
        if(lease.Kind!=ServicingCapacityService.WorkKind) throw Failure(JobFailure.InvalidPayload);
        await using var db=await factory.CreateDbContextAsync(token);
        var submission=await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkId==lease.WorkId,token)??throw Failure(JobFailure.InvalidPayload);
        var cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==submission.CycleId,token);
        var sourceId=await db.Set<Policy>().Where(x=>x.Id==cycle.PolicyId).Select(x=>x.SourceQuoteId).SingleAsync(token);
        var agencyId=await db.Set<Quote>().Where(x=>x.Id==sourceId).Select(x=>x.AgencyId).SingleAsync(token);
        var reference=await IdentitySnapshot.Reference(db,submission.SubmittedBy,token);
        await using var tx=await db.Database.BeginTransactionAsync(token);
        await db.Set<Agency>().FromSqlInterpolated($"SELECT * FROM Agency WITH(UPDLOCK,HOLDLOCK) WHERE Id={agencyId}").AsNoTracking().SingleAsync(token);
        var identity=reference is null?null:await IdentitySnapshot.Lock(db,reference,token);
        await db.Set<Quote>().FromSqlInterpolated($"SELECT * FROM Quote WITH(UPDLOCK,HOLDLOCK) WHERE Id={sourceId}").AsNoTracking().SingleAsync(token);
        await db.Set<Policy>().FromSqlInterpolated($"SELECT * FROM Policy WITH(UPDLOCK,HOLDLOCK) WHERE Id={cycle.PolicyId}").AsNoTracking().SingleAsync(token);
        await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(UPDLOCK,HOLDLOCK) WHERE Id={cycle.BaseTermId}").AsNoTracking().SingleAsync(token);
        var draft=await db.Set<ServicingDraft>().FromSqlInterpolated($"SELECT * FROM ServicingDraft WITH(UPDLOCK,HOLDLOCK) WHERE Id={cycle.DraftId}").SingleAsync(token);
        var now=time.GetUtcNow();
        var work=await SqlJobLeases.OwnedAsync(db,lease,now,token);
        var operation=await db.Set<DemoProviderOperation>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==outcome.OperationId && x.Kind==lease.Kind && x.OperationKey==lease.OperationKey,token);
        if(operation?.Result is null || operation.State!="succeeded" || operation.ScenarioVersionId!=submission.ScenarioVersionId || lease.ScenarioVersionId!=submission.ScenarioVersionId ||
            lease.OperationKey!=$"servicing-capacity/{submission.Id:N}" || outcome.EventId!="servicing-capacity-"+operation.Id.ToString("N") ||
            !CryptographicOperations.FixedTimeEquals(operation.RequestHash,submission.ContextHash)) throw Failure(JobFailure.ProviderConflict);
        var serialized=JsonSerializer.Serialize(outcome,Json);var hash=SHA256.HashData(Encoding.UTF8.GetBytes(serialized));const string provider="demo-servicing-capacity";
        var inbox=await db.Set<AdapterInbox>().FromSqlInterpolated($"SELECT * FROM AdapterInbox WITH(UPDLOCK,HOLDLOCK) WHERE Provider={provider} AND EventId={outcome.EventId}").SingleOrDefaultAsync(token);
        if(inbox is not null)
        {
            if(!CryptographicOperations.FixedTimeEquals(inbox.ContentHash,hash) && !await db.Set<AdapterQuarantine>().AnyAsync(x=>x.InboxId==inbox.Id && x.ObservedHash==hash,token))
            {
                db.Add(new AdapterQuarantine{InboxId=inbox.Id,ObservedHash=hash,ReceivedAt=now,Reason="conflicting-servicing-capacity-event",CreatedAt=now});await db.SaveChangesAsync(token);
            }
            await tx.CommitAsync(token);return false;
        }
        if(work is null) return false;
        if(work.SubjectRecordId!=submission.Id || work.OperationKey!=lease.OperationKey || work.ScenarioVersionId!=lease.ScenarioVersionId || operation.Result!=serialized) throw Failure(JobFailure.ProviderConflict);
        var capacity=await db.Set<ServicingCapacityCase>().FromSqlInterpolated($"SELECT * FROM ServicingCapacityCase WITH(UPDLOCK,HOLDLOCK) WHERE Id={submission.CaseId}").SingleAsync(token);
        ServicingDecisionContext? held=null;ServicingParsedCapacityResponse? parsed=null;
        if(identity is not null && draft.State=="draft" && draft.CurrentCycleId==cycle.Id && draft.CurrentRevisionId==cycle.RevisionId &&
            capacity.State is not ("draft" or "superseded") && capacity.CurrentSubmissionId==submission.Id && capacity.CurrentResponseId is null)
        {
            try
            {
                var actor=new ActorContext(identity.User.Id,identity.User.TeamId,identity.User.AgencyId,identity.Roles.Select(x=>x.Code).ToHashSet(StringComparer.Ordinal));
                held=await ServicingDecisionContext.Hold(db,actor,draft.Id,"underwriting-escalate",now,token,cycle.Id);
                var remaining=held.Input.Term with{Kind="short-period",StartsAt=held.Input.Slices[0].EffectiveAt};
                var grants=await QuoteUnderwritingScope.GrantsAsync(db,held.Scope.Source,cycle.ProductVersionId,held.Scope.Eligible.BinderVersion,
                    held.Scope.Eligible.Capture.Product.Code,remaining,now,token);
                var referral=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==capacity.ReferralId,token);
                if(held.Rating.Id!=submission.RatingId || held.Rating.ExpiresAt<=now || grants.Count==0 || referral.State is "declined" or "superseded" ||
                    capacity.ProviderId!=held.Scope.Eligible.BinderVersion.ProviderId ||
                    !await db.Set<CapacityProvider>().FromSqlInterpolated($"SELECT * FROM CapacityProvider WITH(HOLDLOCK) WHERE Id={capacity.ProviderId} AND State='active'").AsNoTracking().AnyAsync(token)) held=null;
                if(held is not null)
                {
                    await ServicingCapacityService.RequireSelectedProof(db,submission.Id,token);
                    var definition=JsonSerializer.Deserialize<ServicingCapacityResponseDefinition>(outcome.DefinitionJson,Json)??throw Failure(JobFailure.ProviderConflict);
                    parsed=ServicingCapacityResponseRules.Parse(definition,referral.RuleCode,referral.Dimension,submission.SubmittedAt,outcome.ReceivedAt,now,ServicingEvidenceProjection.Slices(held));
                    foreach(var condition in parsed.Conditions)
                        await ServicingTermsService.RequireConditionTerms(db,held,condition.Slices[0].Condition,token);
                }
            }
            catch(QuoteOperationException) { held=null; }
            catch(ArgumentException) { throw Failure(JobFailure.ProviderConflict); }
        }
        inbox=new AdapterInbox{Provider=provider,EventId=outcome.EventId,ContentHash=hash,WorkId=work.Id,State="applied",AppliedAt=now,CreatedAt=now,UpdatedAt=now};
        db.Add(inbox);await db.SaveChangesAsync(token);
        var response=new ServicingCapacityResponseRecord{SubmissionId=submission.Id,CaseId=capacity.Id,DraftId=submission.DraftId,CycleId=submission.CycleId,
            RevisionId=submission.RevisionId,RatingId=submission.RatingId,ProviderId=capacity.ProviderId,
            Sequence=checked((await db.Set<ServicingCapacityResponseRecord>().Where(x=>x.CaseId==capacity.Id).MaxAsync(x=>(int?)x.Sequence,token)??0)+1),
            Provenance="demo-provider",Outcome=outcome.Outcome,Body=outcome.Body,DefinitionJson=outcome.DefinitionJson,
            ContentHash=SHA256.HashData(Encoding.UTF8.GetBytes(outcome.DefinitionJson)),ProviderUnderwriter="Fictional demo carrier underwriter",ProviderReference=outcome.EventId,
            ProviderEventId=outcome.EventId,ProviderOperationId=operation.Id,InboxId=inbox.Id,ReceivedAt=outcome.ReceivedAt,RecordedAt=now,
            RecordedBy=submission.SubmittedBy,CreatedBy=submission.SubmittedBy,CreatedAt=now,ApplicationState=held is null?"superseded":"applied"};
        db.Add(response);await db.SaveChangesAsync(token);
        if(held is not null)
        {
            await ServicingCapacityService.StoreResponseConditions(db,response,parsed!,token);
            capacity.CurrentResponseId=response.Id;capacity.State=ServicingCapacityService.ResponseState(outcome.Outcome);capacity.UpdatedAt=now;
            await held.Receipt(db,response.Id,201,now,token);
        }
        else if(capacity.State!="draft" && capacity.CurrentSubmissionId==submission.Id && capacity.CurrentResponseId is null) {capacity.State="superseded";capacity.UpdatedAt=now;}
        var attempt=await db.Set<AdapterAttempt>().SingleAsync(x=>x.WorkId==work.Id && x.AttemptNumber==lease.Attempt,token);
        attempt.EndedAt=now;attempt.Outcome=held is null?"superseded":"succeeded";
        attempt.Response=JsonSerializer.Serialize(new{caseId=capacity.Id,responseId=response.Id,applicable=held is not null});
        work.State="succeeded";work.CompletedAt=now;work.LeaseToken=null;work.LeaseExpiresAt=null;work.ErrorCode=null;work.Result=attempt.Response;
        db.Add(new AuditEvent{ActorId=submission.SubmittedBy,SubjectRecordId=draft.Id,EventType="servicing.capacity-demo-response",OccurredAt=now,
            CorrelationId=work.CorrelationId,After=attempt.Response});
        await db.SaveChangesAsync(token);await tx.CommitAsync(token);
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==submission.ScenarioVersionId,token);
        if(CapacitySeed.Parse(setting)?.Scenario=="conflicting-duplicate") await ApplyAsync(lease,outcome with{Body="Conflicting fictional response body"},token);
        return true;
    }

    private static CapacityProviderException Failure(JobFailure failure)=>new(failure);
}
