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
namespace BackOffice.IntegrationTests;
public sealed class AgencyActivationServiceTests
{
    [Fact]public async Task RealSqlActivationProposalRequiresCompleteEvidenceAndDoesNotActivateOrDeliver()
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
            Assert.True((await activation.Propose(actor,id,proposalKey,basis,"Request independent activation review")).Replayed);
            await Status(409,()=>activation.Propose(actor,id,Key(),basis,"Duplicate live request"));
            await Status(403,()=>activation.Propose(actor with{Roles=new HashSet<string>{"underwriter"}},id,Key(),basis,"Wrong authority"));
            await using(var db=new BackOfficeDbContext(options))
            {
                var agency=await db.Set<Agency>().SingleAsync(x=>x.Id==id);Assert.Equal("draft",agency.State);Assert.Equal(basis,agency.RowVersion);
                var row=await db.Set<AgencyStateRequest>().SingleAsync(x=>x.Id==request.ResourceId);Assert.Equal("pending",row.State);Assert.Equal(actor.UserId,row.RequestedBy);Assert.Equal(64,row.ProposedInputFingerprint.Length);Assert.Equal(basis,row.BaseVersion);
                Assert.False(await db.Set<AgencyTermsVersion>().AnyAsync(x=>x.AgencyId==id));Assert.False(await db.Set<AgencyNotification>().AnyAsync(x=>x.AgencyId==id));
                var invite=await db.Set<AgencyInvitation>().SingleAsync(x=>x.AgencyId==id);Assert.Equal("staged",invite.State);Assert.Null(invite.TokenHash);Assert.Null(invite.IssuedAt);
            }
            saved=await drafts.Save(actor,id,Key(),basis,normalized,6,null,default);var changedBase=Version(saved);
            await Status(412,()=>activation.Propose(actor,id,Key(),basis,"Old base"));
            async Task<int> Race(){try{await activation.Propose(actor,id,Key(),changedBase,"Replace stale activation request");return 202;}catch(AgencyCommandException failure){return failure.Status;}}
            var race=await Task.WhenAll(Race(),Race());Assert.Single(race,x=>x==202);Assert.Single(race,x=>x==409);
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal("stale",(await db.Set<AgencyStateRequest>().SingleAsync(x=>x.Id==request.ResourceId)).State);
                Assert.Equal(1,await db.Set<AgencyStateRequest>().CountAsync(x=>x.AgencyId==id&&x.State=="pending"));
                var rule=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="agency-compliance");db.Add(new SettingVersion{Scope=rule.Scope,Version=2,EffectiveFrom=clock.GetUtcNow(),Values=rule.Values});await db.SaveChangesAsync();
            }
            await Status(422,()=>activation.Propose(actor,id,Key(),changedBase,"Stale evidence rule"));
            // Original committed receipt is not re-executed after later rule changes.
            Assert.True((await activation.Propose(actor,id,proposalKey,basis,"Request independent activation review")).Replayed);
        }
        finally{if(connection.InitialCatalog!=owned)throw new InvalidOperationException("Cleanup target changed.");await using var cleanup=new BackOfficeDbContext(options);await cleanup.Database.EnsureDeletedAsync();}
    }
}
