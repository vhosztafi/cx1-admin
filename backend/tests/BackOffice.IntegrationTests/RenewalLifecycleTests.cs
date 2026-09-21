using System.Text;
using System.Text.Json;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData("motor-trade-road-risks",false,false)]
    [InlineData("motor-trade-combined",false,false)]
    [InlineData("motor-trade-road-risks",true,false)]
    [InlineData("motor-trade-combined",true,false)]
    [InlineData("motor-trade-road-risks",false,true)]
    [InlineData("motor-trade-combined",false,true)]
    public Task RealSqlRenewalLifecycleInvitationsRetainExactTermsAndSeparateAcceptance(string product,bool issue,bool lapseRace)
        => VerifyRenewalLifecycle(product,issue,lapseRace);

    private async Task VerifyRenewalLifecycle(string product,bool issue,bool lapseRace,
        Func<BackOfficeDbContext,DecisionFixture,ServicingCycle,ServicingTermsVersion,Task>? onPrepared=null,
        Func<BackOfficeDbContext,DecisionFixture,ServicingCycle,ServicingAcceptance,Guid,string,Task>? onAccepted=null)
    {
        await WithDatabase(async(db,password)=>
        {
            await using(var transaction=await db.Database.BeginTransactionAsync()){await RenewalLifecycleSeed.SeedAsync(db);await transaction.CommitAsync();}
            var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
            await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            var issued=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
            static string Key()=>Guid.NewGuid().ToString();
            var drafts=new ServicingDraftService(f.Factory,f.Clock);var preparation=new RenewalPreparationService(f.Factory,f.Clock);
            var preview=await preparation.PreviewAsync(f.Underwriter,issued.TermId);
            var start=preview.TermIntent;
            var created=await drafts.CreateAsync(f.Underwriter,issued.TermId,Version(preview.TermEtag),new("renewal",preview.BaseVersionId,
                JsonSerializer.SerializeToElement(new{localDate=start.GetProperty("localStartDate").GetString(),localTime=start.GetProperty("localStartTime").GetString(),timeZone="Europe/London",utcOffsetMinutes=start.GetProperty("utcOffsetMinutes").GetInt32()}),
                "Prepare fictional renewal invitation"),Key(),Guid.NewGuid());
            var leased=await drafts.LeaseAsync(f.Underwriter,created.ResourceId,Version(created.Etag!),"acquire",null,null,Key(),Guid.NewGuid());
            var fence=JsonSerializer.Deserialize<JsonElement>(leased.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
            var prepared=await preparation.PrepareAsync(f.Underwriter,created.ResourceId,Version(leased.Etag!),fence,12,null,Key(),Guid.NewGuid());
            using(var editor=JsonDocument.Parse((await drafts.ReadEditorAsync(f.Underwriter,created.ResourceId)).Body))
            {
                var slices=editor.RootElement.GetProperty("assessment").GetProperty("slices");
                Assert.Equal(1,slices.GetArrayLength());
                Assert.Equal((await db.Set<RenewalPreparationVersion>().AsNoTracking().SingleAsync(x=>x.Id==prepared.ResourceId)).StartsAt,slices[0].GetProperty("effectiveAt").GetDateTimeOffset());
                Assert.Empty(slices[0].GetProperty("changeIds").EnumerateArray());
            }
            var uploaded=await preparation.UploadExperienceAsync(f.Underwriter,created.ResourceId,Version(prepared.Etag!),fence,"claims.txt","text/plain",Encoding.UTF8.GetBytes("Fictional supplied nil claims and GBP1000 earned"),Key(),Guid.NewGuid());
            var supplied=await preparation.SaveExperienceAsync(f.Underwriter,created.ResourceId,Version(uploaded.Etag!),fence,new(new(2025,9,16),new(2026,9,16),0,0m,0m,1000m,"agency","Fictional observed nil claims",uploaded.ResourceId),Key(),Guid.NewGuid());
            var reviewed=await preparation.ReviewExperienceAsync(f.Underwriter,created.ResourceId,supplied.ResourceId,Version(supplied.Etag!),fence,"accepted","Verified fictional supplied experience",Key(),Guid.NewGuid());
            var draft=await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==created.ResourceId);
            var requested=await new ServicingRatingService(f.Factory,f.Clock).RateAsync(f.Underwriter,draft.Id,draft.CurrentRevisionId!.Value,Version(reviewed.Etag!),fence,"Rate complete fictional renewal",Key(),Guid.NewGuid());
            var cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==requested.ResourceId);
            var jobs=new SqlJobLeases(f.Factory,f.Clock);var worker=new ServicingRatingWorker(f.Factory,f.Clock);
            var claim=Assert.IsType<JobLease>(await jobs.ClaimWorkAsync("servicing-rating",cycle.WorkId));Assert.True(await worker.ApplyAsync(claim,await worker.ExecuteProviderAsync(claim)));
            cycle=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync(x=>x.Id==cycle.Id);
            var terms=new ServicingTermsService(f.Factory,f.Clock);var evidence=new ServicingEvidenceService(f.Factory,f.Clock);
            var options=await terms.ReadAsync(f.Underwriter,draft.Id);
            var template=Assert.Single(options.Templates);Assert.Contains("renewal",template.Title,StringComparison.OrdinalIgnoreCase);
            var storedTemplate=await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x=>x.Id==template.Id);Assert.Equal("renewal-invitation",storedTemplate.Kind);
            var etag=options.DraftEtag;
            var proofFile=await evidence.UploadAsync(f.Underwriter,draft.Id,Version(etag),fence,"renewal-proof.txt","text/plain",Encoding.UTF8.GetBytes("Fictional reviewed renewal proof, signature and separate acceptance"),Key(),Guid.NewGuid());etag=proofFile.Etag!;
            foreach(var item in (await evidence.RequirementsAsync(f.Underwriter,draft.Id)).Requirements)
            {
                var r=item.Requirement;var attached=await evidence.AttachAsync(f.Underwriter,draft.Id,cycle.Id,Version(etag),fence,proofFile.ResourceId,r.Code,r.RiskItemId,r.InputFingerprint,"Attach fictional renewal proof",Key(),Guid.NewGuid());
                var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().SingleAsync(x=>x.Id==attached.ResourceId);
                var review=await evidence.ReviewAsync(f.Underwriter,draft.Id,cycle.Id,association.Id,Version(attached.Etag!),fence,association.RowVersion,"accepted",r.InputFingerprint,"Review fictional renewal proof",Key(),Guid.NewGuid());etag=review.Etag!;
            }
            var exactVersion=Version(etag);var key=Key();
            var invitation=await terms.PrepareAsync(f.Underwriter,draft.Id,cycle.Id,cycle.CurrentRatingId!.Value,template.Id,exactVersion,fence,key,Guid.NewGuid());
            Assert.True((await terms.PrepareAsync(f.Underwriter,draft.Id,cycle.Id,cycle.CurrentRatingId.Value,template.Id,exactVersion,fence,key,Guid.NewGuid())).Replayed);
            var document=await db.Set<ServicingTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==invitation.ResourceId);
            using(var json=JsonDocument.Parse(document.TermsJson))
            {Assert.Equal("renewal-contract-1",json.RootElement.GetProperty("format").GetString());Assert.Equal(prepared.ResourceId,json.RootElement.GetProperty("ratingInput").GetProperty("renewal").GetProperty("preparationVersionId").GetGuid());}
            if(onPrepared is not null){await onPrepared(db,f,cycle,document);return;}
            var sentEtag=await VerifyServicingDeliveryQueue(db,f,cycle,document,proofFile.ResourceId,fence,invitation.Etag!);
            var delivered=await terms.ReadAsync(f.Underwriter,draft.Id);Assert.Equal("delivered",delivered.Delivery!.State);Assert.Null(delivered.Acceptance);
            if(onAccepted is not null){await VerifyServicingAcceptance(db,f,cycle,document,proofFile.ResourceId,fence,sentEtag,
                onAccepted:(acceptance,etag)=>onAccepted(db,f,cycle,acceptance,fence,etag));return;}
            if(lapseRace){await VerifyServicingAcceptance(db,f,cycle,document,proofFile.ResourceId,fence,sentEtag,
                onAcceptancePrepared:(input,version)=>VerifyRenewalAcceptanceLapseRace(db,f,cycle,input,version,fence));return;}
            if(issue){await VerifyServicingAcceptance(db,f,cycle,document,proofFile.ResourceId,fence,sentEtag,
                onAccepted:(acceptance,acceptedEtag)=>VerifyRenewalIssue(db,f,cycle,acceptance,fence,acceptedEtag,issued,password));return;}
            await VerifyServicingAcceptance(db,f,cycle,document,proofFile.ResourceId,fence,sentEtag);
            Assert.Equal(issued.ContentHash,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync()).ContentHash);
            Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());
        });
    }
}
