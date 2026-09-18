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
    private static async Task VerifyServicingCapacityResponseStorage(BackOfficeDbContext db,DecisionFixture f,
        ServicingCapacityCase capacity,Guid submissionId,ServicingEvidenceAssociation proof,Guid lease)
    {
        Assert.Equal(0,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityResponse").SingleAsync());
        var submission=await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleAsync(x=>x.Id==submissionId);
        var now=f.Clock.GetUtcNow();var responseId=Guid.NewGuid();
        var definition=JsonSerializer.Serialize(new {format="servicing-capacity-response-1",draftId=capacity.DraftId,revisionId=capacity.RevisionId,
            cycleId=capacity.CycleId,ratingId=capacity.RatingId,caseId=capacity.Id,referralId=capacity.ReferralId,providerId=capacity.ProviderId,
            submissionId,submissionHash=Convert.ToHexStringLower(submission.ContextHash),outcome="query",validFrom=(DateTimeOffset?)null,
            validTo=(DateTimeOffset?)null,authorisedLimits=Array.Empty<object>(),conditions=Array.Empty<object>()});
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(definition));
        async Task Insert(Guid? draft=null,Guid? provider=null,Guid? review=null,byte[]? contentHash=null,string outcome="query",int sequence=1)=>
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCapacityResponse (Id,SubmissionId,CaseId,DraftId,CycleId,RevisionId,RatingId,ProviderId,Sequence,Provenance,Outcome,Body,DefinitionJson,ContentHash,ProviderUnderwriter,ProviderReference,ReceivedAt,RecordedAt,RecordedBy,EvidenceAssociationId,EvidenceReviewId,ApplicationState,CreatedBy,CreatedAt) VALUES ({responseId},{submissionId},{capacity.Id},{draft ?? capacity.DraftId},{capacity.CycleId},{capacity.RevisionId},{capacity.RatingId},{provider ?? capacity.ProviderId},{sequence},'supplied-response',{outcome},'Fictional request for further information',{definition},{contentHash ?? hash},'Fictional carrier underwriter','DEMO-QUERY-001',{now},{now},{f.Underwriter.UserId},{proof.Id},{review ?? proof.LatestReviewId},'applied',{f.Underwriter.UserId},{now})");
        await Assert.ThrowsAsync<SqlException>(()=>Insert(draft:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(provider:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(review:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(contentHash:new byte[32]));
        Assert.Equal(51470,(await Assert.ThrowsAsync<SqlException>(()=>Insert(outcome:"approve"))).Number);
        Assert.Equal(51470,(await Assert.ThrowsAsync<SqlException>(()=>Insert(sequence:3))).Number);
        responseId=await VerifyServicingSuppliedResponseCommand(db,f,capacity,submissionId,proof,lease);
        Assert.Equal(51471,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityResponse SET Body='Altered fictional carrier message' WHERE Id={responseId}"))).Number);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentResponseId={responseId},State='queried' WHERE Id={capacity.Id}");
        Assert.Equal(51422,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='approved' WHERE Id={capacity.Id}"))).Number);
        Assert.Equal(51422,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentResponseId=NULL WHERE Id={capacity.Id}"))).Number);
        await VerifyServicingCapacityConditionStorage(db,f,capacity,submission,proof,now,lease);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET State='draft' WHERE Id={capacity.Id}");
        Assert.DoesNotContain((await new ServicingEvidenceService(f.Factory,f.Clock).RequirementsAsync(f.Underwriter,capacity.DraftId)).Requirements,x=>x.Requirement.Code=="trading-history");
        responseId=Guid.NewGuid();
        Assert.Equal(51470,(await Assert.ThrowsAsync<SqlException>(()=>Insert(sequence:3))).Number);
        Assert.Equal("draft",await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.State).SingleAsync());
        Assert.Equal(2,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityResponse").SingleAsync());
        // A durable provider result may arrive after withdrawal. Keep exact
        // provider history, but never permit it to become current authority.
        var operation=new DemoProviderOperation{Kind="servicing-capacity",OperationKey=$"servicing-capacity/{submission.Id:N}",
            RequestHash=submission.ContextHash,ScenarioVersionId=submission.ScenarioVersionId,State="succeeded",CreatedAt=now,CompletedAt=now};
        var eventId="servicing-capacity-"+operation.Id.ToString("N"); const string providerBody="Fictional late provider query";
        operation.Result=JsonSerializer.Serialize(new {operationId=operation.Id,eventId,outcome="query",body=providerBody,receivedAt=now,definitionJson=definition});
        db.Add(operation);await db.SaveChangesAsync();
        var inbox=new AdapterInbox{Provider="demo-servicing-capacity",EventId=eventId,WorkId=submission.WorkId,
            ContentHash=SHA256.HashData(Encoding.UTF8.GetBytes(operation.Result)),State="applied",AppliedAt=now,CreatedAt=now,UpdatedAt=now};
        db.Add(inbox);await db.SaveChangesAsync();
        async Task Demo(string body)=>await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCapacityResponse (Id,SubmissionId,CaseId,DraftId,CycleId,RevisionId,RatingId,ProviderId,Sequence,Provenance,Outcome,Body,DefinitionJson,ContentHash,ProviderUnderwriter,ProviderReference,ProviderEventId,ProviderOperationId,InboxId,ReceivedAt,RecordedAt,RecordedBy,ApplicationState,CreatedBy,CreatedAt) VALUES ({responseId},{submissionId},{capacity.Id},{capacity.DraftId},{capacity.CycleId},{capacity.RevisionId},{capacity.RatingId},{capacity.ProviderId},3,'demo-provider','query',{body},{definition},{hash},'Fictional demo provider',{eventId},{eventId},{operation.Id},{inbox.Id},{now},{now},{f.Underwriter.UserId},'superseded',{f.Underwriter.UserId},{now})");
        Assert.Equal(51470,(await Assert.ThrowsAsync<SqlException>(()=>Demo("Altered fictional provider result"))).Number);
        await Demo(providerBody);
        Assert.Equal(51422,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCapacityCase SET CurrentResponseId={responseId},State='queried' WHERE Id={capacity.Id}"))).Number);
        Assert.Equal("draft",await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.State).SingleAsync());
        Assert.Equal(3,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingCapacityResponse").SingleAsync());
    }
}
