using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyReconstructionInput(DateTimeOffset EffectiveAt,DateTimeOffset KnownAt,Guid? VersionId,string? ContentHash,string Reason);

public sealed partial class PolicyHistoryService
{
    public Task<CommandOutcome> ReconstructAsync(ActorContext actor,Guid termId,byte[] expected,PolicyReconstructionInput input,string key,Guid correlation,CancellationToken token=default)
    {
        if(expected.Length!=8||termId==Guid.Empty||input.EffectiveAt.Offset!=TimeSpan.Zero||input.KnownAt.Offset!=TimeSpan.Zero||
            (input.VersionId is null)!=(input.ContentHash is null)||input.VersionId==Guid.Empty||
            input.ContentHash is { } hash&&(hash.Length!=64||hash.Any(c=>!char.IsAsciiDigit(c)&&c is not(>='a' and <='f'))))
            throw new QuoteOperationException(400,"policy-reconstruction-input-invalid");
        input=input with{Reason=QuoteEvidenceRules.Reason(input.Reason)};Policy? policy=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/terms/{termId:D}/as-at/export",key,correlation),
            new{termId,expected=Convert.ToBase64String(expected),input},"policy.reconstruction-requested",
            async(db,ct)=>
            {
                if(!actor.HasCapability("policy-draft-write"))throw new QuoteOperationException(403,"policy-reconstruction-access-denied");
                var policyId=await db.Set<PolicyTerm>().Where(x=>x.Id==termId).Select(x=>(Guid?)x.PolicyId).SingleOrDefaultAsync(ct)
                    ??throw new QuoteOperationException(404,"policy-term-not-found");
                policy=await PolicyScope.Hold(db,actor,policyId,ct,true);
            },
            async(db,ct)=>
            {
                if(!policy!.RowVersion.SequenceEqual(expected))throw new QuoteOperationException(412,"policy-version-conflict");
                await db.Set<PolicyTerm>().FromSqlInterpolated($"SELECT * FROM PolicyTerm WITH(UPDLOCK,HOLDLOCK) WHERE Id={termId} AND PolicyId={policy.Id}").AsNoTracking().SingleAsync(ct);
                var candidates=(await PolicyTemporalSelector.Candidates(db,policy.Id,DateTimeOffset.MaxValue).ToArrayAsync(ct)).Where(x=>x.TermId==termId).ToArray();
                var selected=PolicyTemporalSelector.Select(candidates,policy.Id,input.EffectiveAt,input.KnownAt,termId);
                var version=selected is null?null:await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==selected.Candidate.VersionId&&x.PolicyId==policy.Id&&x.TermId==termId,ct);
                var contentHash=version is null?null:Convert.ToHexStringLower(version.ContentHash);
                if(input.VersionId!=version?.Id||input.ContentHash!=contentHash)throw new QuoteOperationException(409,"policy-reconstruction-selection-stale");
                var now=time.GetUtcNow();var request=new PolicyReconstructionRequest{PolicyId=policy.Id,TermId=termId,VersionId=version?.Id,VersionHash=version?.ContentHash,
                    EffectiveAt=input.EffectiveAt,KnownAt=input.KnownAt,CoverageState=selected?.State??"not-covered",ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,Reason=input.Reason};
                var documents=version is null?Array.Empty<Guid>():await db.Set<PolicyDocumentRequest>().Where(x=>x.VersionId==version.Id&&x.PolicyId==policy.Id).Select(x=>x.Id).ToArrayAsync(ct);
                request.ManifestJson=JsonSerializer.Serialize(new{format="policy-reconstruction-1",requestId=request.Id,policyId=policy.Id,termId,versionId=version?.Id,contentHash,
                    effectiveAt=input.EffectiveAt,knownAt=input.KnownAt,coverageState=request.CoverageState,documentRequestIds=documents,
                    versions=candidates.Select(x=>new{versionId=x.VersionId,effectiveAt=x.EffectiveAt,processedAt=x.ProcessedAt,kind=x.Kind,
                        applicability=x.ProcessedAt>input.KnownAt?"not-yet-known":x.VersionId==version?.Id?"selected":x.EffectiveAt>input.EffectiveAt?"not-yet-effective":"superseded"}).ToArray()});
                request.ManifestHash=SHA256.HashData(Encoding.UTF8.GetBytes(request.ManifestJson));
                var work=new OutboxWork{Kind="policy-reconstruction",OperationKey=$"policy-reconstruction/{request.Id:N}",SubjectRecordId=request.Id,Payload=request.ManifestJson,
                    CreatedAt=now,CreatedBy=actor.UserId,NextAttemptAt=now,CorrelationId=correlation};
                db.Add(work);await db.SaveChangesAsync(ct);request.WorkId=work.Id;db.Add(request);
                db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,SubjectRecordId=policy.Id,EventType="policy.reconstruction-requested",
                    Reason=input.Reason,CorrelationId=correlation,After=JsonSerializer.Serialize(new{requestId=request.Id,versionId=request.VersionId,contentHash})});
                await db.SaveChangesAsync(ct);
                return new(request.Id,201,JsonSerializer.Serialize(new{requestId=request.Id,policyId=policy.Id,termId,versionId=request.VersionId,contentHash,
                    effectiveAt=input.EffectiveAt,knownAt=input.KnownAt,coverageState=request.CoverageState,state=work.State}),Etag:Etag(policy.RowVersion));
            },token);
    }
}
