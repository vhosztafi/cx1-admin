using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingSignedCondition(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,ServicingTermsVersion terms,Guid fence,string etag)
    {
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        var service=new ServicingReferralService(f.Factory,f.Clock);var contracts=new ServicingTermsService(f.Factory,f.Clock);
        using var input=JsonDocument.Parse(cycle.InputJson);var slice=input.RootElement.GetProperty("slices")[0];
        var target=slice.GetProperty("input").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        var row=new ServicingReferral{DraftId=cycle.DraftId,CycleId=cycle.Id,RevisionId=cycle.RevisionId,RatingId=cycle.CurrentRatingId!.Value,
            Sequence=(await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).MaxAsync(x=>(int?)x.Sequence)??0)+1,
            RuleCode="driver-age",Dimension="driver-age",RiskItemId=target,TargetKey=target,Reason="Synthetic signature condition boundary",
            RequiredAuthorityJson=JsonSerializer.Serialize(new{triggers=new[]{new{effectiveAt=slice.GetProperty("effectiveAt").GetDateTimeOffset(),source="authority",requirement=new{ruleCode="driver-age",dimension="driver-age",targetId=target}}}}),
            CreatedBy=f.Underwriter.UserId,CreatedAt=f.Clock.GetUtcNow(),UpdatedAt=f.Clock.GetUtcNow()};db.Add(row);await db.SaveChangesAsync();
        JsonElement Definition(Guid id,string hash)=>JsonSerializer.SerializeToElement(new{code="provide-signed-statement",termsVersionId=id,termsHash=hash});
        var decision=new ReferralDecisionInput(row.Id,row.RowVersion,"approve-with-conditions","Require signature on exact servicing contract",[Definition(terms.Id,terms.TermsHash)]);
        foreach(var wrong in new[]{Definition(Guid.NewGuid(),terms.TermsHash),Definition(terms.Id,new string('0',64))})
            await Assert.ThrowsAsync<QuoteOperationException>(()=>service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(etag),fence,[decision with{Conditions=[wrong]}],Guid.NewGuid().ToString(),Guid.NewGuid()));
        var result=await service.DecideAsync(f.Underwriter,cycle.DraftId,cycle.Id,Version(etag),fence,[decision],Guid.NewGuid().ToString(),Guid.NewGuid());
        var condition=await db.Set<ServicingCondition>().AsNoTracking().SingleAsync(x=>x.ReferralId==row.Id);
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        var view=await contracts.ReadAsync(f.Underwriter,cycle.DraftId);Assert.True(view.CanPrepare);Assert.False(view.CanSend);
        var signature=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.CycleId==cycle.Id && x.RequirementCode=="signed-statement");
        var key=Guid.NewGuid().ToString();
        var resolved=await service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(result.Etag!),fence,condition.RowVersion,signature.Id,"satisfied","Reviewed exact signed servicing contract",key,Guid.NewGuid());
        Assert.True(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));Assert.True((await contracts.ReadAsync(f.Underwriter,cycle.DraftId)).CanSend);
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        await evidence.WithdrawAsync(f.Underwriter,cycle.DraftId,cycle.Id,signature.Id,Version(resolved.Etag!),fence,signature.RowVersion,"Withdraw fictional signature condition proof",Guid.NewGuid().ToString(),Guid.NewGuid());
        Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));Assert.False((await contracts.ReadAsync(f.Underwriter,cycle.DraftId)).CanSend);
        await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ResolveAsync(f.Underwriter,cycle.DraftId,cycle.Id,condition.Id,Version(result.Etag!),fence,condition.RowVersion,signature.Id,"satisfied","Reviewed exact signed servicing contract",key,Guid.NewGuid()));
    }
}
