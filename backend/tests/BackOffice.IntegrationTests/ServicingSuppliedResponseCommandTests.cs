using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<Guid> VerifyServicingSuppliedResponseCommand(BackOfficeDbContext db,DecisionFixture f,
        ServicingCapacityCase capacity,Guid submission,ServicingEvidenceAssociation proof,Guid lease)
    {
        var service=new ServicingCapacityService(f.Factory,f.Clock);
        var version=await db.Set<ServicingDraft>().Where(x=>x.Id==capacity.DraftId).Select(x=>x.RowVersion).SingleAsync();
        var caseVersion=await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.RowVersion).SingleAsync();
        var definition=new ServicingCapacityResponseDefinition("query",null,null,Array.Empty<JsonElement>(),Array.Empty<ServicingCarrierConditionInput>());
        var key=Guid.NewGuid().ToString();var now=f.Clock.GetUtcNow();
        Task<BackOffice.Infrastructure.Platform.CommandOutcome> Send(Guid evidence,byte[]? expected=null,string? retryKey=null,ServicingCapacityResponseDefinition? response=null,BackOffice.Application.ActorContext? actor=null)=>
            service.RecordResponseAsync(actor??f.Underwriter,capacity.DraftId,capacity.CycleId,capacity.Id,submission,expected??version,caseVersion,lease,
                evidence,response??definition,"Fictional request for further information","Fictional carrier underwriter","DEMO-QUERY-001",now,
                "Record fictional exact carrier response",retryKey??Guid.NewGuid().ToString(),Guid.NewGuid());
        Assert.Equal(404,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Send(Guid.NewGuid()))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Send(proof.Id,actor:f.Servicing))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Send(proof.Id,new byte[8]))).Status);
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>Send(proof.Id,response:definition with {Outcome="approve"}))).Status);
        Assert.Equal(0,await db.Set<ServicingCapacityResponseRecord>().CountAsync());
        var result=await Send(proof.Id,retryKey:key);
        Assert.Equal(201,result.Status);
        var replay=await Send(proof.Id,retryKey:key); Assert.True(replay.Replayed); Assert.Equal(result.ResourceId,replay.ResourceId); Assert.Equal(result.Body,replay.Body);
        Assert.Equal(1,await db.Set<ServicingCapacityResponseRecord>().CountAsync());
        Assert.Equal("queried",await db.Set<ServicingCapacityCase>().Where(x=>x.Id==capacity.Id).Select(x=>x.State).SingleAsync());
        var clock=f.Clock.Current;f.Clock.Current=clock.AddMinutes(6);
        try { Assert.Equal("servicing-lease-conflict",(await Assert.ThrowsAsync<QuoteOperationException>(()=>Send(proof.Id,retryKey:key))).Code); }
        finally { f.Clock.Current=clock; }
        return result.ResourceId;
    }
}

