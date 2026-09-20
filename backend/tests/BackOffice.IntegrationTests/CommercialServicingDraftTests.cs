using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialServicingDraftStoresTypedProjectionAndFencesStaleLeaseReplay() => CommercialTermsScenario(async (db, cycle, acceptance, actorId, now) =>
    {
        var f = await CommercialIssueCommand(db,cycle,acceptance,actorId);
        var issued = await f.Service.IssueAsync(f.Actor,f.Quote.Id,f.Quote.RowVersion,f.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var clock = new RatingClock(); var service = new ServicingDraftService(f.Factory,clock);
        static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
        static JsonElement Body(string value) => JsonSerializer.Deserialize<JsonElement>(value);
        static string Key() => Guid.NewGuid().ToString();
        var list = await service.ListAsync(f.Actor,basis.TermId);
        var input = new ServicingDraftCreate("adjustment",basis.Id,JsonSerializer.SerializeToElement(new {localDate="2026-10-01",localTime="00:00",timeZone="Europe/London"}),"Fictional commercial change capture");
        Assert.Equal(422,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.CreateAsync(f.Actor,basis.TermId,Version(list.Etag),input with {BaseVersionId=Guid.NewGuid()},Key(),Guid.NewGuid()))).Status);
        // This adjustment fixture intentionally has no renewal configuration.
        // Supported, configured commercial renewals have their own lifecycle tests.
        var unavailable = await Assert.ThrowsAsync<QuoteOperationException>(()=>service.CreateAsync(f.Actor,basis.TermId,Version(list.Etag),input with {Kind="renewal"},Key(),Guid.NewGuid()));
        Assert.Equal(503,unavailable.Status); Assert.Equal("renewal-configuration-unavailable",unavailable.Code);
        Assert.Empty(await db.Set<ServicingDraft>().ToArrayAsync());
        var created = await service.CreateAsync(f.Actor,basis.TermId,Version(list.Etag),input,Key(),Guid.NewGuid());
        var acquired = await service.LeaseAsync(f.Actor,created.ResourceId,Version(created.Etag!),"acquire",null,null,Key(),Guid.NewGuid());
        var fence = Body(acquired.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var proposal = JsonNode.Parse(Body(acquired.Body).GetProperty("proposal").GetRawText())!.AsObject();
        var location = Body(basis.SnapshotJson).GetProperty("risk").GetProperty("locations")[0].GetProperty("id").GetGuid();
        proposal["changes"] = JsonSerializer.SerializeToNode(new object[] {
            new {changeId=Guid.NewGuid(),riskItemId=location,kind="commercial-property",operation="update",payload=new {stock="11111.11"}},
            new {changeId=Guid.NewGuid(),riskItemId=issued.ResourceId,kind="commercial-business",operation="update",payload=new {description="Fictional changed warehouse activity"}} });
        var originalVersion=Version(acquired.Etag!);var saveKey=Key();var json=proposal.ToJsonString();
        var saved=await service.SaveAsync(f.Actor,created.ResourceId,originalVersion,fence,json,saveKey,Guid.NewGuid());
        var editor=Body((await service.ReadEditorAsync(f.Actor,created.ResourceId)).Body);
        Assert.Equal("commercial-questions-1",editor.GetProperty("captureVersions").GetProperty("questionSetVersion").GetString());
        Assert.Equal("commercial-reference-1",editor.GetProperty("captureVersions").GetProperty("referenceDataVersion").GetString());
        var assessment=editor.GetProperty("assessment");Assert.Equal("11111.11",assessment.GetProperty("proposed").GetProperty("risk").GetProperty("locations")[0].GetProperty("stock").GetString());
        Assert.Single(assessment.GetProperty("slices").EnumerateArray());Assert.NotEmpty(assessment.GetProperty("changes").EnumerateArray());
        Assert.Equal(saved.Body,(await service.SaveAsync(f.Actor,created.ResourceId,originalVersion,fence,json,saveKey,Guid.NewGuid())).Body);
        Assert.Equal(2,await db.Set<ServicingRevision>().CountAsync());
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(f.Actor,created.ResourceId,originalVersion,fence,json,Key(),Guid.NewGuid()))).Status);
        foreach(var mutation in new[]{"foreign","motor","client-transfer","base"})
        {
            var bad=proposal.DeepClone();
            if(mutation=="foreign")bad["changes"]![0]!["riskItemId"]=Guid.NewGuid();
            if(mutation=="motor"){bad["changes"]![0]!["kind"]="driver";bad["changes"]![0]!["payload"]=new JsonObject();}
            if(mutation=="client-transfer")bad["changes"]![1]!["payload"]!["clientId"]=Guid.NewGuid();
            if(mutation=="base")bad["baseVersionId"]=Guid.NewGuid();
            if (mutation is "foreign" or "motor")
                await Assert.ThrowsAsync<QuoteValidationException>(()=>service.SaveAsync(f.Actor,created.ResourceId,Version(saved.Etag!),fence,bad.ToJsonString(),Key(),Guid.NewGuid()));
            else await Assert.ThrowsAsync<QuoteInputException>(()=>service.SaveAsync(f.Actor,created.ResourceId,Version(saved.Etag!),fence,bad.ToJsonString(),Key(),Guid.NewGuid()));
        }
        var revision=Body(saved.Body).GetProperty("revisionId").GetGuid();
        clock.Current=now.AddMinutes(6);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(f.Actor,created.ResourceId,Version(saved.Etag!),fence,json,Key(),Guid.NewGuid()))).Status);
        var renewed=await service.LeaseAsync(f.Actor,created.ResourceId,Version(saved.Etag!),"acquire",null,null,Key(),Guid.NewGuid());
        Assert.NotEqual(fence,Body(renewed.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid());
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(f.Actor,created.ResourceId,Version(renewed.Etag!),fence,json,Key(),Guid.NewGuid()))).Status);
        var retained=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();Assert.Equal(basis.SnapshotJson,retained.SnapshotJson);Assert.Equal(basis.ContentHash,retained.ContentHash);
        Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureIssueDecision>().CountAsync());Assert.Equal(1,await db.Set<IssueFinancialObligation>().CountAsync());
        Assert.Equal(2,await db.Set<ServicingRevision>().CountAsync());Assert.False(await db.Set<ServicingCycle>().AnyAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [User] SET State=N'suspended' WHERE Id={f.Actor.UserId}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(f.Actor,created.ResourceId,originalVersion,fence,json,saveKey,Guid.NewGuid()))).Status);
    },stopAfterAccepted:true);
}
