using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingConditionStorage(BackOfficeDbContext db,ServicingCycle cycle,Guid referral,
        Guid decision,string definition,DateTimeOffset now,Guid actor)
    {
        using var input=JsonDocument.Parse(cycle.InputJson);
        var dates=JsonSerializer.Serialize(input.RootElement.GetProperty("slices").EnumerateArray().Select(x=>x.GetProperty("effectiveAt").GetDateTimeOffset()));
        async Task Insert(Guid id,Guid parent,Guid revision,int sequence=1,string? value=null,string? effective=null,string code="provide-trading-history")
            =>await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingCondition (Id,DraftId,CycleId,RevisionId,RatingId,ReferralId,DecisionId,Sequence,Code,Kind,DefinitionJson,EffectiveDatesJson,CreatedBy,CreatedAt,UpdatedAt) VALUES ({id},{cycle.DraftId},{cycle.Id},{revision},{cycle.CurrentRatingId},{referral},{parent},{sequence},{code},'documentary',{value??definition},{effective??dates},{actor},{now},{now})");
        foreach(var invalid in new[]{"[\"not-a-date\"]","[\"1900-01-01T00:00:00+00:00\"]",JsonSerializer.Serialize(new[]{input.RootElement.GetProperty("slices")[0].GetProperty("effectiveAt").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(1))}),
            JsonSerializer.Serialize(new[]{input.RootElement.GetProperty("slices")[0].GetProperty("effectiveAt").GetString(),input.RootElement.GetProperty("slices")[0].GetProperty("effectiveAt").GetString()})})
            Assert.Equal(51362,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),decision,cycle.RevisionId,effective:invalid))).Number);
        Assert.Equal(51361,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),decision,cycle.RevisionId,value:"{\"code\":\"provide-trading-history\",\"forged\":true}"))).Number);
        var condition=Guid.NewGuid();await Insert(condition,decision,cycle.RevisionId);
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),Guid.NewGuid(),cycle.RevisionId));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),decision,Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),decision,cycle.RevisionId));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),decision,cycle.RevisionId,2));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),decision,cycle.RevisionId,2,value:"{\"code\":\"forged\"}"));
        var altered="{\"code\":\"provide-trading-history\",\"forged\":true}";
        Assert.Equal(51360,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingCondition SET DefinitionJson={altered} WHERE Id={condition}"))).Number);
        Assert.Equal(51360,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingCondition WHERE Id={condition}"))).Number);
        Assert.Equal(definition,await db.Database.SqlQuery<string>($"SELECT DefinitionJson AS Value FROM ServicingCondition WHERE Id={condition}").SingleAsync());
    }
}
