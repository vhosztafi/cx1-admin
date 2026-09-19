using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityWorker(BackOfficeDbContext db,DecisionFixture f,ServicingCapacityCase capacity,
        Guid editLease,string etag,string scenario)
    {
        var service=new ServicingCapacityService(f.Factory,f.Clock);
        var settingScope=scenario=="capacity-worker-conditional"?"capacity-escalation/conditional-security":"capacity-escalation/query-proof";
        var setting=await db.Set<SettingVersion>().Where(x=>x.Scope==settingScope).OrderByDescending(x=>x.Version).FirstAsync();
        var sent=await service.SubmitAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,capacity.Id,Convert.FromBase64String(etag.Trim('"')),
            capacity.RowVersion,editLease,"Fictional servicing carrier request","Request fictional capacity response",[],setting.Id,Guid.NewGuid().ToString(),Guid.NewGuid());
        var submission=await db.Set<ServicingCapacitySubmission>().AsNoTracking().SingleAsync(x=>x.CaseId==capacity.Id);
        var leases=new SqlJobLeases(f.Factory,f.Clock);
        var lease=await leases.ClaimWorkAsync(ServicingCapacityService.WorkKind,submission.WorkId);Assert.NotNull(lease);
        var worker=new ServicingCapacityWorker(f.Factory,f.Clock);
        var result=await worker.ExecuteProviderAsync(lease);
        Assert.Equal(result,await new ServicingCapacityWorker(f.Factory,f.Clock).ExecuteProviderAsync(lease));
        f.Clock.Current=f.Clock.Current.AddMinutes(1);
        Assert.False(await worker.ApplyAsync(lease,result));
        lease=await leases.ClaimWorkAsync(ServicingCapacityService.WorkKind,submission.WorkId);Assert.NotNull(lease);
        Assert.Equal(result,await worker.ExecuteProviderAsync(lease));
        var conflict=await Assert.ThrowsAsync<BackOffice.Infrastructure.Underwriting.CapacityProviderException>(()=>worker.ApplyAsync(lease,result with {Body="Forged initial provider result"}));
        Assert.Equal(JobFailure.ProviderConflict,conflict.Failure);
        Assert.False(await db.Set<ServicingCapacityResponseRecord>().AnyAsync(x=>x.CaseId==capacity.Id));
        if(scenario=="capacity-worker-withdrawn")
        {
            var current=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==capacity.Id);
            await service.ActionAsync(f.Underwriter,capacity.DraftId,capacity.CycleId,capacity.Id,Convert.FromBase64String(sent.Etag!.Trim('"')),
                current.RowVersion,editLease,"withdraw","Withdraw before fictional provider reply",Guid.NewGuid().ToString(),Guid.NewGuid());
        }
        if(scenario=="capacity-worker-revoked")
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={DateTimeOffset.UtcNow},RevokedBy={f.Underwriter.UserId},RevocationReason='Revoke before fictional provider reply' WHERE UserId={f.Underwriter.UserId} AND RevokedAt IS NULL");
        if(scenario=="capacity-worker-cancelled")
        {
            var basis=await db.Set<ServicingCycle>().Where(x=>x.Id==capacity.CycleId).Select(x=>x.BaseVersionId).SingleAsync();
            await VerifyCancellationOfAdjustedLedger(db,f,basis,false);
        }
        Assert.True(await worker.ApplyAsync(lease,result));
        Assert.False(await worker.ApplyAsync(lease,result));
        Assert.False(await worker.ApplyAsync(lease,result with {Body="Conflicting fictional response body"}));
        var response=await db.Set<ServicingCapacityResponseRecord>().AsNoTracking().SingleAsync(x=>x.CaseId==capacity.Id);
        Assert.Equal(result.OperationId,response.ProviderOperationId);Assert.Equal(result.Body,response.Body);
        var applicable=scenario is "capacity-worker" or "capacity-worker-conditional";
        Assert.Equal(applicable?"applied":"superseded",response.ApplicationState);
        var final=await db.Set<ServicingCapacityCase>().AsNoTracking().SingleAsync(x=>x.Id==capacity.Id);
        Assert.Equal(applicable?response.Id:(Guid?)null,final.CurrentResponseId);
        Assert.Equal(scenario=="capacity-worker-conditional"?"conditional":scenario=="capacity-worker"?"queried":scenario=="capacity-worker-withdrawn"?"draft":"superseded",final.State);
        if(scenario=="capacity-worker-conditional")
        {
            using var definition=System.Text.Json.JsonDocument.Parse(response.DefinitionJson);
            Assert.True(definition.RootElement.GetProperty("validFrom").GetDateTimeOffset()<=f.Clock.GetUtcNow());
            var conditions=await db.Set<ServicingCapacityCondition>().AsNoTracking().Where(x=>x.ResponseId==response.Id).ToArrayAsync();
            Assert.NotEmpty(conditions);Assert.All(conditions,x=>Assert.Equal("overnight-security",x.Code));
            foreach(var condition in conditions) Assert.False(await service.ConditionSatisfiedAsync(f.Underwriter,capacity.DraftId,condition.Id));
        }
        Assert.Equal(1,await db.Set<AdapterQuarantine>().CountAsync(x=>x.InboxId==response.InboxId));
        Assert.Equal("succeeded",await db.Set<OutboxWork>().Where(x=>x.Id==submission.WorkId).Select(x=>x.State).SingleAsync());
        await VerifyServicingCapacityReads(db,f,capacity,scenario);
        if(scenario=="capacity-worker")
        {
            await VerifyServicingCapacityAssignment(db,f,capacity,editLease);
            await VerifyServicingCapacityQueryReply(db,f,capacity,response.Id,editLease,setting.Id);
        }
    }
}

