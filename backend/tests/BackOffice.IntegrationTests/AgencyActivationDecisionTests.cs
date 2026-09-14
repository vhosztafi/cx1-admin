using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Application.Agencies;
using BackOffice.Infrastructure.Agencies;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace BackOffice.IntegrationTests;
public sealed class AgencyActivationDecisionTests
{
    [Fact]public async Task RealSqlActivationAppliesTermsInvitationsNoticesAndFollowUpsAtomically()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION")??DemoDatabase.DefaultConnection);
        var owned="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=owned;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        try
        {
            ActorContext actor;Guid productId;
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                var user=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-admin@cover.example");actor=new(user.Id,user.TeamId,null,new HashSet<string>{"agency-admin"});productId=await db.Set<ProductVersion>().Select(x=>x.Id).FirstAsync();
            }
            var factory=new PooledDbContextFactory<BackOfficeDbContext>(options);var clock=TimeProvider.System;var commands=new SqlCommandBoundary(factory,clock);var drafts=new AgencyDraftService(factory,commands,clock);var evidence=new AgencyEvidenceService(commands,drafts,clock);var activation=new AgencyActivationService(drafts,evidence,commands,clock);
            var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime);
            var details=JsonNode.Parse("""
                {"legalName":"Fictional Complete Agency","entityType":"limited-company","companyNumber":"12345678","address":{"line1":"1 Fictional Road","town":"Leeds","postcode":"LS1 1AA","country":"GB"},"tradingAddressMode":"different","tradingAddress":{"line1":"2 Fictional Road","town":"Leeds","postcode":"LS1 1AB","country":"GB"},"regulatoryReference":"123456","regulatoryStatus":"appointed-representative","principalFirm":"Fictional Principal","clientMoneyBasis":"cass5-client-money","arrangesGeneralInsurance":"confirmed","territory":"UK","mainContact":{"name":"Fictional Main","email":"main@example.test","telephone":"01130000001"},"complianceContact":{"name":"Fictional Compliance","email":"compliance@example.test","telephone":"01130000002"},"relationshipManagerId":"cc1f5e5b-d08b-4a9c-bab4-d40310e491b8","correspondencePreference":"portal-only","commercialTerms":{"effectiveFrom":"2026-09-14","commissionBasis":"flat-rate","flatCommissionBasisPoints":1250,"feeSharing":"agreed-split","feeShareBasisPoints":1000,"volumeCommitmentMode":"target-tiered","volumeCommitment":"100000.00","minimumPremiumOverrideMode":"capacity-provider-agreed","minimumPremiumOverride":"150.00","referralRouting":"standard-internal-underwriting"},"compliance":{"tobaStatus":"signed","tobaVersion":"2026.1","tobaSignedOn":"2026-09-01","professionalIndemnityStatus":"meets-minimum","professionalIndemnityLimit":"2000000.00","piExpiresOn":"2027-09-14","dataProcessingAgreement":"signed"},"paymentTermsDays":30,"creditLimit":"0.00","settlement":{"statementCycle":"monthly","method":"bank-transfer","premiumCollection":"agency","commissionSettlement":"net-remittance"}}
                """)!.AsObject();details["relationshipManagerId"]=actor.UserId.ToString();details["commercialTerms"]!["effectiveFrom"]=today.ToString("yyyy-MM-dd");details["compliance"]!["tobaSignedOn"]=today.ToString("yyyy-MM-dd");details["compliance"]!["piExpiresOn"]=today.AddYears(1).ToString("yyyy-MM-dd");
            using var document=JsonDocument.Parse(details.ToJsonString());var normalized=AgencyDraftRules.Validate(document.RootElement);
            string Key()=>Guid.NewGuid().ToString();byte[] Version(CommandOutcome result)=>Convert.FromBase64String(result.Etag!.Trim('"'));
            var saved=await drafts.Save(actor,null,Key(),null,normalized,6,[new(productId,today,1250)],default);var id=saved.ResourceId;
            async Task Status(int expected,Func<Task<CommandOutcome>> action)=>Assert.Equal(expected,(await Assert.ThrowsAsync<AgencyCommandException>(action)).Status);
            await Status(422,()=>activation.Propose(actor,id,Key(),Version(saved),"Missing readiness"));
            saved=await new AgencyUserService(drafts,commands,clock).Stage(actor,id,Key(),Version(saved),AgencyUserRules.Validate("activation-broker@cover.example","Fictional activation administrator","broker-admin"));
            await Status(422,()=>activation.Propose(actor,id,Key(),Version(saved),"Missing evidence"));
            saved=await evidence.Upload(actor,id,Key(),Version(saved),AgencyEvidenceRules.ValidateFile("activation-proof.txt","text/plain","Fictional activation evidence"u8.ToArray()),default);var fileId=saved.ResourceId;
            foreach(var kind in AgencyEvidenceRules.AttestationKinds)saved=await evidence.Attest(actor,id,Key(),Version(saved),kind,fileId,"Fictional reviewed supporting evidence",kind=="professional-indemnity"?today.AddYears(1):null,default);
            foreach(var kind in AgencyEvidenceRules.CheckKinds)saved=await evidence.Check(actor,id,Key(),Version(saved),kind,default);
            var basis=Version(saved);var proposalKey=Key();var request=await activation.Propose(actor,id,proposalKey,basis,"Request independent activation review");Assert.Equal(202,request.Status);

            ActorContext reviewer,other;
            await using(var db=new BackOfficeDbContext(options))
            {
                var one=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="agency-reviewer@cover.example");reviewer=new(one.Id,one.TeamId,null,new HashSet<string>{"agency-admin"});
                var two=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");other=new(two.Id,two.TeamId,null,new HashSet<string>{"system-admin"});
            }
            var payload=new AgencyNotificationPayload(new EphemeralDataProtectionProvider());var notifications=new AgencyNotificationService(payload,clock);var issuer=new InvitationService(notifications,clock);
            var decisions=new AgencyActivationDecisions(drafts,activation,commands,issuer,notifications,clock);
            await Status(403,()=>decisions.Decide(actor,id,request.ResourceId,Key(),Version(request),true,"Self decision"));
            var rejected=await decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),false,"Return for independent review");Assert.Equal(200,rejected.Status);
            await using(var db=new BackOfficeDbContext(options)){Assert.Equal("draft",(await db.Set<Agency>().SingleAsync(x=>x.Id==id)).State);Assert.False(await db.Set<AgencyTermsVersion>().AnyAsync(x=>x.AgencyId==id));}
            request=await activation.Propose(actor,id,Key(),basis,"Ready for independent activation");
            saved=await drafts.Save(actor,id,Key(),basis,normalized,6,null,default);basis=Version(saved);
            await Status(409,()=>decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),true,"Changed agency base"));
            await decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),false,"Reject changed base");
            request=await activation.Propose(actor,id,Key(),basis,"Ready with current base");
            await using(var db=new BackOfficeDbContext(options))
            {
                var rule=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-distribution");db.Add(new SettingVersion{Scope=rule.Scope,Version=2,EffectiveFrom=clock.GetUtcNow(),Values=rule.Values});await db.SaveChangesAsync();
            }
            await Status(409,()=>decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),true,"Changed distribution rule"));
            await decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),false,"Reject changed rule");
            request=await activation.Propose(actor,id,Key(),basis,"Ready with current rule");
            await using(var db=new BackOfficeDbContext(options)){var manager=await db.Set<StaffUser>().SingleAsync(x=>x.Id==actor.UserId);manager.State="suspended";await db.SaveChangesAsync();}
            await Status(422,()=>decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),true,"Inactive relationship manager"));
            await using(var db=new BackOfficeDbContext(options)){var manager=await db.Set<StaffUser>().SingleAsync(x=>x.Id==actor.UserId);manager.State="active";await db.SaveChangesAsync();}
            await using(var db=new BackOfficeDbContext(options)){var product=await db.Set<ProductVersion>().SingleAsync(x=>x.Id==productId);product.QuestionSetVersion="demo-revised";await db.SaveChangesAsync();}
            await Status(409,()=>decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),true,"Changed catalogue version"));
            await decisions.Decide(reviewer,id,request.ResourceId,Key(),Version(request),false,"Reject changed catalogue");
            request=await activation.Propose(actor,id,Key(),basis,"Ready with current catalogue");
            var failingFactory=new PooledDbContextFactory<BackOfficeDbContext>(new DbContextOptionsBuilder<BackOfficeDbContext>(options).AddInterceptors(new FailAfterNotices()).Options);
            var failing=new AgencyActivationDecisions(drafts,activation,new SqlCommandBoundary(failingFactory,clock),issuer,notifications,clock);var failedKey=Key();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>failing.Decide(reviewer,id,request.ResourceId,failedKey,Version(request),true,"Injected failure"));
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("pending",(await db.Set<AgencyStateRequest>().SingleAsync(x=>x.Id==request.ResourceId)).State);
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);Assert.Equal("draft",agency.State);Assert.Equal(basis,agency.RowVersion);
                Assert.False(await db.Set<AgencyTermsVersion>().AnyAsync(x=>x.AgencyId==id));Assert.False(await db.Set<AgencyNotification>().AnyAsync(x=>x.AgencyId==id));Assert.False(await db.Set<AgencyFollowUp>().AnyAsync(x=>x.AgencyId==id));
                var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.AgencyId==id);Assert.Equal("staged",invitation.State);Assert.Null(invitation.TokenHash);
                Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key==failedKey));
            }
            async Task<(int Status,ActorContext Actor,string Key,CommandOutcome? Result)> Race(ActorContext person)
            {
                var key=Key();try{return(200,person,key,await decisions.Decide(person,id,request.ResourceId,key,Version(request),true,"Independent activation"));}
                catch(AgencyCommandException error){return(error.Status,person,key,null);}
            }
            var race=await Task.WhenAll(Race(reviewer),Race(other));Assert.Single(race,x=>x.Status==200);Assert.Single(race,x=>x.Status==412);var winner=race.Single(x=>x.Status==200);
            var replay=await decisions.Decide(winner.Actor,id,request.ResourceId,winner.Key,Version(request),true,"Independent activation");Assert.True(replay.Replayed);Assert.Equal(winner.Result!.Etag,replay.Etag);using(var receipt=JsonDocument.Parse(replay.Body))Assert.Single(receipt.RootElement.EnumerateObject());
            List<Guid> jobs;
            await using(var db=new BackOfficeDbContext(options))
            {
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);Assert.Equal("active",agency.State);Assert.NotEqual(basis,agency.RowVersion);
                var terms=await db.Set<AgencyTermsVersion>().SingleAsync(x=>x.AgencyId==id);Assert.Equal(1,terms.Version);Assert.Equal(request.ResourceId,terms.ApprovedStateRequestId);Assert.Equal(today,terms.EffectiveFrom);
                Assert.Single(await db.Set<AgencyProduct>().Where(x=>x.AgencyTermsVersionId==terms.Id).ToListAsync());
                var followups=await db.Set<AgencyFollowUp>().Where(x=>x.AgencyId==id).ToListAsync();Assert.Equal(2,followups.Count);Assert.Contains(followups,x=>x.Purpose=="pi-expiry"&&x.DueOn==today.AddYears(1));Assert.Contains(followups,x=>x.Purpose=="quarter-review"&&x.DueOn==today.AddMonths(3));
                var invitation=await db.Set<AgencyInvitation>().SingleAsync(x=>x.AgencyId==id);Assert.Equal("pending",invitation.State);Assert.NotNull(invitation.TokenHash);Assert.NotNull(invitation.IssuedAt);
                var notices=await db.Set<AgencyNotification>().Where(x=>x.AgencyId==id).ToListAsync();Assert.Equal(3,notices.Count);Assert.Single(notices,x=>x.InvitationId!=null);jobs=notices.Select(x=>x.WorkId).ToList();
                Assert.All(notices,x=>Assert.DoesNotContain("Fictional",x.ProtectedPayload));
            }
            var leases=new SqlJobLeases(factory,clock);var worker=new AgencyNotificationWorker(factory,payload,clock);
            foreach(var job in jobs){var lease=await leases.ClaimWorkAsync(AgencyNotificationService.Kind,job);Assert.NotNull(lease);var receipt=await worker.Deliver(lease);Assert.NotNull(receipt);await worker.Apply(lease,receipt.Value);}
            await using(var db=new BackOfficeDbContext(options)){Assert.Equal(3,await db.Set<AgencyNotificationReceipt>().CountAsync());Assert.Equal(2,await db.Set<AgencyFollowUp>().CountAsync(x=>x.AgencyId==id));}
            // Suspend a genuinely activated agency: published terms and completed delivery remain history.
            string termsBefore;byte[] activeVersion;
            await using(var db=new BackOfficeDbContext(options))
            {
                activeVersion=await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.RowVersion).SingleAsync();
                termsBefore=JsonSerializer.Serialize(await db.Set<AgencyTermsVersion>().Where(x=>x.AgencyId==id).ToListAsync());
            }
            var suspension=new AgencySuspensionService(drafts,commands,clock);
            var suspensionRequest=await suspension.Propose(actor,id,Key(),activeVersion,"Fictional suspension after activation");
            await suspension.Decide(reviewer,id,suspensionRequest.ResourceId,Key(),Version(suspensionRequest),true,"Independent suspension after activation");
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("suspended",await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
                Assert.Equal(termsBefore,JsonSerializer.Serialize(await db.Set<AgencyTermsVersion>().Where(x=>x.AgencyId==id).ToListAsync()));
                Assert.Equal("revoked",await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==id).Select(x=>x.State).SingleAsync());
                Assert.Equal(3,await db.Set<AgencyNotificationReceipt>().CountAsync());Assert.Equal(2,await db.Set<AgencyFollowUp>().CountAsync(x=>x.AgencyId==id));
            }
            var assessment=new AgencyReactivationAssessment(evidence,clock);
            async Task<AgencyReactivationReadiness> Assess(AgencyReactivationAssessment? chosen=null)
            {await using var db=new BackOfficeDbContext(options);await using var transaction=await db.Database.BeginTransactionAsync();var result=await (chosen??assessment).Assess(db,id);await transaction.CommitAsync();return result;}
            await using(var db=new BackOfficeDbContext(options))await Assert.ThrowsAsync<InvalidOperationException>(()=>assessment.Assess(db,id));
            var ready=await Assess();Assert.Single(ready.FreshInvitationUserIds);Assert.Equal(ready.Fingerprint,(await Assess()).Fingerprint);
            // A scheduled approved version replaces stale onboarding commercial inputs only on its London date.
            var proposal=JsonNode.Parse(termsBefore)!.AsArray()[0]!["Snapshot"]!.GetValue<string>();
            var scheduled=JsonNode.Parse(proposal)!.AsObject();scheduled["reason"]="Fictional scheduled terms before reactivation";
            scheduled["effectiveFrom"]=today.AddDays(1).ToString("yyyy-MM-dd");scheduled["commercialTerms"]!["effectiveFrom"]=today.AddDays(1).ToString("yyyy-MM-dd");scheduled["products"]![0]!["effectiveFrom"]=today.AddDays(1).ToString("yyyy-MM-dd");scheduled["products"]![0]!["brokerCommissionBasisPoints"]=1500;
            using var scheduledDocument=JsonDocument.Parse(scheduled.ToJsonString());
            var termsService=new AgencyTermsService(drafts,commands,clock);byte[] suspendedVersion;
            await using(var db=new BackOfficeDbContext(options))suspendedVersion=await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.RowVersion).SingleAsync();
            var termsRequest=await termsService.Propose(actor,id,Key(),suspendedVersion,scheduledDocument.RootElement);
            await termsService.Decide(reviewer,id,termsRequest.ResourceId,Key(),Version(termsRequest),true,"Independent future terms review");
            Assert.Equal(ready.TermsVersionId,(await Assess()).TermsVersionId);
            var tomorrow=new FixedTime(clock.GetUtcNow().AddDays(1));
            var tomorrowReady=await Assess(new AgencyReactivationAssessment(new AgencyEvidenceService(commands,drafts,tomorrow),tomorrow));
            Assert.NotEqual(ready.TermsVersionId,tomorrowReady.TermsVersionId);
            var expired=new FixedTime(clock.GetUtcNow().AddYears(2));
            Assert.Equal(422,(await Assert.ThrowsAsync<AgencyCommandException>(()=>Assess(new AgencyReactivationAssessment(new AgencyEvidenceService(commands,drafts,expired),expired)))).Status);
            var broker=ready.FreshInvitationUserIds.Single();
            await using(var db=new BackOfficeDbContext(options)){var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==broker);user.State="suspended";await db.SaveChangesAsync();}
            Assert.Equal(422,(await Assert.ThrowsAsync<AgencyCommandException>(()=>Assess())).Status);
            await using(var db=new BackOfficeDbContext(options)){var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==broker);user.State="invited";await db.SaveChangesAsync();}
            var restored=await Assess();Assert.NotEqual(ready.Fingerprint,restored.Fingerprint);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("suspended",await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
                Assert.Equal("revoked",await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==id).Select(x=>x.State).SingleAsync());
                Assert.Equal(3,await db.Set<AgencyNotification>().CountAsync(x=>x.AgencyId==id));
                db.Add(new SettingVersion{Scope="agency-distribution",Version=3,EffectiveFrom=clock.GetUtcNow(),Values="{\"demo\":true,\"kind\":\"agency-distribution\",\"productVersionIds\":[]}"});await db.SaveChangesAsync();
            }
            Assert.Equal(422,(await Assert.ThrowsAsync<AgencyCommandException>(()=>Assess())).Status);
            // Restore explicit distribution, then exercise the actual independently reviewed reactivation.
            await using(var db=new BackOfficeDbContext(options))
            {
                var allowed=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-distribution"&&x.Version==2);
                db.Add(new SettingVersion{Scope=allowed.Scope,Version=4,EffectiveFrom=clock.GetUtcNow(),Values=allowed.Values});await db.SaveChangesAsync();
            }
            var reactivation=new AgencyReactivationService(drafts,assessment,commands,issuer,clock);
            Guid inactiveId;var previouslyRevokedAt=clock.GetUtcNow().AddMinutes(-1);
            await using(var db=new BackOfficeDbContext(options))
            {
                var email=Guid.NewGuid()+"@example.test";
                var inactive=new StaffUser{AgencyId=id,State="suspended",Email=email,NormalizedEmail=email.ToUpperInvariant(),DisplayName="Fictional individually disabled user"};inactiveId=inactive.Id;db.Add(inactive);await db.SaveChangesAsync();
                db.Add(new UserRole{UserId=inactive.Id,RoleId=await db.Set<Role>().Where(x=>x.Code=="broker-readonly").Select(x=>x.Id).SingleAsync()});
                var brokerUser=await db.Set<StaffUser>().SingleAsync(x=>x.Id==broker);
                foreach(var user in new[]{inactive,brokerUser})db.Add(new UserSession{UserId=user.Id,TokenHash=RandomNumberGenerator.GetBytes(32),SecurityStamp=user.SecurityStamp,ExpiresAt=clock.GetUtcNow().AddDays(1),LastSeenAt=clock.GetUtcNow(),RevokedAt=user.Id==inactiveId?previouslyRevokedAt:null,DeviceLabel="Fictional retained session",TicketCiphertext=[1]});
                await db.SaveChangesAsync();
            }
            async Task<byte[]> CurrentBase(){await using var db=new BackOfficeDbContext(options);return await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.RowVersion).SingleAsync();}
            async Task<string> ReactivationState()
            {
                await using var db=new BackOfficeDbContext(options);
                return JsonSerializer.Serialize(new{agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id),users=await db.Set<StaffUser>().Where(x=>x.AgencyId==id).OrderBy(x=>x.Id).ToListAsync(),sessions=await db.Set<UserSession>().OrderBy(x=>x.Id).ToListAsync(),invitations=await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==id).OrderBy(x=>x.Id).ToListAsync(),notices=await db.Set<AgencyNotification>().Where(x=>x.AgencyId==id).OrderBy(x=>x.Id).ToListAsync()});
            }
            var reactivationBase=await CurrentBase();var beforeReactivation=await ReactivationState();var reactivateKey=Key();
            var restore=await reactivation.Propose(actor,id,reactivateKey,reactivationBase,"Fictional request to resume agency access");
            Assert.Equal(beforeReactivation,await ReactivationState());Assert.True((await reactivation.Propose(actor,id,reactivateKey,reactivationBase,"Fictional request to resume agency access")).Replayed);
            await Status(409,()=>reactivation.Propose(actor,id,Key(),reactivationBase,"Duplicate reactivation"));
            await Status(403,()=>reactivation.Decide(actor,id,restore.ResourceId,Key(),Version(restore),true,"Self reactivation"));
            await Status(404,()=>reactivation.Decide(reviewer,PartyDemoSeed.SecondAgencyId,restore.ResourceId,Key(),Version(restore),true,"Wrong agency"));
            await Status(412,()=>reactivation.Decide(reviewer,id,restore.ResourceId,Key(),new byte[8],true,"Wrong request version"));
            await reactivation.Decide(reviewer,id,restore.ResourceId,Key(),Version(restore),false,"Return for independent review");Assert.Equal(beforeReactivation,await ReactivationState());
            restore=await reactivation.Propose(actor,id,Key(),reactivationBase,"Review current reactivation");
            var expiredService=new AgencyReactivationService(drafts,new AgencyReactivationAssessment(new AgencyEvidenceService(commands,drafts,expired),expired),commands,issuer,expired);
            await Status(422,()=>expiredService.Decide(reviewer,id,restore.ResourceId,Key(),Version(restore),true,"Evidence expired before review"));
            await using(var db=new BackOfficeDbContext(options)){var user=await db.Set<StaffUser>().SingleAsync(x=>x.Id==broker);user.DisplayName="Fictional revised broker name";await db.SaveChangesAsync();}
            await Status(409,()=>reactivation.Decide(reviewer,id,restore.ResourceId,Key(),Version(restore),true,"Changed prerequisites"));
            await reactivation.Decide(reviewer,id,restore.ResourceId,Key(),Version(restore),false,"Reject changed prerequisites");
            restore=await reactivation.Propose(actor,id,Key(),reactivationBase,"Reactivation ready with current prerequisites");
            var staleRestore=restore.ResourceId;
            await using(var db=new BackOfficeDbContext(options)){var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);agency.UpdatedAt=clock.GetUtcNow();await db.SaveChangesAsync();}
            await Status(409,()=>reactivation.Decide(reviewer,id,restore.ResourceId,Key(),Version(restore),true,"Changed agency base"));
            reactivationBase=await CurrentBase();restore=await reactivation.Propose(actor,id,Key(),reactivationBase,"Replacement with current agency base");
            await using(var db=new BackOfficeDbContext(options))Assert.Equal("stale",await db.Set<AgencyStateRequest>().Where(x=>x.Id==staleRestore).Select(x=>x.State).SingleAsync());
            beforeReactivation=await ReactivationState();
            var failingReactivationFactory=new PooledDbContextFactory<BackOfficeDbContext>(new DbContextOptionsBuilder<BackOfficeDbContext>(options).AddInterceptors(new FailAfterReactivationInvitation()).Options);
            var failingReactivation=new AgencyReactivationService(drafts,assessment,new SqlCommandBoundary(failingReactivationFactory,clock),issuer,clock);var reactivationFailureKey=Key();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>failingReactivation.Decide(reviewer,id,restore.ResourceId,reactivationFailureKey,Version(restore),true,"Fictional rollback after fresh invitation"));
            Assert.Equal(beforeReactivation,await ReactivationState());
            await using(var db=new BackOfficeDbContext(options)){Assert.Equal("pending",await db.Set<AgencyStateRequest>().Where(x=>x.Id==restore.ResourceId).Select(x=>x.State).SingleAsync());Assert.False(await db.Set<IdempotencyRecord>().AnyAsync(x=>x.Key==reactivationFailureKey));}
            async Task<(ActorContext Actor,string Key,CommandOutcome? Result,int Status)> RestoreRace(ActorContext who){var commandKey=Key();try{var result=await reactivation.Decide(who,id,restore.ResourceId,commandKey,Version(restore),true,"Independent reactivation");return(who,commandKey,result,result.Status);}catch(AgencyCommandException ex){return(who,commandKey,null,ex.Status);}}
            var restoreRace=await Task.WhenAll(RestoreRace(reviewer),RestoreRace(other));Assert.Single(restoreRace,x=>x.Status==200);Assert.Single(restoreRace,x=>x.Status==412);var restoreWinner=restoreRace.Single(x=>x.Status==200);
            var restoredReplay=await reactivation.Decide(restoreWinner.Actor,id,restore.ResourceId,restoreWinner.Key,Version(restore),true,"Independent reactivation");Assert.True(restoredReplay.Replayed);Assert.Equal(restoreWinner.Result!.Etag,restoredReplay.Etag);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("active",await db.Set<Agency>().Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
                var invitationsAfter=await db.Set<AgencyInvitation>().Where(x=>x.AgencyId==id).ToListAsync();Assert.Equal(2,invitationsAfter.Count);
                var old=invitationsAfter.Single(x=>x.State=="revoked");var fresh=invitationsAfter.Single(x=>x.State=="pending");Assert.NotEqual(old.TokenHash,fresh.TokenHash);Assert.NotEqual(old.Id,fresh.Id);
                Assert.Equal("suspended",await db.Set<StaffUser>().Where(x=>x.Id==inactiveId).Select(x=>x.State).SingleAsync());
                Assert.Equal(previouslyRevokedAt,await db.Set<UserSession>().Where(x=>x.UserId==inactiveId).Select(x=>x.RevokedAt).SingleAsync());
                var brokerSession=await db.Set<UserSession>().SingleAsync(x=>x.UserId==broker);Assert.NotNull(brokerSession.RevokedAt);Assert.NotEqual(brokerSession.SecurityStamp,await db.Set<StaffUser>().Where(x=>x.Id==broker).Select(x=>x.SecurityStamp).SingleAsync());
                Assert.Equal(2,await db.Set<AgencyTermsVersion>().CountAsync(x=>x.AgencyId==id));Assert.Equal(2,await db.Set<AgencyFollowUp>().CountAsync(x=>x.AgencyId==id));Assert.Equal(3,await db.Set<AgencyNotificationReceipt>().CountAsync());Assert.Equal(4,await db.Set<AgencyNotification>().CountAsync(x=>x.AgencyId==id));
                var revokedReviewer=await db.Set<StaffUser>().SingleAsync(x=>x.Id==restoreWinner.Actor.UserId);revokedReviewer.State="suspended";await db.SaveChangesAsync();
            }
            await Status(403,()=>reactivation.Decide(restoreWinner.Actor,id,restore.ResourceId,restoreWinner.Key,Version(restore),true,"Independent reactivation"));
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
    private sealed class FixedTime(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
    private sealed class FailAfterReactivationInvitation:SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {if(data.Context!.ChangeTracker.Entries<AgencyActivity>().Any(x=>x.State==EntityState.Added&&x.Entity.Action=="agency.reactivated"))throw new InvalidOperationException("Injected failure after fresh reactivation invitation.");return ValueTask.FromResult(result);}
    }
    private sealed class FailAfterNotices:SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {if(data.Context!.ChangeTracker.Entries<AgencyFollowUp>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("Injected failure after activation notices.");return ValueTask.FromResult(result);}
    }
}
