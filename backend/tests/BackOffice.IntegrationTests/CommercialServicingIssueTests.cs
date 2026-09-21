using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RealSqlCommercialServicingIssueRatingRetainsOwnedTypedSourcesAndSingleFee(bool dated) => CommercialServicingIssueScenario(dated);

    [Fact]
    public Task RealSqlCommercialServicingAndNewBusinessRaceForDistrictHeadroom() => CommercialServicingIssueScenario(false,true);

    [Fact]
    public Task RealSqlCommercialServicingIssueRejectsStaleReviewedProof() => CommercialServicingIssueScenario(false,false,true);

    private Task CommercialServicingIssueScenario(bool dated,bool race=false,bool staleProof=false,bool verifyRenewal=false,
        Func<BackOfficeDbContext,IDbContextFactory<BackOfficeDbContext>,BackOffice.Application.ActorContext,TimeProvider,Task>? onIssued=null) => CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);
        await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await ServicingRatingSeed.SeedAsync(db);await tx.CommitAsync();
        }
        var clock=new RatingClock();var drafts=new ServicingDraftService(source.Factory,clock);
        static byte[] Version(string value)=>Convert.FromBase64String(value.Trim('"'));
        static JsonElement Body(string value)=>JsonSerializer.Deserialize<JsonElement>(value);
        static string Key()=>Guid.NewGuid().ToString();
        var listed=await drafts.ListAsync(source.Actor,basis.TermId);
        var created=await drafts.CreateAsync(source.Actor,basis.TermId,Version(listed.Etag),new("adjustment",basis.Id,
            JsonSerializer.SerializeToElement(new {localDate="2026-10-01",localTime="00:00",timeZone="Europe/London"}),"Fictional commercial adjustment rating"),Key(),Guid.NewGuid());
        var owned=await drafts.LeaseAsync(source.Actor,created.ResourceId,Version(created.Etag!),"acquire",null,null,Key(),Guid.NewGuid());
        var fence=Body(owned.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var proposal=JsonNode.Parse(Body(owned.Body).GetProperty("proposal").GetRawText())!;
        var location=Body(basis.SnapshotJson).GetProperty("risk").GetProperty("locations")[0].GetProperty("id").GetGuid();
        proposal["changes"]=JsonSerializer.SerializeToNode(new[]{new {changeId=Guid.NewGuid(),riskItemId=location,kind="commercial-property",operation="update",payload=new {stock="11111.11"}}});
        if(dated)
        {
            proposal["dateBasis"]="per-cover-change";
            proposal["changes"]![0]!["kind"]="commercial-location";
            proposal["changes"]![0]!["payload"]!["address"]=JsonSerializer.SerializeToNode(new {postcode="EC1A 1BB"});
            proposal["changes"]!.AsArray().Add(JsonSerializer.SerializeToNode(new {changeId=Guid.NewGuid(),riskItemId=basis.PolicyId,kind="commercial-cover",operation="update",
                payload=new {contractWorks=new {sumInsured="12345.67"}},effectiveIntent=new {localDate="2026-11-01",localTime="00:00",timeZone="Europe/London"}}));
        }
        if(race)proposal["changes"]![0]!["payload"]!["stock"]=(decimal.Parse(Body(basis.SnapshotJson).GetProperty("risk").GetProperty("locations")[0].GetProperty("stock").GetString()!,System.Globalization.CultureInfo.InvariantCulture)+100000m).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture);
        var saved=await drafts.SaveAsync(source.Actor,created.ResourceId,Version(owned.Etag!),fence,proposal.ToJsonString(),Key(),Guid.NewGuid());
        var exposurePreview=await new CommercialExposureReadModel(source.Factory,clock).ReadAsync(source.Actor,"drafts",created.ResourceId);
        Assert.Equal("proposed",exposurePreview["coverageState"]);Assert.Equal("within-capacity",exposurePreview["outcome"]);
        var previewJson=JsonSerializer.SerializeToElement(exposurePreview);
        Assert.Equal("draft-revision",previewJson.GetProperty("source").GetProperty("kind").GetString());
        var revision=Body(saved.Body).GetProperty("revisionId").GetGuid();
        var proposedProperty=CommercialExposureProjection.Locations(Body(basis.SnapshotJson));
        var district=dated?"EC1A":proposedProperty.Single(x=>x.RiskItemId==location).District;
        var oldStock=decimal.Parse(Body(basis.SnapshotJson).GetProperty("risk").GetProperty("locations")[0].GetProperty("stock").GetString()!,System.Globalization.CultureInfo.InvariantCulture);
        var expectedProperty=(dated?proposedProperty.Single(x=>x.RiskItemId==location).SumInsured:proposedProperty.Where(x=>x.District==district).Sum(x=>x.SumInsured))-oldStock+(race?oldStock+100000m:11111.11m);
        var previewDistricts=previewJson.GetProperty("districts").EnumerateArray().Where(x=>x.GetProperty("district").GetString()==district).ToArray();
        Assert.NotEmpty(previewDistricts);
        Assert.All(previewDistricts,row=>Assert.Equal(expectedProperty,decimal.Parse(row.GetProperty("ownProposedSumInsured").GetString()!,System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(revision,previewJson.GetProperty("source").GetProperty("id").GetGuid());var ratings=new ServicingRatingService(source.Factory,clock);var key=Key();
        const string reason="Rate exact fictional commercial adjustment";
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>ratings.RateAsync(source.Actor,created.ResourceId,Guid.NewGuid(),Version(saved.Etag!),fence,reason,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>ratings.RateAsync(source.Actor,created.ResourceId,revision,Version(saved.Etag!),Guid.NewGuid(),reason,Key(),Guid.NewGuid()))).Status);
        Assert.Equal(0,await db.Set<ServicingCycle>().CountAsync());
        var requested=await ratings.RateAsync(source.Actor,created.ResourceId,revision,Version(saved.Etag!),fence,reason,key,Guid.NewGuid());
        Assert.Equal(202,requested.Status);Assert.Equal(requested.Body,(await ratings.RateAsync(source.Actor,created.ResourceId,revision,Version(saved.Etag!),fence,reason,key,Guid.NewGuid())).Body);
        var stored=await db.Set<ServicingCycle>().AsNoTracking().SingleAsync();var input=ServicingRatingInput.Read(stored.InputJson,stored.InputHash);
        Assert.True(input.IsCommercial);Assert.Equal(25m,input.Fee);Assert.Equal(dated?2:1,input.Slices.Count);Assert.All(input.Slices,x=>Assert.Null(x.Input));Assert.NotNull(input.Slices[0].Commercial);
        Assert.Equal(Convert.ToHexStringLower(basis.ContentHash),input.BaseContentHash);Assert.Equal(revision,input.RevisionId);
        var worker=new ServicingRatingWorker(source.Factory,clock);var jobs=new SqlJobLeases(source.Factory,clock);
        var lease=Assert.IsType<JobLease>(await jobs.ClaimWorkAsync(ServicingRatingService.WorkKind,stored.WorkId));
        var outcome=await worker.ExecuteProviderAsync(lease);Assert.True(await worker.ApplyAsync(lease,outcome));
        var read=await new ServicingRatingReadModel(source.Factory,clock).ReadAsync(source.Actor,created.ResourceId);
        Assert.Equal("rated",Assert.Single(read.Items).State);Assert.True(read.Items[0].Applicable);
        Assert.Equal(25m,outcome.Rating!.Fee);Assert.NotEqual(outcome.Rating.Slices[0].AnnualPremium,outcome.Rating.Premium);
        var proofs=await new ServicingEvidenceService(source.Factory,clock).RequirementsAsync(source.Actor,created.ResourceId);
        Assert.True(proofs.Applicable);Assert.All(proofs.Requirements,p=>Assert.False(p.Satisfied));
        Assert.Contains(proofs.Requirements,p=>p.Requirement.Code=="cc-location-proof" && p.Requirement.RiskItemId==location);
        Assert.DoesNotContain(proofs.Requirements,p=>p.Requirement.Code=="trading-history");
        var referrals=await db.Set<ServicingReferral>().AsNoTracking().Where(x=>x.CycleId==stored.Id).ToArrayAsync();
        Assert.Contains(referrals,r=>r.RuleCode=="AU-05" && r.RiskItemId==location);
        var exemplar=referrals.First(r=>r.RiskItemId==location);var foreign=Guid.NewGuid();
        var forged=JsonNode.Parse(exemplar.RequiredAuthorityJson)!;
        foreach(var trigger in forged["triggers"]!.AsArray())trigger!["requirement"]!["targetId"]=foreign;
        var fakeJson=forged.ToJsonString();var fakeId=Guid.NewGuid();var sequence=referrals.Max(x=>x.Sequence)+1;
        Assert.Equal(51342,(await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT ServicingReferral(Id,DraftId,CycleId,RevisionId,RatingId,Sequence,RuleCode,Dimension,RiskItemId,TargetKey,RequiredAuthorityJson,Reason,State,CreatedBy,CreatedAt,UpdatedAt) SELECT {fakeId},DraftId,CycleId,RevisionId,RatingId,{sequence},RuleCode,Dimension,{foreign},{foreign},{fakeJson},Reason,'open',CreatedBy,CreatedAt,UpdatedAt FROM ServicingReferral WHERE Id={exemplar.Id}"))).Number);
        Assert.Equal(referrals.Length,await db.Set<ServicingReferral>().CountAsync());
        var accepted=await VerifyCommercialServicingDecisions(db,source.Factory,source.Actor,clock,stored,fence,read.DraftEtag);
        var issueService=new ServicingIssueService(source.Factory,clock);
        var issueInput=new ServicingIssueInput(stored.Id,accepted.Acceptance.RatingId,accepted.Acceptance.TermsVersionId,accepted.Acceptance.Id,
            accepted.Acceptance.TermsHash,accepted.Acceptance.AssuranceHash,"Issue exact fictional commercial adjustment");
        if(staleProof)
        {
            var association=await db.Set<ServicingEvidenceAssociation>().AsNoTracking().FirstAsync(x=>x.DraftId==created.ResourceId&&x.RequirementCode=="cc-property-proof");
            var rejected=await new ServicingEvidenceService(source.Factory,clock).ReviewAsync(source.Actor,created.ResourceId,stored.Id,association.Id,Version(accepted.Etag),fence,
                association.RowVersion,"rejected",association.InputFingerprint,"Reject fictional proof after accepted terms",Key(),Guid.NewGuid());
            Assert.False((await new ServicingTermsService(source.Factory,clock).ReadAsync(source.Actor,created.ResourceId)).AcceptanceApplicable);
            var refusal=await Assert.ThrowsAsync<QuoteOperationException>(()=>issueService.IssueAsync(source.Actor,created.ResourceId,Version(rejected.Etag!),fence,issueInput,Key(),Guid.NewGuid()));
            Assert.Contains(refusal.Status,new[]{403,409,412});
            Assert.False(await db.Set<ServicingIssueDecision>().AnyAsync());Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<Journal>().CountAsync());
            return;
        }
        if(race)
        {
            var raceKey=Key();
            await VerifyCommercialServicingRace(db,source.Actor.UserId,clock,basis,()=>issueService.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,issueInput,raceKey,Guid.NewGuid()));
            return;
        }
        foreach(var invalid in new[]{issueInput with {AcceptanceId=Guid.NewGuid()},issueInput with {TermsHash=new string('f',64)},issueInput with {AssuranceHash=new string('a',64)}})
            await Assert.ThrowsAsync<QuoteOperationException>(()=>issueService.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,invalid,Key(),Guid.NewGuid()));
        Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<Journal>().CountAsync());
        Assert.Equal(1,await db.Set<CommercialExposureIssueDecision>().CountAsync());Assert.Equal(0,await db.Set<ServicingIssueDecision>().CountAsync());
        if(dated)
        {
            foreach(var mutation in new[]{"district","boundary"})
            {
                var fault=new CommercialExposureTamper(mutation);var factory=new CommercialExposureTamperFactory(db.Database.GetConnectionString()!,fault);
                var error=await Assert.ThrowsAsync<DbUpdateException>(()=>new ServicingIssueService(factory,clock).IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,issueInput,Key(),Guid.NewGuid()));
                Assert.True(fault.Applied);Assert.Equal(51945,Assert.IsType<SqlException>(error.InnerException).Number);
                Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());
                Assert.False(await db.Set<ServicingIssueDecision>().AnyAsync());
            }
        }
        var issueKey=Key();
        var issued=await issueService.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,issueInput,issueKey,Guid.NewGuid());
        if(onIssued is not null){Assert.Equal(201,issued.Status);await onIssued(db,source.Factory,source.Actor,clock);return;}
        Assert.Equal(201,issued.Status);
        Assert.Equal(issued.Body,(await issueService.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,issueInput,issueKey,Guid.NewGuid())).Body);
        Assert.Empty(Body(issued.Body).GetProperty("midIntentIds").EnumerateArray());
        var newVersion=Body(issued.Body).GetProperty("versionId").GetGuid();
        var snapshot=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==newVersion);
        Assert.Equal("issued-commercial-servicing-1",Body(snapshot.SnapshotJson).GetProperty("snapshotFormat").GetString());
        Assert.Empty(PolicySnapshotShape.Errors(Body(snapshot.SnapshotJson)));
        Assert.Equal(dated?3:2,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(0,await db.Set<PolicyMidIntent>().CountAsync());
        var retained=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==basis.Id);Assert.Equal(basis.SnapshotJson,retained.SnapshotJson);Assert.Equal(basis.ContentHash,retained.ContentHash);
        Assert.Equal(dated?3:2,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(dated?3:2,await db.Set<CommercialExposureIssueDecision>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync());
        if(dated)
        {
            var secondVersion=Body(issued.Body).GetProperty("versionIds")[1].GetGuid();
            var selectedRead=await new PolicyReadService(source.Factory,clock).ReadAsync(source.Actor,basis.PolicyId,basis.TermId,secondVersion);
            Assert.Equal(secondVersion,selectedRead["versionId"]);
            Assert.Equal(input.Slices[1].EffectiveAt,selectedRead["effectiveAt"]);
            var observations=new CommercialExposureReadModel(source.Factory,clock);
            var before=JsonSerializer.SerializeToElement(await observations.ReadAsync(source.Actor,"policies",basis.PolicyId,input.Slices[0].EffectiveAt.AddTicks(-1)));
            var after=JsonSerializer.SerializeToElement(await observations.ReadAsync(source.Actor,"policies",basis.PolicyId,input.Slices[0].EffectiveAt));
            Assert.Equal(basis.Id,before.GetProperty("source").GetProperty("id").GetGuid());
            Assert.Equal(newVersion,after.GetProperty("source").GetProperty("id").GetGuid());
            var originalDistrict=proposedProperty.Single(x=>x.RiskItemId==location).District;
            Assert.NotEqual("EC1A",originalDistrict);
            static decimal Own(JsonElement observation,string value)=>observation.GetProperty("districts").EnumerateArray()
                .Where(x=>x.GetProperty("district").GetString()==value)
                .Sum(x=>decimal.Parse(x.GetProperty("ownProposedSumInsured").GetString()!,System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(proposedProperty.Where(x=>x.District==originalDistrict).Sum(x=>x.SumInsured),Own(before,originalDistrict));
            Assert.Equal(0m,Own(before,"EC1A"));
            Assert.Equal(expectedProperty,Own(after,"EC1A"));
            Assert.Equal(proposedProperty.Where(x=>x.District==originalDistrict && x.RiskItemId!=location).Sum(x=>x.SumInsured),Own(after,originalDistrict));
            var options=await drafts.ListAsync(source.Actor,basis.TermId);
            var refusal=await Assert.ThrowsAsync<QuoteOperationException>(()=>drafts.CreateAsync(source.Actor,basis.TermId,Version(options.Etag),new("adjustment",basis.Id,
                JsonSerializer.SerializeToElement(new {localDate="2026-11-01",localTime="00:00",timeZone="Europe/London"}),"Reject obsolete commercial issued base"),Key(),Guid.NewGuid()));
            Assert.Equal("servicing-base-stale",refusal.Code);Assert.Equal(1,await db.Set<ServicingCycle>().CountAsync());
            if(verifyRenewal)
            {
                await using(var tx=await db.Database.BeginTransactionAsync())
                {await RenewalPreparationSeed.SeedCommercialAsync(db);await tx.CommitAsync();}
                var renewals=new RenewalPreparationService(source.Factory,clock);
                var preview=await renewals.PreviewAsync(source.Actor,basis.TermId);
                Assert.Equal(secondVersion,preview.BaseVersionId);
                Assert.Equal(basis.Id,(await new PolicyReadService(source.Factory,clock).ReadAsync(source.Actor,basis.PolicyId))["versionId"]);
                var intent=preview.TermIntent;var effective=JsonSerializer.SerializeToElement(new{localDate=intent.GetProperty("localStartDate").GetString(),localTime=intent.GetProperty("localStartTime").GetString(),timeZone="Europe/London",utcOffsetMinutes=intent.GetProperty("utcOffsetMinutes").GetInt32()});
                var oldBase=await Assert.ThrowsAsync<QuoteOperationException>(()=>drafts.CreateAsync(source.Actor,basis.TermId,Version(preview.TermEtag),new("renewal",basis.Id,effective,"Reject renewal from current rather than final risk"),Key(),Guid.NewGuid()));
                Assert.Equal("renewal-base-stale",oldBase.Code);
                var createdRenewal=await drafts.CreateAsync(source.Actor,basis.TermId,Version(preview.TermEtag),new("renewal",secondVersion,effective,"Renew the final scheduled commercial risk"),Key(),Guid.NewGuid());
                Assert.Equal(201,createdRenewal.Status);
                Assert.Equal(secondVersion,(await db.Set<ServicingDraft>().AsNoTracking().SingleAsync(x=>x.Id==createdRenewal.ResourceId)).BaseVersionId);
                Assert.Equal(3,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());
            }
        }
        var actualDecision=await db.Set<ServicingIssueDecision>().AsNoTracking().SingleAsync();
        var actualGrant=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==actualDecision.GrantId);
        var current=clock.Current;clock.Current=actualGrant.EffectiveTo;
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issueService.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,issueInput,issueKey,Guid.NewGuid()))).Status);
        clock.Current=current;
        var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={current},RevokedBy={admin.Id},RevocationReason=N'Owned commercial servicing replay authorization test' WHERE Id={actualGrant.Id}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>issueService.IssueAsync(source.Actor,created.ResourceId,Version(accepted.Etag),fence,issueInput,issueKey,Guid.NewGuid()))).Status);
        Assert.Equal(dated?3:2,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync());
    },stopAfterAccepted:true);
}
