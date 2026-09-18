using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyServicingTermsHttp(BackOfficeDbContext db,DecisionFixture f,string password,ServicingCycle cycle,
        ServicingTermsVersion contract,Guid fileId,Guid fence,string etag)
    {
        using var host=ServicingRatingApiHost(db,f.Clock,false).WithWebHostBuilder(builder=>builder.UseSetting("Cover:ServicingDeliveryWorkerEnabled","false")
            .ConfigureServices(services=>services.AddScoped(p=>new ServicingTermsService(p.GetRequiredService<IDbContextFactory<BackOfficeDbContext>>(),f.Clock))));
        using var client=host.CreateClient();var csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        using(var login=new HttpRequestMessage(HttpMethod.Post,"/api/v1/auth/login"){Content=JsonContent.Create(new{email="underwriter@cover.example",password})})
        {login.Headers.Add("X-CSRF-Token",csrf);using var signed=await client.SendAsync(login);signed.EnsureSuccessStatusCode();}
        csrf=(await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("requestToken").GetString()!;
        var root=$"/api/v1/drafts/{cycle.DraftId:D}";var serial=0;
        async Task<HttpResponseMessage> Post(string path,object input,string? key=null,string? expected=null,bool antiForgery=true,string? lease=null)
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,root+path){Content=input is string raw?new StringContent(raw,Encoding.UTF8,"application/json"):JsonContent.Create(input)};
            if(key!="missing")request.Headers.Add("Idempotency-Key",key??Guid.NewGuid().ToString());
            if(expected!="missing")request.Headers.TryAddWithoutValidation("If-Match",expected??etag);
            if(lease!="missing")request.Headers.Add("X-Edit-Lease",lease??fence.ToString());
            if(antiForgery)request.Headers.Add("X-CSRF-Token",csrf);return await client.SendAsync(request);
        }
        async Task<JsonElement> Capture(HttpResponseMessage response,HttpStatusCode status,string schema)
        {
            var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==status,text);Assert.True(response.Headers.CacheControl!.NoStore);
            var data=JsonSerializer.Deserialize<JsonElement>(text);
            if(Environment.GetEnvironmentVariable("COVER_TERMS_RESPONSES_DIRECTORY") is {Length:>0} output)
            {Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,$"{cycle.Id:D}-{++serial}.json"),JsonSerializer.Serialize(new{schema,data}));}
            return data;
        }
        using(var read=await client.GetAsync(root+"/terms"))
        {var view=await Capture(read,HttpStatusCode.OK,"ServicingTermsView");Assert.False(view.GetProperty("acceptanceApplicable").GetBoolean());Assert.Null(read.Headers.ETag);}
        using(var anonymous=host.CreateClient()){using var denied=await anonymous.GetAsync(root+"/terms");Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);}
        using(var denied=await client.GetAsync(root+"/terms?unknown=1"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await client.GetAsync($"/api/v1/drafts/{Guid.NewGuid():D}/terms"))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        var prepare=new{cycleId=cycle.Id,ratingId=cycle.CurrentRatingId,templateVersionId=contract.TemplateVersionId};
        using(var denied=await Post("/terms/prepare",prepare,antiForgery:false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post("/terms/prepare",prepare,key:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post("/terms/prepare",prepare,expected:"missing"))Assert.Equal(HttpStatusCode.PreconditionRequired,denied.StatusCode);
        using(var denied=await Post("/terms/prepare",prepare,expected:"W/"+etag))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post("/terms/prepare",prepare,lease:"missing"))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using(var denied=await Post("/terms/prepare",prepare,lease:Guid.NewGuid().ToString()))Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using(var denied=await Post("/terms/prepare",new{prepare.cycleId,prepare.ratingId,prepare.templateVersionId,premium="0.00"}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var prepareKey=Guid.NewGuid().ToString();var before=etag;
        using(var response=await Post("/terms/prepare",prepare,prepareKey))
        {
            var receipt=await Capture(response,HttpStatusCode.Created,"ServicingTermsReceipt");Assert.Equal(contract.Id,receipt.GetProperty("id").GetGuid());etag=response.Headers.ETag!.Tag;
            using var replay=await Post("/terms/prepare",prepare,prepareKey,before);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Equal(await response.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        }
        var source=await db.Set<Quote>().AsNoTracking().SingleAsync(x=>x.Id==f.QuoteId);
        var contact=await db.Set<Contact>().AsNoTracking().FirstAsync(x=>x.ClientId==source.ClientId && x.RelationshipId==source.RelationshipId && x.EndedAt==null && x.Email!=null);
        var send=new{cycleId=cycle.Id,termsVersionId=contract.Id,recipientContactIds=new[]{contact.Id}};
        using(var denied=await Post("/terms/send",send,antiForgery:false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post("/terms/send",new{send.cycleId,send.termsVersionId,recipientContactIds=new[]{Guid.NewGuid()}}))Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);
        using(var denied=await Post("/terms/send",new{send.cycleId,send.termsVersionId,send.recipientContactIds,delivered=true}))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var sendKey=Guid.NewGuid().ToString();before=etag;Guid deliveryId;
        using(var response=await Post("/terms/send",send,sendKey))
        {
            var receipt=await Capture(response,HttpStatusCode.Accepted,"ServicingTermsReceipt");deliveryId=receipt.GetProperty("id").GetGuid();etag=response.Headers.ETag!.Tag;
            using var replay=await Post("/terms/send",send,sendKey,before);Assert.Equal(HttpStatusCode.Accepted,replay.StatusCode);Assert.Equal(await response.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        }
        var delivery=await db.Set<ServicingTermsDelivery>().AsNoTracking().SingleAsync(x=>x.Id==deliveryId);
        var worker=new ServicingDeliveryWorker(f.Factory,f.Clock);var claim=(await new SqlJobLeases(f.Factory,f.Clock).ClaimWorkAsync(ServicingTermsService.WorkKind,delivery.WorkId))!;
        Assert.True(await worker.ApplyAsync(claim,await worker.ExecuteProviderAsync(claim)));
        etag="\""+Convert.ToBase64String((await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==cycle.DraftId)).RowVersion)+"\"";
        var evidence=new ServicingEvidenceService(f.Factory,f.Clock);var purpose=Assert.Single((await evidence.RequirementsAsync(f.Underwriter,cycle.DraftId)).Requirements,x=>x.Requirement.Code=="acceptance-proof").Requirement;
        var attached=await evidence.AttachAsync(f.Underwriter,cycle.DraftId,cycle.Id,Convert.FromBase64String(etag.Trim('"')),fence,fileId,purpose.Code,null,purpose.InputFingerprint,"Attach fictional HTTP acceptance proof",Guid.NewGuid().ToString(),Guid.NewGuid());
        var proof=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
        var reviewed=await evidence.ReviewAsync(f.Underwriter,cycle.DraftId,cycle.Id,proof.Id,Convert.FromBase64String(attached.Etag!.Trim('"')),fence,proof.RowVersion,"accepted",purpose.InputFingerprint,"Review fictional HTTP acceptance proof",Guid.NewGuid().ToString(),Guid.NewGuid());etag=reviewed.Etag!;
        JsonElement assessment;using(var read=await client.GetAsync(root+"/terms"))assessment=await Capture(read,HttpStatusCode.OK,"ServicingTermsView");
        var accept=new{cycleId=cycle.Id,ratingId=cycle.CurrentRatingId,termsVersionId=contract.Id,deliveryId,termsHash=contract.TermsHash,
            assuranceHash=assessment.GetProperty("assuranceHash").GetString(),accepterLabel="Fictional HTTP customer",acceptedAt=f.Clock.GetUtcNow(),channel="email",evidenceAssociationId=proof.Id};
        using(var denied=await Post("/acceptances",accept,antiForgery:false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var denied=await Post("/acceptances",new{accept.cycleId,accept.ratingId,accept.termsVersionId,accept.deliveryId,accept.termsHash,accept.assuranceHash,accept.accepterLabel,acceptedAt="2026-09-18T12:00:00",accept.channel,accept.evidenceAssociationId}))Assert.Equal(HttpStatusCode.UnprocessableEntity,denied.StatusCode);
        var acceptKey=Guid.NewGuid().ToString();before=etag;
        using(var response=await Post("/acceptances",accept,acceptKey))
        {
            await Capture(response,HttpStatusCode.Created,"ServicingTermsReceipt");etag=response.Headers.ETag!.Tag;
            using var replay=await Post("/acceptances",accept,acceptKey,before);Assert.Equal(HttpStatusCode.Created,replay.StatusCode);Assert.Equal(await response.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        }
        using(var read=await client.GetAsync(root+"/terms")){var view=await Capture(read,HttpStatusCode.OK,"ServicingTermsView");Assert.True(view.GetProperty("acceptanceApplicable").GetBoolean());}
        foreach(var kind in new[]{"terms","deliveries","acceptances"})
        {
            using var history=await client.GetAsync(root+"/terms/history/"+kind+"?pageSize=1");
            var page=await Capture(history,HttpStatusCode.OK,"ServicingTermsHistoryPage");Assert.Single(page.GetProperty("items").EnumerateArray());
            if(kind!="terms")
            {
                var cursor=page.GetProperty("nextCursor").GetString();Assert.False(string.IsNullOrEmpty(cursor));
                using var next=await client.GetAsync(root+"/terms/history/"+kind+"?pageSize=1&cursor="+Uri.EscapeDataString(cursor!));next.EnsureSuccessStatusCode();
                var second=await Capture(next,HttpStatusCode.OK,"ServicingTermsHistoryPage");Assert.Single(second.GetProperty("items").EnumerateArray());
                Assert.NotEqual(page.GetProperty("items")[0].GetProperty("id").GetGuid(),second.GetProperty("items")[0].GetProperty("id").GetGuid());
            }
        }
        using(var retained=await client.GetAsync(root+$"/terms/history/terms/{contract.Id:D}"))await Capture(retained,HttpStatusCode.OK,"ServicingTermsSnapshot");
        using(var denied=await client.GetAsync(root+$"/terms/history/terms/{Guid.NewGuid():D}"))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={f.Underwriter.UserId}");
        using(var denied=await Post("/acceptances",accept,acceptKey,before))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        Assert.Equal(1,await db.Set<ServicingTermsVersion>().CountAsync());Assert.Equal(2,await db.Set<ServicingTermsDelivery>().CountAsync());Assert.Equal(2,await db.Set<ServicingAcceptance>().CountAsync());
    }
}
