using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingReferralStorage(BackOfficeDbContext db,DecisionFixture f,ServicingCycle cycle)
    {
        var now=f.Clock.GetUtcNow();var rating=cycle.CurrentRatingId!.Value;
        var baseSequence=await db.Set<ServicingReferral>().Where(x=>x.CycleId==cycle.Id).Select(x=>(int?)x.Sequence).MaxAsync()??0;
        using var input=JsonDocument.Parse(cycle.InputJson);var slice=input.RootElement.GetProperty("slices")[0];
        var target=slice.GetProperty("input").GetProperty("drivers")[0].GetProperty("id").GetGuid();
        var required=JsonSerializer.Serialize(new {triggers=new[]{new {effectiveAt=slice.GetProperty("effectiveAt").GetDateTimeOffset(),source="authority",requirement=new {ruleCode="driver-age",dimension="driver-age",targetId=target}}}});
        var id=Guid.NewGuid();var other=Guid.NewGuid();
        async Task Insert(Guid referral,int sequence,Guid price,Guid revision,string code="driver-age",Guid? item=null,string? definition=null)
        {
            var actual=item??target;
            sequence+=baseSequence;
            var json=definition??JsonSerializer.Serialize(new {triggers=new[]{new {effectiveAt=slice.GetProperty("effectiveAt").GetDateTimeOffset(),source="authority",requirement=new {ruleCode=code,dimension=code,targetId=actual}}}});
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingReferral (Id,DraftId,CycleId,RevisionId,RatingId,Sequence,RuleCode,Dimension,RiskItemId,TargetKey,RequiredAuthorityJson,Reason,State,CreatedBy,CreatedAt,UpdatedAt) VALUES ({referral},{cycle.DraftId},{cycle.Id},{revision},{price},{sequence},{code},{code},{actual},{actual},{json},'Fictional servicing referral','open',{f.Servicing.UserId},{now},{now})");
        }
        await Insert(id,1,rating,cycle.RevisionId);
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,Guid.NewGuid(),cycle.RevisionId));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,rating,Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,rating,cycle.RevisionId));
        Assert.Equal(51341,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,rating,cycle.RevisionId,"licence-years",definition:required))).Number);
        Assert.Equal(51342,(await Assert.ThrowsAsync<SqlException>(()=>Insert(Guid.NewGuid(),2,rating,cycle.RevisionId,item:Guid.NewGuid()))).Number);
        await Insert(other,2,rating,cycle.RevisionId,"licence-years");
        Assert.Equal(51346,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingReferral WHERE Id={other}"))).Number);
        var secondTarget=Guid.NewGuid();
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET RiskItemId={secondTarget},TargetKey={secondTarget} WHERE Id={id}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='approved' WHERE Id={id}"));
        var grant=await (from g in db.Set<UserAuthorityGrant>().AsNoTracking() join a in db.Set<AuthorityVersion>() on g.AuthorityVersionId equals a.Id
            where g.UserId==f.Underwriter.UserId && g.RevokedAt==null && a.ProductVersionId==cycle.ProductVersionId && a.BinderVersionId==cycle.BinderVersionId select g).FirstAsync();
        async Task Decide(Guid decision,int sequence,string outcome="approve",string conditions="[]",Guid? grantId=null)=>await db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingReferralDecision (Id,ReferralId,DraftId,CycleId,RevisionId,RatingId,Sequence,Outcome,Reason,ActorId,AuthorityVersionId,GrantId,DecidedAt,ConditionsJson,CreatedBy,CreatedAt) VALUES ({decision},{id},{cycle.DraftId},{cycle.Id},{cycle.RevisionId},{rating},{sequence},{outcome},'Fictional servicing decision',{f.Underwriter.UserId},{grant.AuthorityVersionId},{grantId??grant.Id},{now},{conditions},{f.Underwriter.UserId},{now})");
        var approved=Guid.NewGuid();await Decide(approved,1);
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='approved',LatestDecisionId={approved} WHERE Id={other}"))).Number);
        var foreignGrant=await db.Set<UserAuthorityGrant>().Where(x=>x.UserId!=f.Underwriter.UserId).Select(x=>x.Id).FirstAsync();
        Assert.Equal(547,(await Assert.ThrowsAsync<SqlException>(()=>Decide(Guid.NewGuid(),2,grantId:foreignGrant))).Number);
        await Assert.ThrowsAsync<SqlException>(()=>Decide(Guid.NewGuid(),3));
        await Assert.ThrowsAsync<SqlException>(()=>Decide(Guid.NewGuid(),2,grantId:Guid.NewGuid()));
        await Assert.ThrowsAsync<SqlException>(()=>Decide(Guid.NewGuid(),2,outcome:"approve-with-conditions"));
        await Assert.ThrowsAsync<SqlException>(()=>Decide(Guid.NewGuid(),2,outcome:"query"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='approved',LatestDecisionId={approved} WHERE Id={id}");
        var declined=Guid.NewGuid();await Decide(declined,2,"decline");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='declined',LatestDecisionId={declined} WHERE Id={id}");
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='approved',LatestDecisionId={approved} WHERE Id={id}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferralDecision SET Outcome='approve' WHERE Id={declined}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingReferralDecision WHERE Id={approved}"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE ServicingReferral WHERE Id={id}"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='superseded' WHERE Id={id}");
        await Assert.ThrowsAsync<SqlException>(()=>Decide(Guid.NewGuid(),3,"reopen"));
        await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ServicingReferral SET State='declined' WHERE Id={id}"));
        Assert.Equal(2,await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM ServicingReferralDecision").SingleAsync());
    }
}
