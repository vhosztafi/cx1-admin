using System.Security.Cryptography;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Parties;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class MatchStorageTests
{
    [Fact]
    public async Task RealSqlMatchEvidenceIsImmutableAndAssociationCannotCrossTheSubmissionAgency()
    {
        var connection=new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("COVER_SQL_TEST_CONNECTION") ?? DemoDatabase.DefaultConnection);
        var ownedName="CoverMGA_Test_"+Guid.NewGuid().ToString("N");connection.InitialCatalog=ownedName;connection.AttachDBFilename="";
        var options=new DbContextOptionsBuilder<BackOfficeDbContext>().UseSqlServer(connection.ConnectionString,sql=>sql.UseCompatibilityLevel(160)).Options;
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);Guid actor=default;
        var rule=new SettingVersion {Scope="matching-rule",Version=2,Values="{\"requireReview\":true}"};
        var identity=new ClientWrite("Fictional Match Intake","sole-trader",new("1 Fictional Road","Sheffield","S1 1AA","GB"));
        var evidence=MatchRules.ValidateEvidence(identity,[new("legal-name","Fictional submitted comparison","Fictional Match Intake","Fictional Demo Traders 03","strong","near-match")],new(rule.Id,2,"refer",true,"Fictional rule; staff review required."),"high");
        var intake=new MatchSubmission {Reference="MI-FICTIONAL-001",AgencyId=PartyDemoSeed.SecondAgencyId,IdentitySnapshot=JsonSerializer.Serialize(evidence.Identity,json)};
        var review=new MatchReview {SubmissionId=intake.Id,CandidateClientId=PartyDemoSeed.ClientId(3),CandidateRelationshipId=PartyDemoSeed.RelationshipId(3,1),RuleVersionId=rule.Id,RuleSnapshot=JsonSerializer.Serialize(evidence.Rule,json),Signals=JsonSerializer.Serialize(evidence.Signals,json),Confidence=evidence.Confidence};
        try
        {
            await using(var db=new BackOfficeDbContext(options))
            {
                await db.Database.MigrateAsync();await DemoDatabase.SeedAsync(db,"Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1");
                actor=await db.Set<StaffUser>().Where(x=>x.Email=="underwriter@cover.example").Select(x=>x.Id).SingleAsync();
                db.AddRange(rule,intake,review);await db.SaveChangesAsync();Assert.False(db.Database.HasPendingModelChanges());
                var request=new MatchInformationRequest {MatchId=review.Id,ActorId=actor,Description="Fictional request for identity clarification"};
                db.Add(request);db.Add(new MatchDecision {MatchId=review.Id,Outcome="query",Reason="Fictional comparison needs confirmation",ActorId=actor,InformationRequestId=request.Id});
                review.State="queried";await db.SaveChangesAsync();
            }
            await using(var db=new BackOfficeDbContext(options))
            {
                var staff=new MatchScope(new ActorContext(actor,null,null,new HashSet<string>{"underwriter"}));
                Assert.Single(await staff.Reviews(db).ToListAsync());Assert.Single(await staff.Decisions(db).ToListAsync());
                Assert.Equal("recorded",(await staff.InformationRequests(db).SingleAsync()).DeliveryState);
                foreach(var role in new[] {"servicing","agency-admin","finance","system-admin"})
                {
                    var denied=new MatchScope(new ActorContext(actor,null,null,new HashSet<string>{role}));
                    Assert.Empty(await denied.Reviews(db).ToListAsync());Assert.Empty(await denied.Decisions(db).ToListAsync());Assert.Empty(await denied.InformationRequests(db).ToListAsync());
                }
                foreach(var agency in new[] {PartyDemoSeed.FirstAgencyId,PartyDemoSeed.SecondAgencyId})
                    Assert.Empty(await new MatchScope(new ActorContext(actor,null,agency,new HashSet<string>{"agency-admin","underwriter"})).Reviews(db).ToListAsync());
            }
            await SqlReject("UPDATE [MatchSubmission] SET [IdentitySnapshot]='{}'",51006);
            await SqlReject("DELETE FROM [MatchSubmission]",547,51006);
            await SqlReject("UPDATE [MatchReview] SET [Signals]='[{\"changed\":true}]'",51007);
            await SqlReject("UPDATE [MatchReview] SET [CandidateRelationshipId]='33000000-0000-4000-8000-000000000032'",51007);
            await SqlReject("DELETE FROM [MatchReview]",547,51007);
            await SqlReject("UPDATE [MatchDecision] SET [Reason]='tampered'",51008);await SqlReject("DELETE FROM [MatchDecision]",51008);
            await SqlReject("UPDATE [MatchInformationRequest] SET [Description]='tampered'",51009);await SqlReject("DELETE FROM [MatchInformationRequest]",547,51009);
            await Reject(async db=>{var row=await db.Set<MatchSubmission>().SingleAsync();row.LinkedClientId=review.CandidateClientId;row.LinkedRelationshipId=review.CandidateRelationshipId;},547);
            await Reject(async db=>{var row=await db.Set<MatchSubmission>().SingleAsync();row.LinkedClientId=review.CandidateClientId;},547);
            await Reject(db=>{db.Add(new MatchSubmission {Reference=intake.Reference,AgencyId=intake.AgencyId,IdentitySnapshot="{}"});return Task.CompletedTask;},2601,2627);
            await Reject(db=>{db.Add(new MatchSubmission {Reference="BAD-JSON",AgencyId=intake.AgencyId,IdentitySnapshot="not-json"});return Task.CompletedTask;},547);
            await Reject(db=>{db.Add(new MatchDecision {MatchId=review.Id,ActorId=actor,Outcome="link",Reason="No association"});return Task.CompletedTask;},547);
            await Reject(db=>{db.Add(new MatchInformationRequest {MatchId=review.Id,ActorId=actor,Description="Not actually sent",DeliveryState="delivered"});return Task.CompletedTask;},547);
            await RejectReview(row=>row.CandidateRelationshipId=PartyDemoSeed.RelationshipId(1,1),547);
            await RejectReview(row=>row.RuleSnapshot=JsonSerializer.Serialize(evidence.Rule with {Version=1},json),51007);
            await RejectReview(row=>row.RuleSnapshot=JsonSerializer.Serialize(evidence.Rule with {Id=Guid.NewGuid()},json),547);
            await RejectReview(row=>row.Signals="[]",51007);
            await RejectReview(row=>row.Signals=JsonSerializer.Serialize(Enumerable.Repeat(evidence.Signals[0],101),json),51007);
            await using(var db=new BackOfficeDbContext(options))
            {
                var row=await db.Set<MatchSubmission>().SingleAsync();row.LinkedClientId=review.CandidateClientId;row.LinkedRelationshipId=PartyDemoSeed.RelationshipId(3,2);row.SeparateClientId=PartyDemoSeed.ClientId(4);await db.SaveChangesAsync();
            }
            await SqlReject("UPDATE [MatchSubmission] SET [SeparateClientId]=NULL",51006);
            await using(var first=new BackOfficeDbContext(options))
            await using(var second=new BackOfficeDbContext(options))
            {
                var current=await first.Set<MatchReview>().SingleAsync();var stale=await second.Set<MatchReview>().SingleAsync();
                current.State="declined";await first.SaveChangesAsync();stale.State="linked";await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>second.SaveChangesAsync());
            }
            await using(var db=new BackOfficeDbContext(options))
            {
                Assert.Equal(review.Signals,(await db.Set<MatchReview>().SingleAsync()).Signals);
                Assert.Equal(intake.IdentitySnapshot,(await db.Set<MatchSubmission>().SingleAsync()).IdentitySnapshot);
                Assert.Equal(3,await db.Set<Contact>().CountAsync());Assert.Empty(await db.Set<FlagVisibility>().ToListAsync());
                Assert.Single(await db.Set<MatchDecision>().ToListAsync());
                var seedPassword="Demo!"+Convert.ToHexString(RandomNumberGenerator.GetBytes(24))+"a1";
                await DemoDatabase.SeedAsync(db,seedPassword,includeMatches:true);
                Assert.Equal(7,await db.Set<MatchReview>().CountAsync());Assert.Equal(3,await db.Set<MatchDecision>().CountAsync());
                var seeded=await db.Set<MatchReview>().SingleAsync(x=>x.Id==MatchDemoSeed.ReviewId(5));Assert.Equal("declined",seeded.State);var captured=seeded.Signals;
                seeded.State="pending";await db.SaveChangesAsync();
                await DemoDatabase.SeedAsync(db,seedPassword,includeMatches:true);
                Assert.Equal(7,await db.Set<MatchSubmission>().CountAsync());Assert.Equal(3,await db.Set<MatchDecision>().CountAsync());
                Assert.Equal("pending",(await db.Set<MatchReview>().AsNoTracking().SingleAsync(x=>x.Id==seeded.Id)).State);Assert.Equal(captured,seeded.Signals);
            }
        }
        finally {if(connection.InitialCatalog!=ownedName)throw new InvalidOperationException("Test cleanup target changed.");await using var db=new BackOfficeDbContext(options);await db.Database.EnsureDeletedAsync();}
        async Task SqlReject(string sql,params int[] codes){await using var db=new BackOfficeDbContext(options);var error=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync(sql.Replace("{","{{").Replace("}","}}")));Assert.Contains(error.Number,codes);}
        async Task Reject(Func<BackOfficeDbContext,Task> change,params int[] codes){await using var db=new BackOfficeDbContext(options);await change(db);var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number,codes);}
        Task RejectReview(Action<MatchReview> change,params int[] codes)=>Reject(db=>
        {
            var parent=new MatchSubmission {Reference=Guid.NewGuid().ToString("N"),AgencyId=intake.AgencyId,IdentitySnapshot=intake.IdentitySnapshot};
            var row=new MatchReview {SubmissionId=parent.Id,CandidateClientId=review.CandidateClientId,CandidateRelationshipId=review.CandidateRelationshipId,RuleVersionId=rule.Id,RuleSnapshot=review.RuleSnapshot,Signals=review.Signals};
            change(row);db.AddRange(parent,row);return Task.CompletedTask;
        },codes);
    }
}
