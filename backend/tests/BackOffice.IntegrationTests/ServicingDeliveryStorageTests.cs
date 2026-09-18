using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingDeliveryStorage(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle,Guid oldTerms,Guid termsId,DateTimeOffset now)
    {
        Assert.Equal(0,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingTermsDelivery").SingleAsync());
        var scenario=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Scope=="servicing-delivery");
        var terms=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==termsId);
        var id=Guid.NewGuid();var recipients=JsonSerializer.Serialize(new[]{new{id=Guid.NewGuid(),name="Fictional recipient",email="fictional@example.test"}});
        var payload=$$"""{"format":"servicing-delivery-1","deliveryId":"{{id:D}}","draftId":"{{cycle.DraftId:D}}","cycleId":"{{cycle.Id:D}}","termsVersionId":"{{termsId:D}}","termsHash":"{{terms.TermsHash}}","recipients":{{recipients}},"document":{{terms.TermsJson}}} """.TrimEnd();
        Assert.Equal(terms.TermsJson,await db.Database.SqlQuery<string>($"SELECT JSON_QUERY({payload},'$.document') AS Value").SingleAsync());
        var hash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var work=new OutboxWork{Kind="servicing-delivery",SubjectRecordId=id,OperationKey=$"servicing-delivery/{id:N}",ScenarioVersionId=scenario.Id,
            Payload="{}",NextAttemptAt=now,CreatedAt=now,UpdatedAt=now,CreatedBy=f.Underwriter.UserId};
        db.Add(work);await db.SaveChangesAsync();
        Task<int> Insert(Guid owner,Guid contract,string fingerprint,string recipientJson)=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingTermsDelivery (Id,DraftId,CycleId,RevisionId,RatingId,TermsVersionId,WorkId,ScenarioVersionId,RecipientSnapshotJson,PayloadJson,PayloadHash,AssuranceHashAtSend,SentBy,State,CreatedAt,UpdatedAt,CreatedBy) VALUES ({id},{owner},{cycle.Id},{cycle.RevisionId},{cycle.CurrentRatingId},{contract},{work.Id},{scenario.Id},{recipientJson},{payload},{fingerprint},{new string('b',64)},{f.Underwriter.UserId},'queued',{now},{now},{f.Underwriter.UserId})");
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),termsId,hash,recipients));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(cycle.DraftId,termsId,new string('f',64),recipients));
        Assert.Equal(51510,(await Assert.ThrowsAsync<SqlException>(()=>Insert(cycle.DraftId,oldTerms,hash,recipients))).Number);
        Assert.Equal(51510,(await Assert.ThrowsAsync<SqlException>(()=>Insert(cycle.DraftId,termsId,hash,"[]"))).Number);
        await Insert(cycle.DraftId,termsId,hash,recipients);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCycle SET CurrentDeliveryId={id} WHERE Id={cycle.Id}");
        Assert.Equal(51511,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingTermsDelivery SET AssuranceHashAtSend={new string('c',64)} WHERE Id={id}"))).Number);
        // The live cycle FK can reject deletion before the append-only trigger.
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingTermsDelivery WHERE Id={id}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingTermsDelivery SET State='delivered',CompletedAt={now} WHERE Id={id}"));
        Assert.Equal("queued",await db.Database.SqlQueryRaw<string>("SELECT State AS Value FROM ServicingTermsDelivery").SingleAsync());
    }
}
