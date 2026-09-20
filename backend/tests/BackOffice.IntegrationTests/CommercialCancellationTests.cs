using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.SqlClient;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    internal async Task<Guid> SeedCommercialCancellationBrowserPolicy(BackOfficeDbContext db)
    {
        Guid policyId=Guid.Empty;
        await CommercialTermsScenario(async(database,cycle,acceptance,actorId,now)=>
        {
            var source=await CommercialIssueCommand(database,cycle,acceptance,actorId);
            var issued=await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
            policyId=issued.ResourceId;
        },stopAfterAccepted:true,existingDb:db);
        return policyId;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RealSqlCommercialCancellationReleasesOnlyAtEffectiveTimeAndPreventsRenewal(bool employersSelected)=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);
        await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        await using(var tx=await db.Database.BeginTransactionAsync())
        {await CommercialUnderwritingCancellationSeed.SeedAsync(db);await RenewalPreparationSeed.SeedCommercialAsync(db);await tx.CommitAsync();}
        var clock=new RatingClock{Current=term.StartsAt.AddDays(1)};
        var drafts=new ServicingDraftService(source.Factory,clock);var review=new CancellationReviewService(source.Factory,clock);
        static byte[] V(string value)=>Convert.FromBase64String(value.Trim('"'));
        static string K()=>Guid.NewGuid().ToString();
        var date=JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"});
        var created=await drafts.CreateAsync(source.Actor,basis.TermId,V((await drafts.ListAsync(source.Actor,basis.TermId)).Etag),
            new("cancellation",basis.Id,date,"Fictional commercial cancellation request"),K(),Guid.NewGuid());
        var lease=await drafts.LeaseAsync(source.Actor,created.ResourceId,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());
        var body=JsonNode.Parse(lease.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();
        body["proposal"]!["cancellationReasonCode"]="insured-request";
        var saved=await drafts.SaveAsync(source.Actor,created.ResourceId,V(lease.Etag!),fence,body["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
        var upload=await review.UploadAsync(source.Actor,created.ResourceId,V(saved.Etag!),fence,"cancellation-request",null,"commercial-request.txt","text/plain",
            Encoding.UTF8.GetBytes("Fictional insured commercial cancellation request."),K(),Guid.NewGuid());
        var evidence=await review.ReviewEvidenceAsync(source.Actor,created.ResourceId,upload.ResourceId,V(upload.Etag!),fence,"accepted","Reviewed fictional commercial request",K(),Guid.NewGuid());
        var view=await review.ReadAsync(source.Actor,created.ResourceId);Assert.Empty(view.Blockers);
        var prepared=await review.PrepareAsync(source.Actor,created.ResourceId,V(evidence.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());
        var approval=await review.ApproveAsync(source.Actor,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Approve reviewed commercial cancellation",K(),Guid.NewGuid());
        var command=new CancellationIssueInput(prepared.ResourceId,approval.ResourceId,view.PreviewHash,"Issue reviewed commercial cancellation");
        await PublishCommercialTestLimit(db,actorId,clock.Current,1m);
        var beforeRelease=await db.Set<CommercialExposureVersion>().AsNoTracking().SingleAsync();
        Assert.True(CommercialExposureRules.Snapshot(await CommercialExposureProjection.ReadAsync(db,beforeRelease.BookId,clock.Current),beforeRelease.BookId,clock.Current,clock.Current).Sum(x=>x.PropertySum)>1m);
        Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(source.Actor,created.ResourceId,new byte[8],fence,command,K(),Guid.NewGuid()))).Status);
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(source.Actor,created.ResourceId,V(approval.Etag!),fence,command with{PreviewHash=new string('f',64)},K(),Guid.NewGuid()))).Status);
        var key=K();
        foreach(var corruptRelease in new[]{true,false})
        {
            var fault=new CommercialCancellationFault(corruptRelease);var failing=new CancellationReviewService(new CommercialCancellationFaultFactory(db.Database.GetConnectionString()!,fault),clock);
            await Assert.ThrowsAnyAsync<Exception>(()=>failing.IssueAsync(source.Actor,created.ResourceId,V(approval.Etag!),fence,command,key,Guid.NewGuid()));
            Assert.True(fault.Applied);Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(1,await db.Set<Journal>().CountAsync());
            Assert.Empty(await db.Set<CancellationIssueDecision>().ToArrayAsync());Assert.Empty(await db.Set<CancellationConsequence>().ToArrayAsync());
        }
        var issued=await review.IssueAsync(source.Actor,created.ResourceId,V(approval.Etag!),fence,command,key,Guid.NewGuid());
        Assert.Equal(201,issued.Status);Assert.Equal(issued.Body,(await review.IssueAsync(source.Actor,created.ResourceId,V(approval.Etag!),fence,command,key,Guid.NewGuid())).Body);
        var receipt=JsonSerializer.Deserialize<JsonElement>(issued.Body);Assert.Equal("0.00",receipt.GetProperty("cashPaid").GetString());
        var versionId=receipt.GetProperty("versionId").GetGuid();var effective=receipt.GetProperty("effectiveAt").GetDateTimeOffset();
        var policy=new PolicyReadService(source.Factory,clock);
        Assert.Equal("active",(await policy.ReadAsync(source.Actor,basis.PolicyId))["coverageState"]);
        Assert.Equal(basis.Id,(await policy.ReadAtAsync(source.Actor,basis.PolicyId,effective.AddTicks(-1),clock.Current))["versionId"]);
        Assert.Equal("cancelled",(await policy.ReadAtAsync(source.Actor,basis.PolicyId,effective,clock.Current))["coverageState"]);
        var original=await db.Set<CommercialExposureVersion>().AsNoTracking().SingleAsync(x=>x.VersionId==basis.Id);
        var zero=await db.Set<CommercialExposureVersion>().AsNoTracking().SingleAsync(x=>x.VersionId==versionId);
        Assert.Equal(original.BookId,zero.BookId);Assert.Equal("[]",zero.LocationsJson);Assert.Equal(effective,zero.EffectiveAt);
        var slices=await CommercialExposureProjection.ReadAsync(db,original.BookId,clock.Current);
        Assert.NotEmpty(CommercialExposureRules.Snapshot(slices,original.BookId,effective.AddTicks(-1),clock.Current));
        Assert.Empty(CommercialExposureRules.Snapshot(slices,original.BookId,effective,clock.Current));
        Assert.DoesNotContain(await db.Set<CancellationConsequence>().ToArrayAsync(),x=>x.Kind=="mid-removal");
        Assert.Equal(CommercialDocumentSelection.CancellationKinds(employersSelected).Order(),(await db.Set<CancellationConsequence>().ToArrayAsync()).Select(x=>x.Kind).Order());
        Assert.Equal(basis.SnapshotJson,(await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.Id==basis.Id)).SnapshotJson);
        Assert.Equal(2,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync());
        Assert.Equal("renewal-expiring-term-cancelled",(await Assert.ThrowsAsync<QuoteOperationException>(()=>new RenewalPreparationService(source.Factory,clock).PreviewAsync(source.Actor,term.Id))).Code);
        var list=await drafts.ListAsync(source.Actor,term.Id);
        var nextLocal=TimeZoneInfo.ConvertTime(term.EndsAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London"));
        var nextDate=JsonSerializer.SerializeToElement(new{localDate=nextLocal.ToString("yyyy-MM-dd"),localTime=nextLocal.ToString("HH:mm"),timeZone="Europe/London",utcOffsetMinutes=(int)nextLocal.Offset.TotalMinutes});
        Assert.Equal("renewal-expiring-term-cancelled",(await Assert.ThrowsAsync<QuoteOperationException>(()=>drafts.CreateAsync(source.Actor,term.Id,V(list.Etag),new("renewal",versionId,nextDate,"Do not renew cancelled commercial term"),K(),Guid.NewGuid()))).Code);
        Assert.Equal(1,await db.Set<PolicyTerm>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());
        var grant=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==(db.Set<CancellationIssueDecision>().Select(x=>x.AuthorityGrantId).Single()));
        var present=clock.Current;clock.Current=grant.EffectiveTo;
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(source.Actor,created.ResourceId,V(approval.Etag!),fence,command,key,Guid.NewGuid()))).Status);clock.Current=present;
        var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={present},RevokedBy={admin.Id},RevocationReason=N'Owned cancellation replay authority test' WHERE Id={grant.Id}");
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>review.IssueAsync(source.Actor,created.ResourceId,V(approval.Etag!),fence,command,key,Guid.NewGuid()))).Status);
        var downgrade=await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20260920133214_CommercialRenewalPreparation"));Assert.Equal(51963,downgrade.Number);
        Assert.Equal(2,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(2,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(2,await db.Set<Journal>().CountAsync());
    },stopAfterAccepted:true,configureProposal:proposal=>
    {
        proposal["risk"]!["declarations"]!["answers"]!.AsArray().Single(x=>x!["questionId"]!.GetValue<string>()=="prototype.quote.36ef01068295")!["value"]=employersSelected;
        if(!employersSelected){proposal["risk"]!["liability"]!.AsObject().Remove("employersLimit");proposal["risk"]!["liability"]!.AsObject().Remove("employersReferenceNumber");}
    });

    [Fact]
    public Task RealSqlCommercialCancellationSeedIsMissingOnlyAndDoesNotGrantAuthority()=>WithDatabase(async(db,password)=>
    {
        async Task Seed()=>await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,
            includeRenewalLifecycle:true,includeCommercialCapture:true,includeCommercialUnderwriting:true);
        await Seed();
        var motor=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Scope==CancellationConfiguration.Scope);
        var original=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Scope==CancellationConfiguration.CommercialScope);
        Assert.NotNull(CancellationConfiguration.Parse(original.Values,CancellationConfiguration.CommercialScope));
        var grantIds=await db.Set<UserAuthorityGrant>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync();
        var authorityIds=await db.Set<AuthorityVersion>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync();
        var adverse=new SettingVersion{Scope=original.Scope,Version=2,EffectiveFrom=original.EffectiveFrom,
            Values=original.Values.Replace("commercial-demo-senior-1","ungranted-demo-authority"),CreatedAt=original.CreatedAt,CreatedBy=original.CreatedBy};
        db.Add(adverse);await db.SaveChangesAsync();await Seed();
        await db.GetService<IMigrator>().MigrateAsync("20260920133214_CommercialRenewalPreparation");await db.GetService<IMigrator>().MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var settings=await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope==original.Scope).OrderBy(x=>x.Version).ToArrayAsync();
        Assert.Equal(new[]{original.Id,adverse.Id},settings.Select(x=>x.Id));
        Assert.Equal(original.Values,settings[0].Values);Assert.Equal(adverse.Values,settings[1].Values);
        Assert.Equal(motor.Values,(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==motor.Id)).Values);
        Assert.Equal(grantIds,await db.Set<UserAuthorityGrant>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync());
        Assert.Equal(authorityIds,await db.Set<AuthorityVersion>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>x.Id).ToArrayAsync());
    });
}
