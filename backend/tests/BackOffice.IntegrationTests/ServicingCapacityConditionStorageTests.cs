using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityConditionStorage(BackOfficeDbContext db,DecisionFixture f,
        ServicingCapacityCase capacity,ServicingCapacitySubmission submission,ServicingEvidenceAssociation proof,DateTimeOffset now,Guid lease)
    {
        Assert.Equal(0,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityCondition").SingleAsync());
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        Assert.DoesNotContain((await evidence.RequirementsAsync(f.Underwriter,capacity.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history");
        var cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==capacity.CycleId);
        using var input=JsonDocument.Parse(cycle.InputJson);
        var dates=input.RootElement.GetProperty("slices").EnumerateArray().Select(x=>x.GetProperty("effectiveAt").GetDateTimeOffset()).ToArray();
        var conditionJson="{\"code\":\"provide-trading-history\"}";var datesJson=JsonSerializer.Serialize(dates);
        var definition=JsonSerializer.Serialize(new {format="servicing-capacity-response-1",draftId=capacity.DraftId,revisionId=capacity.RevisionId,
            cycleId=capacity.CycleId,ratingId=capacity.RatingId,caseId=capacity.Id,referralId=capacity.ReferralId,providerId=capacity.ProviderId,
            submissionId=submission.Id,submissionHash=Convert.ToHexStringLower(submission.ContextHash),outcome="approve-with-conditions",validFrom=now,
            validTo=now.AddYears(1),authorisedLimits=new[]{new {dimension="tools-limit",maximumAmount="15000.00"}},
            conditions=new[]{new {definition=JsonSerializer.Deserialize<JsonElement>(conditionJson),effectiveDates=dates}}});
        var response=new ServicingCapacityResponseRecord{SubmissionId=submission.Id,CaseId=capacity.Id,DraftId=capacity.DraftId,CycleId=capacity.CycleId,
            RevisionId=capacity.RevisionId,RatingId=capacity.RatingId,ProviderId=capacity.ProviderId,Sequence=2,Outcome="approve-with-conditions",
            Body="Fictional conditional tools capacity permission",DefinitionJson=definition,ContentHash=SHA256.HashData(Encoding.UTF8.GetBytes(definition)),
            ProviderUnderwriter="Fictional carrier underwriter",ProviderReference="DEMO-CONDITIONAL-001",ReceivedAt=now,RecordedAt=now,
            RecordedBy=f.Underwriter.UserId,EvidenceAssociationId=proof.Id,EvidenceReviewId=proof.LatestReviewId,CreatedAt=now,CreatedBy=f.Underwriter.UserId};
        db.Add(response);await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentResponseId={response.Id},State='conditional' WHERE Id={capacity.Id}"));
        var conditionId=Guid.NewGuid();
        async Task Insert(Guid? draft=null,int sequence=1,string? clause=null,string? effectiveDates=null)=>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCapacityCondition (Id,ResponseId,SubmissionId,CaseId,DraftId,CycleId,RevisionId,RatingId,Sequence,Code,Kind,DefinitionJson,EffectiveDatesJson,CreatedAt,CreatedBy,UpdatedAt) VALUES ({conditionId},{response.Id},{submission.Id},{capacity.Id},{draft ?? capacity.DraftId},{capacity.CycleId},{capacity.RevisionId},{capacity.RatingId},{sequence},'provide-trading-history','documentary',{clause ?? conditionJson},{effectiveDates ?? datesJson},{now},{f.Underwriter.UserId},{now})");
        await Assert.ThrowsAsync<SqlException>(()=>Insert(draft:Guid.NewGuid()));
        Assert.Equal(51480,(await Assert.ThrowsAsync<SqlException>(()=>Insert(sequence:2))).Number);
        Assert.Equal(51480,(await Assert.ThrowsAsync<SqlException>(()=>Insert(clause:"{\"code\":\"provide-trading-history\",\"waiveProof\":true}"))).Number);
        Assert.Equal(51480,(await Assert.ThrowsAsync<SqlException>(()=>Insert(effectiveDates:JsonSerializer.Serialize(new[]{now.AddYears(10)})))).Number);
        await Insert();
        Assert.Equal(51481,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCondition SET UpdatedAt={now.AddMinutes(1)} WHERE Id={conditionId}"))).Number);
        Assert.Equal(51481,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingCapacityCondition WHERE Id={conditionId}"))).Number);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentResponseId={response.Id},State='conditional' WHERE Id={capacity.Id}");
        Assert.Equal("conditional",await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.State).SingleAsync());
        Assert.Equal(1,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityCondition").SingleAsync());
        var requirement=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,capacity.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history");
        Assert.Equal(dates,requirement.Requirement.EffectiveDates);Assert.False(requirement.Satisfied);
        await VerifyServicingCarrierResolutionStorage(db,cycle,response.Id,submission.Id,capacity.Id,conditionId,now,f.Underwriter.UserId);
        await VerifyServicingCarrierResolutionCommand(db,f,capacity,conditionId,lease);
    }
}
