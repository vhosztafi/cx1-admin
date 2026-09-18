using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingCapacityHttpCommands(BackOfficeDbContext db,DecisionFixture f,HttpClient client,string csrf,
        ServicingCycle cycle,Guid caseId,Guid lease,string initialEtag)
    {
        var etag=initialEtag;var root=$"/api/v1/drafts/{cycle.DraftId:D}";var path=root+$"/capacity/{caseId:D}";
        var serial=0;
        async Task Capture(string schema,JsonElement data)
        {
            if(Environment.GetEnvironmentVariable("COVER_CONTRACT_RESPONSES_DIRECTORY") is {Length:>0} output)
            {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}-{++serial}.json"),JsonSerializer.Serialize(new{schema,data}));}
        }
        static string Etag(byte[] value)=>"\""+Convert.ToBase64String(value)+"\"";
        async Task<string> CaseEtag()=>Etag(await db.Set<ServicingCapacityCase>().Where(x=>x.Id==caseId).Select(x=>x.RowVersion).SingleAsync());
        async Task<JsonElement> Send(string route,object body,HttpStatusCode expected,string? version=null,string? key=null)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(body)};
            request.Headers.TryAddWithoutValidation("If-Match",version??etag);request.Headers.Add("X-Edit-Lease",lease.ToString());
            request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());request.Headers.Add("X-CSRF-Token",csrf);
            using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode==expected,$"{route}: {response.StatusCode} {text}");Assert.True(response.Headers.CacheControl!.NoStore);
            var result=JsonSerializer.Deserialize<JsonElement>(text);
            if(response.IsSuccessStatusCode) {etag=response.Headers.ETag!.Tag;Assert.Equal(etag,result.GetProperty("draftEtag").GetString());
                await Capture(expected==HttpStatusCode.Accepted?"ServicingCapacityQueuedReceipt":"ServicingCapacityReceipt",result);}
            return result;
        }
        async Task<JsonElement> Read(string route)
        {
            using var response=await client.GetAsync(route);var text=await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode==HttpStatusCode.OK,text);Assert.True(response.Headers.CacheControl!.NoStore);Assert.Null(response.Headers.ETag);
            var data=JsonSerializer.Deserialize<JsonElement>(text);var suffix=route.Split('?')[0];
            var schema=suffix.EndsWith("/resolutions")?"ServicingCapacityResolutionsPage":suffix.EndsWith("/submissions")?"ServicingCapacitySubmissionsPage":
                suffix.EndsWith("/messages")?"ServicingCapacityMessagesPage":suffix.EndsWith("/responses")?"ServicingCapacityResponsesPage":suffix.EndsWith("/capacity")?"ServicingCapacityPage":"ServicingCapacityDetail";
            await Capture(schema,data);return data;
        }
        foreach(var operation in new[]{"submissions","query-replies","chases","assignment","actions","responses"})
        {
            using var denied=new HttpRequestMessage(HttpMethod.Post,path+"/"+operation){Content=JsonContent.Create(new{})};
            denied.Headers.TryAddWithoutValidation("If-Match",etag);denied.Headers.Add("X-Edit-Lease",lease.ToString());denied.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
            using var response=await client.SendAsync(denied);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            await Send(path+"/"+operation,new{waiveAuthority=true},HttpStatusCode.BadRequest);
        }
        var setting=await db.Set<SettingVersion>().Where(x=>x.Scope=="capacity-escalation/query-proof").OrderByDescending(x=>x.Version).FirstAsync();
        var submissionBody=new{cycleId=cycle.Id,caseEtag=await CaseEtag(),body="Fictional request submitted over the servicing API",
            reason="Request exact fictional tools capacity",evidenceAssociationIds=Array.Empty<Guid>(),scenarioVersionId=setting.Id};
        await Send(path+"/submissions",submissionBody,HttpStatusCode.PreconditionFailed,"\"AAAAAAAAAAA=\"");
        Assert.Empty(await db.Set<ServicingCapacitySubmission>().Where(x=>x.CaseId==caseId).ToArrayAsync());
        var original=etag;var submitKey=Guid.NewGuid().ToString();var sent=await Send(path+"/submissions",submissionBody,HttpStatusCode.Accepted,key:submitKey);
        var retry=await Send(path+"/submissions",submissionBody,HttpStatusCode.Accepted,original,submitKey);Assert.Equal(sent.GetRawText(),retry.GetRawText());
        var submissionId=sent.GetProperty("submissionId").GetGuid();var jobId=sent.GetProperty("jobId").GetGuid();
        await Send(path+"/chases",new{cycleId=cycle.Id,caseEtag=await CaseEtag(),submissionId,body="Please answer the fictional retained submission",reason="Chase fictional provider response"},HttpStatusCode.Created);
        var senior=await db.Set<StaffUser>().Where(x=>x.Email=="senior-underwriter@cover.example").Select(x=>x.Id).SingleAsync();
        await Send(path+"/assignment",new{cycleId=cycle.Id,caseEtag=await CaseEtag(),assignedUserId=senior,reason="Assign fictional case to eligible senior"},HttpStatusCode.OK);
        var job=Assert.IsType<JobLease>(await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync(ServicingCapacityService.WorkKind,jobId));
        var worker=new ServicingCapacityWorker(f.Factory,f.Clock);Assert.True(await worker.ApplyAsync(job,await worker.ExecuteProviderAsync(job)));
        etag=Etag(await db.Set<ServicingDraft>().Where(x=>x.Id==cycle.DraftId).Select(x=>x.RowVersion).SingleAsync());
        var query=await Read(path);Assert.Equal("queried",query.GetProperty("case").GetProperty("state").GetString());
        Assert.Single((await Read(root+"/capacity")).GetProperty("items").EnumerateArray());
        var queryId=query.GetProperty("case").GetProperty("currentResponseId").GetGuid();
        var reply=await Send(path+"/query-replies",new{cycleId=cycle.Id,caseEtag=await CaseEtag(),responseId=queryId,
            body="Fictional clarification for the carrier query",reason="Resubmit fictional clarified request",evidenceAssociationIds=Array.Empty<Guid>(),scenarioVersionId=setting.Id},HttpStatusCode.Accepted);
        submissionId=reply.GetProperty("submissionId").GetGuid();
        var repliedJob=Assert.IsType<JobLease>(await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync(ServicingCapacityService.WorkKind,reply.GetProperty("jobId").GetGuid()));
        Assert.True(await worker.ApplyAsync(repliedJob,await worker.ExecuteProviderAsync(repliedJob)));
        etag=Etag(await db.Set<ServicingDraft>().Where(x=>x.Id==cycle.DraftId).Select(x=>x.RowVersion).SingleAsync());
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
        async Task<ServicingEvidenceAssociation> Proof(string code)
        {
            var purpose=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code==code).Requirement;
            var upload=await evidence.UploadAsync(f.Underwriter,cycle.DraftId,Convert.FromBase64String(etag.Trim('"')),lease,"fictional-"+code+".txt","text/plain",
                Encoding.UTF8.GetBytes("Fictional API scenario proof: "+code),Guid.NewGuid().ToString(),Guid.NewGuid());
            var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Convert.FromBase64String(upload.Etag!.Trim('"')),lease,upload.ResourceId,code,
                purpose.RiskItemId,purpose.InputFingerprint,"Attach exact fictional API scenario proof",Guid.NewGuid().ToString(),Guid.NewGuid());
            var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
            var review=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,association.Id,Convert.FromBase64String(attached.Etag!.Trim('"')),lease,
                association.RowVersion,"accepted",purpose.InputFingerprint,"Review exact fictional API scenario proof",Guid.NewGuid().ToString(),Guid.NewGuid());
            etag=review.Etag!;return await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==association.Id);
        }
        var responseProof=await Proof("capacity-response");
        using var input=JsonDocument.Parse(cycle.InputJson);var dates=input.RootElement.GetProperty("slices").EnumerateArray().Select(x=>x.GetProperty("effectiveAt").GetDateTimeOffset()).ToArray();
        var definition=new{outcome="approve-with-conditions",validFrom=f.Clock.GetUtcNow(),validTo=f.Clock.GetUtcNow().AddYears(1),
            authorisedLimits=new[]{new{dimension="tools-limit",maximumAmount="15000.00"}},conditions=new[]{new{definition=new{code="provide-trading-history"},effectiveDates=dates}}};
        var responseBody=new{cycleId=cycle.Id,caseEtag=await CaseEtag(),submissionId,evidenceAssociationId=responseProof.Id,definition,
            body="Fictional conditional tools permission",providerUnderwriter="Fictional carrier reviewer",providerReference="API-TOOLS-001",receivedAt=f.Clock.GetUtcNow(),reason="Record exact fictional carrier permission"};
        var malformed=JsonSerializer.SerializeToNode(responseBody)!;malformed["definition"]=42;
        await Send(path+"/responses",malformed,HttpStatusCode.UnprocessableEntity);
        var recorded=await Send(path+"/responses",responseBody,HttpStatusCode.Created);var responseId=recorded.GetProperty("id").GetGuid();
        var conditional=await Read(path);Assert.Equal("conditional",conditional.GetProperty("case").GetProperty("state").GetString());Assert.False(conditional.GetProperty("ready").GetBoolean());
        var condition=Assert.Single(conditional.GetProperty("conditions").EnumerateArray());var conditionId=condition.GetProperty("id").GetGuid();
        var resolutionPath=root+$"/capacity-conditions/{conditionId:D}/resolutions";
        await Send(resolutionPath,new{cycleId=cycle.Id,conditionEtag=condition.GetProperty("etag").GetString(),evidenceAssociationId=Guid.NewGuid(),outcome="satisfied",reason="Reject unknown fictional condition evidence"},HttpStatusCode.NotFound);
        var trading=await Proof("trading-history");Assert.False((await Read(path)).GetProperty("ready").GetBoolean());
        await Send(resolutionPath,new{cycleId=cycle.Id,conditionEtag=condition.GetProperty("etag").GetString(),evidenceAssociationId=trading.Id,outcome="satisfied",reason="Resolve exact fictional carrier condition"},HttpStatusCode.OK);
        Assert.True((await Read(path)).GetProperty("ready").GetBoolean());
        var resolutions=await Read(path+$"/conditions/{conditionId:D}/resolutions");Assert.Single(resolutions.GetProperty("items").EnumerateArray());
        var history=await Read(path+"/messages?pageSize=1");var cursor=history.GetProperty("nextCursor").GetString();Assert.NotNull(cursor);
        var next=await Read(path+"/messages?pageSize=1&cursor="+Uri.EscapeDataString(cursor));
        Assert.NotEqual(history.GetProperty("items")[0].GetProperty("id").GetGuid(),next.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(3,(await Read(path+"/responses")).GetProperty("items").GetArrayLength());
        Assert.Equal(2,(await Read(path+"/submissions")).GetProperty("items").GetArrayLength());
        await Send(path+"/actions",new{cycleId=cycle.Id,caseEtag=await CaseEtag(),action="reopen",reason="Reopen fictional permission for revision"},HttpStatusCode.OK);
        var reopened=await Read(path);Assert.Equal("draft",reopened.GetProperty("case").GetProperty("state").GetString());
        Assert.False(reopened.GetProperty("ready").GetBoolean());Assert.Equal(responseId,reopened.GetProperty("case").GetProperty("currentResponseId").GetGuid());
        using var stale=await client.GetAsync(path+"/messages?pageSize=1&cursor="+Uri.EscapeDataString(cursor));Assert.Equal(HttpStatusCode.BadRequest,stale.StatusCode);
        using var foreign=await client.GetAsync($"/api/v1/drafts/{Guid.NewGuid():D}/capacity/{caseId:D}");Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
    }
}
