using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingProofCommandsHttp(BackOfficeDbContext db,DecisionFixture f,string password,ServicingCycle cycle,Guid referralId)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{
            services.AddScoped(p=>new ServicingEvidenceService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
            services.AddScoped(p=>new ServicingReferralService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock));
        }));
        using var client=host.CreateClient();
        var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var email=await db.Set<StaffUser>().Where(x=>x.Id==f.Underwriter.UserId).Select(x=>x.Email).SingleAsync();
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new {email,password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var response=await client.SendAsync(login);response.EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var service=new ServicingEvidenceService(f.Factory,f.Clock);var referrals=new ServicingReferralService(f.Factory,f.Clock);
        var root=$"/api/v1/drafts/{cycle.DraftId:D}";var etag=await service.HistoryVersionAsync(f.Underwriter,cycle.DraftId);
        var fence=(await db.Set<ServicingLease>().AsNoTracking().SingleAsync(x=>x.DraftId==cycle.DraftId)).Token;
        var condition=await (from c in db.Set<ServicingCondition>().AsNoTracking() join r in db.Set<ServicingReferral>() on c.DecisionId equals r.LatestDecisionId where r.Id==referralId select c).SingleAsync();
        var required=Assert.Single((await service.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="warranty-acknowledgement").Requirement;
        async Task<HttpResponseMessage> Post(string suffix,object body,string? key=null,string? version=null,bool antiForgery=true,string? lease=null)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,root+suffix){Content=body is string raw?new StringContent(raw,Encoding.UTF8,"application/json"):JsonContent.Create(body)};
            if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);
            request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
            if(version!="missing")request.Headers.TryAddWithoutValidation("If-Match",version??etag);
            if(lease!="missing")request.Headers.Add("X-Edit-Lease",lease??fence.ToString());
            return await client.SendAsync(request);
        }
        async Task<HttpResponseMessage> Upload(string key,string version,bool extra=false)
        {
            using var form=new MultipartFormDataContent();var file=new ByteArrayContent(Encoding.UTF8.GetBytes("Fictional HTTP warranty acknowledgement"));
            file.Headers.ContentType=new("text/plain");form.Add(file,"file","http-proof.txt");form.Add(new StringContent("http-proof.txt"),"fileName");form.Add(new StringContent("text/plain"),"contentType");
            if(extra)form.Add(new StringContent("invalid"),"unknown");
            using var request=new HttpRequestMessage(HttpMethod.Post,root+"/evidence/uploads"){Content=form};
            request.Headers.Add("X-CSRF-Token",csrf);request.Headers.Add("Idempotency-Key",key);request.Headers.TryAddWithoutValidation("If-Match",version);request.Headers.Add("X-Edit-Lease",fence.ToString());
            return await client.SendAsync(request);
        }
        var receiptSequence=0;
        async Task<JsonElement> Success(HttpResponseMessage response,HttpStatusCode status)
        {
            var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==status,text);Assert.True(response.Headers.CacheControl!.NoStore);
            etag=response.Headers.ETag!.Tag;var data=JsonSerializer.Deserialize<JsonElement>(text);
            if(Environment.GetEnvironmentVariable("COVER_COMMAND_RESPONSES_DIRECTORY") is {Length:>0} output)
            {
                Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}-{++receiptSequence}.json"),JsonSerializer.Serialize(new {schema="ServicingProofReceipt",data}));
            }
            return data;
        }
        foreach(var path in new[]{"/evidence/uploads","/evidence",$"/evidence/{Guid.NewGuid():D}/reviews",$"/evidence/{Guid.NewGuid():D}/withdraw","/referrals/decisions",$"/referrals/{referralId:D}/decisions",$"/conditions/{condition.Id:D}/resolutions"})
        {using var denied=await Post(path,new {cycleId=cycle.Id},antiForgery:false);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);}
        using(var denied=await Post("/evidence",new {cycleId=cycle.Id},version:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
        using(var denied=await Post("/evidence",new {cycleId=cycle.Id},lease:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post("/evidence?unknown=1",new {cycleId=cycle.Id}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Upload(Guid.NewGuid().ToString(),etag,true))Assert.Equal(HttpStatusCode.UnprocessableEntity,denied.StatusCode);
        var uploadKey=Guid.NewGuid().ToString();var uploadVersion=etag;
        using var uploaded=await Upload(uploadKey,uploadVersion);var upload=await Success(uploaded,HttpStatusCode.Created);var fileId=upload.GetProperty("id").GetGuid();
        using(var replay=await Upload(uploadKey,uploadVersion))Assert.Equal(await uploaded.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        Assert.Equal(root+$"/evidence-files/{fileId:D}/content",uploaded.Headers.Location!.ToString());
        var attach=new {cycleId=cycle.Id,fileId,requirementCode=required.Code,inputFingerprint=required.InputFingerprint,reason="Attach reviewed HTTP warranty file"};
        using(var denied=await Post("/evidence",new {attach.cycleId,attach.fileId,attach.requirementCode,attach.inputFingerprint,attach.reason,approved=true}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post("/evidence",attach,lease:Guid.NewGuid().ToString()))Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using(var attached=await Post("/evidence",attach))
        {
            var associationId=(await Success(attached,HttpStatusCode.Created)).GetProperty("id").GetGuid();
            var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==associationId);
            var childTag="\""+Convert.ToBase64String(association.RowVersion)+"\"";
            var review=new {cycleId=cycle.Id,associationEtag=childTag,outcome="accepted",expectedFingerprint=required.InputFingerprint,reason="Review HTTP warranty acknowledgement"};
            using(var denied=await Post($"/evidence/{associationId:D}/reviews",new {review.cycleId,associationEtag="\"AAAAAAAAAAA=\"",review.outcome,review.expectedFingerprint,review.reason}))Assert.Equal(HttpStatusCode.PreconditionFailed,denied.StatusCode);
            using(var reviewed=await Post($"/evidence/{associationId:D}/reviews",review))await Success(reviewed,HttpStatusCode.OK);
            var resolution=new {cycleId=cycle.Id,conditionEtag="\""+Convert.ToBase64String(condition.RowVersion)+"\"",evidenceAssociationId=associationId,outcome="satisfied",reason="Resolve HTTP warranty acknowledgement"};
            using(var denied=await Post($"/conditions/{condition.Id:D}/resolutions",new {resolution.cycleId,resolution.conditionEtag,evidenceAssociationId=Guid.NewGuid(),resolution.outcome,resolution.reason}))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            using(var resolved=await Post($"/conditions/{condition.Id:D}/resolutions",resolution))await Success(resolved,HttpStatusCode.OK);
            Assert.True(await referrals.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
            association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==associationId);
            using(var withdrawn=await Post($"/evidence/{associationId:D}/withdraw",new {cycleId=cycle.Id,associationEtag="\""+Convert.ToBase64String(association.RowVersion)+"\"",reason="Withdraw fictional HTTP acknowledgement"}))await Success(withdrawn,HttpStatusCode.OK);
            Assert.False(await referrals.ConditionSatisfiedAsync(f.Underwriter,cycle.DraftId,condition.Id));
        }
        var row=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==referralId);
        var decline=new {referralId,etag="\""+Convert.ToBase64String(row.RowVersion)+"\"",outcome="decline",reason="Decline fictional HTTP servicing risk"};
        using(var denied=await Post("/referrals/decisions",new {cycleId=cycle.Id,decisions=new[]{decline,decline}}))Assert.Equal(HttpStatusCode.UnprocessableEntity,denied.StatusCode);
        using(var declined=await Post("/referrals/decisions",new {cycleId=cycle.Id,decisions=new[]{decline}}))await Success(declined,HttpStatusCode.OK);
        row=await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==referralId);Assert.Equal("declined",row.State);
        var reopen=new {referralId,etag="\""+Convert.ToBase64String(row.RowVersion)+"\"",outcome="reopen",reason="Reopen fictional HTTP servicing risk"};
        using(var denied=await Post($"/referrals/{Guid.NewGuid():D}/decisions",new {cycleId=cycle.Id,decision=reopen}))Assert.Equal(HttpStatusCode.UnprocessableEntity,denied.StatusCode);
        using(var reopened=await Post($"/referrals/{referralId:D}/decisions",new {cycleId=cycle.Id,decision=reopen}))await Success(reopened,HttpStatusCode.OK);
        Assert.Equal("open",(await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x=>x.Id==referralId)).State);
    }
}
