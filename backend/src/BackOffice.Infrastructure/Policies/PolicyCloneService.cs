using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Policies;

public sealed record PolicyCloneInput(Guid VersionId,Guid RelationshipId,Guid ConfirmedTermsId,string Reason);
public sealed record PolicyCloneTerms(Guid PolicyId,Guid VersionId,Guid RelationshipId,Guid TermsId,int TermsVersion,string PolicyEtag);

public sealed partial class PolicyHistoryService
{
    private readonly SqlCommandBoundary commands=new(factory,time);
    private sealed record HeldClone(Policy Policy,PolicyVersion Version,QuoteRelationshipScope Target,EligibleQuoteCapture Destination,QuoteVersionPins SourcePins);

    public async Task<PolicyCloneTerms> CloneTermsAsync(ActorContext actor,Guid policyId,Guid versionId,Guid relationshipId,CancellationToken token=default)
    {
        await using var db=await factory.CreateDbContextAsync(token);await using var tx=await db.Database.BeginTransactionAsync(token);
        var held=await HoldClone(db,actor,policyId,versionId,relationshipId,token);
        var result=new PolicyCloneTerms(policyId,versionId,relationshipId,held.Destination.Terms.Id,held.Destination.Terms.Version,Etag(held.Policy.RowVersion));
        await tx.CommitAsync(token);return result;
    }

    public Task<CommandOutcome> CloneAsync(ActorContext actor,Guid policyId,byte[] expected,PolicyCloneInput input,string key,Guid correlation,CancellationToken token=default)
    {
        if(expected.Length!=8||input.VersionId==Guid.Empty||input.RelationshipId==Guid.Empty||input.ConfirmedTermsId==Guid.Empty)
            throw new QuoteOperationException(400,"policy-clone-input-invalid");
        input=input with{Reason=QuoteEvidenceRules.Reason(input.Reason)};HeldClone? held=null;
        return commands.ExecuteAuthorizedAsync(new(actor.UserId,$"/api/v1/policies/{policyId:D}/clone",key,correlation),
            new{policyId,expected=Convert.ToBase64String(expected),input},"policy.cloned",
            async(db,ct)=>
            {
                held=await HoldClone(db,actor,policyId,input.VersionId,input.RelationshipId,ct);
                if(held.Destination.Terms.Id!=input.ConfirmedTermsId)throw new QuoteOperationException(409,"policy-clone-terms-changed");
            },
            async(db,ct)=>
            {
                var h=held!;if(!h.Policy.RowVersion.SequenceEqual(expected))throw new QuoteOperationException(412,"policy-version-conflict");
                using var snapshot=JsonDocument.Parse(h.Version.SnapshotJson);
                var prepared=PolicyHistoryRules.Clone(snapshot.RootElement,h.SourcePins,h.Destination.Pins);var now=time.GetUtcNow();
                var quote=new Quote{AgencyId=h.Target.Agency.Id,ClientId=h.Target.Client.Id,RelationshipId=h.Target.Relationship.Id,ProductId=h.Destination.Product.Id,
                    CreatedBy=actor.UserId,CreatedAt=now,UpdatedAt=now};
                db.Add(quote);await db.SaveChangesAsync(ct);
                await QuoteService.Append(db,quote,h.Destination,prepared.Capture,1,input.Reason,actor.UserId,now,ct);
                await QuoteMatching.AttachOrReviewAsync(db,quote,h.Target,null,actor.UserId,now,ct);
                var lineage=new PolicyQuoteClone{PolicyId=policyId,VersionId=h.Version.Id,VersionHash=h.Version.ContentHash,QuoteId=quote.Id,RevisionId=quote.CurrentRevisionId!.Value,
                    ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,Reason=input.Reason,ItemMapJson=JsonSerializer.Serialize(prepared.ItemIds)};
                db.Add(lineage);db.Add(new AuditEvent{ActorId=actor.UserId,CreatedBy=actor.UserId,CreatedAt=now,OccurredAt=now,SubjectRecordId=policyId,
                    EventType="policy.cloned",Reason=input.Reason,CorrelationId=correlation,After=JsonSerializer.Serialize(new{quoteId=quote.Id,versionId=h.Version.Id})});
                await db.SaveChangesAsync(ct);var etag=Etag(quote.RowVersion);
                return new(quote.Id,201,JsonSerializer.Serialize(new{quoteId=quote.Id,revisionId=quote.CurrentRevisionId,lineageId=lineage.Id,sourcePolicyId=policyId,sourceVersionId=h.Version.Id,quoteEtag=etag}),Etag:etag);
            },token);
    }

    private async Task<HeldClone> HoldClone(BackOfficeDbContext db,ActorContext actor,Guid policyId,Guid versionId,Guid relationshipId,CancellationToken token)
    {
        if(!actor.HasCapability("quote-capture"))throw new QuoteOperationException(403,"policy-clone-access-denied");
        var policy=await PolicyScope.Hold(db,actor,policyId,token,true);
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleOrDefaultAsync(x=>x.Id==versionId&&x.PolicyId==policyId,token)
            ??throw new QuoteOperationException(404,"policy-version-not-found");
        if(!await db.Set<ClientAgencyRelationship>().AnyAsync(x=>x.Id==relationshipId&&x.ClientId==policy.ClientId&&x.AgencyId==policy.AgencyId,token))
            throw new QuoteOperationException(404,"policy-clone-target-not-found");
        var target=await QuoteScope.ForRelationshipAsync(db,actor,relationshipId,QuoteAccess.Capture,token);
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync(x=>x.Id==version.TermId&&x.PolicyId==policyId,token);
        var source=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==policy.SourceQuoteId,token);
        var revision=await db.Set<QuoteRevision>().AsNoTracking().SingleAsync(x=>x.Id==source.CurrentRevisionId&&x.QuoteId==source.Id,token);
        var pins=QuoteService.Pins(revision) with{ProductVersionId=term.ProductVersionId};
        var destination=await QuoteCaptureEligibility.ResolveAsync(db,target,term.ProductVersionId,time.GetUtcNow(),token:token);
        return new(policy,version,target,destination,pins);
    }
}
