using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<string> VerifyServicingDeliveryQueue(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,ServicingTermsVersion contract,Guid fileId,Guid fence,string etag,string scenario="success")
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string Key()=>Guid.NewGuid().ToString();
        await using(var tx=await db.Database.BeginTransactionAsync()){await ServicingTermsSeed.SeedAsync(db);await tx.CommitAsync();}
        if(scenario is "timeout-after-success" or "transient-once" or "reject")
        {
            db.Add(new SettingVersion{Scope="servicing-delivery",Version=2,EffectiveFrom=f.Clock.GetUtcNow(),
                Values=System.Text.Json.JsonSerializer.Serialize(new{demo=true,kind="servicing-delivery",schemaVersion="1",scenario})});
            await db.SaveChangesAsync();
        }
        var source=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==f.QuoteId);
        var contact=await db.Set<Contact>().AsNoTracking().FirstAsync(x=>x.ClientId==source.ClientId && x.RelationshipId==source.RelationshipId && x.EndedAt==null && x.Email!=null);
        var terms=new ServicingTermsService(f.Factory,f.Clock);var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        Assert.Equal("servicing-proof-review-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.SendAsync(f.Underwriter,cycle.DraftId,cycle.Id,contract.Id,[contact.Id],Version(etag),fence,Key(),Guid.NewGuid()))).Code);
        var signature=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="signed-statement").Requirement;
        var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(etag),fence,fileId,signature.Code,null,signature.InputFingerprint,"Attach fictional signed contract",Key(),Guid.NewGuid());
        var proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);Assert.Equal(contract.Id,proof.TermsVersionId);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,proof.Id,Version(attached.Etag!),fence,proof.RowVersion,"accepted",signature.InputFingerprint,"Review fictional signed contract",Key(),Guid.NewGuid());
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.SendAsync(f.Underwriter,cycle.DraftId,cycle.Id,contract.Id,[Guid.NewGuid()],Version(reviewed.Etag!),fence,Key(),Guid.NewGuid()))).Status);
        var version=Version(reviewed.Etag!);var key=Key();
        var sent=await terms.SendAsync(f.Underwriter,cycle.DraftId,cycle.Id,contract.Id,[contact.Id],version,fence,key,Guid.NewGuid());Assert.Equal(202,sent.Status);
        Assert.True((await terms.SendAsync(f.Underwriter,cycle.DraftId,cycle.Id,contract.Id,[contact.Id],version,fence,key,Guid.NewGuid())).Replayed);
        var delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.Id==sent.ResourceId);
        Assert.Equal("queued",delivery.State);Assert.Null(delivery.CompletedAt);Assert.Null(delivery.ProviderOperationId);
        Assert.Equal(contract.Id,delivery.TermsVersionId);
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==delivery.WorkId);
        Assert.Equal("servicing-delivery",work.Kind);Assert.Equal(delivery.Id,work.SubjectRecordId);
        Assert.Equal(delivery.Id,(await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==cycle.Id)).CurrentDeliveryId);
        Assert.Equal("servicing-delivery-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.AcceptAsync(f.Underwriter,cycle.DraftId,Version(sent.Etag!),fence,
            new(cycle.Id,cycle.CurrentRatingId!.Value,contract.Id,delivery.Id,contract.TermsHash,new string('b',64),"Fictional customer",f.Clock.GetUtcNow(),"email",Guid.NewGuid()),Key(),Guid.NewGuid()))).Code);
        var leases=new SqlJobLeases(f.Factory,f.Clock);var worker=new ServicingDeliveryWorker(f.Factory,f.Clock);
        if(scenario=="withdraw-signature")
        {
            proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==proof.Id);
            await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,proof.Id,Version(sent.Etag!),fence,proof.RowVersion,"Withdraw signature before delivery completion",Key(),Guid.NewGuid());
            Assert.Equal("servicing-proof-review-required",(await Assert.ThrowsAsync<QuoteOperationException>(()=>terms.SendAsync(f.Underwriter,cycle.DraftId,cycle.Id,contract.Id,[contact.Id],version,fence,key,Guid.NewGuid()))).Code);
        }
        if(scenario=="sender-revoked")
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State='suspended' WHERE Id={f.Underwriter.UserId}");
        if(scenario=="recipient-ended")
            // The retained quote fixture creates contacts with the wall clock.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Contact SET EndedAt={DateTimeOffset.UtcNow},EndedBy={f.Underwriter.UserId},EndReason='Fictional contact ended before delivery',IsPrimary=0 WHERE Id={contact.Id}");
        if(scenario=="amended")
        {
            var revision=await db.Set<ServicingRevision>().AsNoTracking().SingleAsync(x=>x.Id==cycle.RevisionId);
            var proposal=System.Text.Json.Nodes.JsonNode.Parse(revision.ProposalJson)!;
            proposal["changes"]![0]!["payload"]!["fullName"]="Fictional changed after queued terms";
            await new ServicingDraftService(f.Factory,f.Clock).SaveAsync(f.Underwriter,cycle.DraftId,Version(sent.Etag!),fence,proposal.ToJsonString(),Key(),Guid.NewGuid());
        }
        var claim=(await leases.ClaimWorkAsync(ServicingTermsService.WorkKind,work.Id))!;Assert.NotNull(claim);
        if(scenario=="terminal-failure")
        {
            Assert.True(await leases.FailAsync(claim,JobFailure.InvalidPayload));Assert.False(await leases.FailAsync(claim,JobFailure.InvalidPayload));
            var failed=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.Id==delivery.Id);
            Assert.Equal("failed",failed.State);Assert.Equal("invalid-payload",failed.OutcomeCode);Assert.NotNull(failed.AttemptId);Assert.Null(failed.ProviderOperationId);
            Assert.Equal(0,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ServicingTermsService.WorkKind));return sent.Etag!;
        }
        if(scenario is "timeout-after-success" or "transient-once")
        {
            var failure=await Assert.ThrowsAsync<ServicingDeliveryException>(()=>worker.ExecuteProviderAsync(claim));
            Assert.Equal(scenario=="transient-once"?JobFailure.ProviderUnavailable:JobFailure.ProviderTimeout,failure.Failure);
            Assert.True(await leases.FailAsync(claim,failure.Failure));
            var pending=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==work.Id);f.Clock.Current=pending.NextAttemptAt;
            claim=(await leases.ClaimWorkAsync(ServicingTermsService.WorkKind,work.Id))!;Assert.Equal(2,claim.Attempt);
        }
        var delivered=await worker.ExecuteProviderAsync(claim);
        Assert.Equal(delivered,await worker.ExecuteProviderAsync(claim));
        Assert.True(await worker.ApplyAsync(claim,delivered));Assert.False(await worker.ApplyAsync(claim,delivered));
        delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.Id==sent.ResourceId);
        Assert.Equal(scenario is "withdraw-signature" or "sender-revoked" or "recipient-ended" or "amended"?"superseded":scenario=="reject"?"failed":"delivered",delivery.State);
        Assert.NotNull(delivery.CompletedAt);Assert.Equal(delivered.OperationId,delivery.ProviderOperationId);
        Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ServicingTermsService.WorkKind));
        return "\""+Convert.ToBase64String((await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==cycle.DraftId)).RowVersion)+"\"";
    }
}
