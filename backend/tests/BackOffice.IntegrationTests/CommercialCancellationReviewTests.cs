using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Policies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlCommercialCancellationRejectsLaterIssuedRenewal()=>CommercialRenewalScenario(true,verifyCancellation:true);

    [Fact]
    public Task RealSqlCommercialCancellationRequiresCurrentNoticeSeparateApprovalAndPreview()=>CommercialTermsScenario(async(db,cycle,acceptance,actorId,now)=>
    {
        var source=await CommercialIssueCommand(db,cycle,acceptance,actorId);
        await source.Service.IssueAsync(source.Actor,source.Quote.Id,source.Quote.RowVersion,source.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await using(var tx=await db.Database.BeginTransactionAsync()){await CommercialUnderwritingCancellationSeed.SeedAsync(db);await tx.CommitAsync();}
        var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        var clock=new RatingClock{Current=term.StartsAt.AddDays(1)};
        var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="underwriter@cover.example");var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
        var authority=await db.Set<AuthorityVersion>().AsNoTracking().SingleAsync(x=>x.Id==cycle.AuthorityVersionId);
        db.Add(new UserAuthorityGrant{UserId=user.Id,AuthorityVersionId=authority.Id,GrantedBy=admin.Id,CreatedBy=admin.Id,CreatedAt=clock.Current,
            EffectiveFrom=authority.EffectiveFrom,EffectiveTo=authority.EffectiveTo,Reason="Explicit owned commercial cancellation requester authority"});await db.SaveChangesAsync();
        var requester=new ActorContext(user.Id,user.TeamId,null,new HashSet<string>{"underwriter"});
        var drafts=new ServicingDraftService(source.Factory,clock);var service=new CancellationReviewService(source.Factory,clock);
        static string K()=>Guid.NewGuid().ToString();static byte[] V(string value)=>Convert.FromBase64String(value.Trim('"'));
        var created=await drafts.CreateAsync(requester,term.Id,V((await drafts.ListAsync(requester,term.Id)).Etag),new("cancellation",basis.Id,
            JsonSerializer.SerializeToElement(new{localDate="2026-10-15",localTime="00:00",timeZone="Europe/London"}),"Fictional commercial non-payment cancellation"),K(),Guid.NewGuid());
        var lease=await drafts.LeaseAsync(requester,created.ResourceId,V(created.Etag!),"acquire",null,null,K(),Guid.NewGuid());
        var body=JsonNode.Parse(lease.Body)!;var fence=body["lease"]!["leaseToken"]!.GetValue<Guid>();body["proposal"]!["cancellationReasonCode"]="non-payment";
        var saved=await drafts.SaveAsync(requester,created.ResourceId,V(lease.Etag!),fence,body["proposal"]!.ToJsonString(),K(),Guid.NewGuid());
        var upload=await service.UploadAsync(requester,created.ResourceId,V(saved.Etag!),fence,"cancellation-notice",null,"notice.txt","text/plain",Encoding.UTF8.GetBytes("Fictional commercial notice"),K(),Guid.NewGuid());
        var reviewed=await service.ReviewEvidenceAsync(requester,created.ResourceId,upload.ResourceId,V(upload.Etag!),fence,"accepted","Review commercial notice without delivery",K(),Guid.NewGuid());
        Assert.Contains("cancellation-notice-delivery-required",(await service.ReadAsync(requester,created.ResourceId)).Blockers);
        upload=await service.UploadAsync(requester,created.ResourceId,V(reviewed.Etag!),fence,"cancellation-notice",clock.Current,"delivered.txt","text/plain",Encoding.UTF8.GetBytes("Fictional delivered commercial notice"),K(),Guid.NewGuid());
        reviewed=await service.ReviewEvidenceAsync(requester,created.ResourceId,upload.ResourceId,V(upload.Etag!),fence,"accepted","Review actual fictional commercial notice delivery",K(),Guid.NewGuid());
        var view=await service.ReadAsync(requester,created.ResourceId);Assert.Empty(view.Blockers);
        var prepared=await service.PrepareAsync(requester,created.ResourceId,V(reviewed.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());
        Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.ApproveAsync(requester,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Requester cannot approve own non-payment",K(),Guid.NewGuid()))).Status);
        reviewed=await service.ReviewEvidenceAsync(requester,created.ResourceId,upload.ResourceId,V(prepared.Etag!),fence,"rejected","Withdraw the delivered commercial notice review",K(),Guid.NewGuid());
        Assert.NotEmpty((await service.ReadAsync(requester,created.ResourceId)).Blockers);
        reviewed=await service.ReviewEvidenceAsync(requester,created.ResourceId,upload.ResourceId,V(reviewed.Etag!),fence,"accepted","Recheck the corrected commercial notice evidence",K(),Guid.NewGuid());
        view=await service.ReadAsync(requester,created.ResourceId);prepared=await service.PrepareAsync(requester,created.ResourceId,V(reviewed.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());
        var takeover=await drafts.LeaseAsync(source.Actor,created.ResourceId,V(prepared.Etag!),"takeover",null,"Independent senior commercial cancellation review",K(),Guid.NewGuid());
        fence=JsonNode.Parse(takeover.Body)!["lease"]!["leaseToken"]!.GetValue<Guid>();
        var approved=await service.ApproveAsync(source.Actor,created.ResourceId,V(takeover.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Independent senior approves commercial non-payment",K(),Guid.NewGuid());
        var command=new CancellationIssueInput(prepared.ResourceId,approved.ResourceId,view.PreviewHash,"Issue the reviewed commercial non-payment cancellation");
        var setting=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Scope==CancellationConfiguration.CommercialScope);
        db.Add(new SettingVersion{Scope=setting.Scope,Version=2,EffectiveFrom=clock.Current,Values=setting.Values,CreatedAt=clock.Current,CreatedBy=admin.Id});await db.SaveChangesAsync();
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.IssueAsync(source.Actor,created.ResourceId,V(approved.Etag!),fence,command,K(),Guid.NewGuid()))).Status);
        view=await service.ReadAsync(source.Actor,created.ResourceId);Assert.Null(view.ApprovalId);Assert.Empty(view.Blockers);
        prepared=await service.PrepareAsync(source.Actor,created.ResourceId,V(approved.Etag!),fence,view.PreviewHash,K(),Guid.NewGuid());
        approved=await service.ApproveAsync(source.Actor,created.ResourceId,V(prepared.Etag!),fence,prepared.ResourceId,view.PreviewHash,"Independent senior approves current commercial preview",K(),Guid.NewGuid());
        var approval=await db.Set<CancellationApproval>().AsNoTracking().SingleAsync(x=>x.Id==approved.ResourceId);
        var grant=await db.Set<UserAuthorityGrant>().AsNoTracking().SingleAsync(x=>x.Id==approval.AuthorityGrantId);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserAuthorityGrant SET RevokedAt={clock.Current},RevokedBy={admin.Id},RevocationReason=N'Owned independent approval revocation test' WHERE Id={grant.Id}");
        db.Add(new UserAuthorityGrant{UserId=grant.UserId,AuthorityVersionId=grant.AuthorityVersionId,GrantedBy=admin.Id,CreatedBy=admin.Id,CreatedAt=clock.Current,
            EffectiveFrom=grant.EffectiveFrom.AddDays(1),EffectiveTo=grant.EffectiveTo,Reason="Replacement current issuer grant must not revive the old approval"});await db.SaveChangesAsync();
        var revoked=await service.ReadAsync(source.Actor,created.ResourceId);Assert.True(revoked.CanApprove);Assert.Null(revoked.ApprovalId);Assert.Empty(revoked.Blockers);
        command=new(prepared.ResourceId,approved.ResourceId,view.PreviewHash,"Cannot issue against revoked independent approval");
        Assert.Equal(409,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.IssueAsync(source.Actor,created.ResourceId,V(approved.Etag!),fence,command,K(),Guid.NewGuid()))).Status);
        Assert.Equal(1,await db.Set<PolicyVersion>().CountAsync());Assert.Equal(1,await db.Set<CommercialExposureVersion>().CountAsync());Assert.Equal(1,await db.Set<Journal>().CountAsync());
    },stopAfterAccepted:true);
}
