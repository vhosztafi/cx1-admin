using System.Text.Json;
using BackOffice.Application;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Administration;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Parties;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;
public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlConfigurationSuccessorsValidateConsumeAndPreservePriorVersions()
    {
        await WithDatabase(async(db,password)=>
        {
            await DemoDatabase.SeedAsync(db,password,includeQuoteCapture:true,includeUnderwriting:true,includeMatches:true);
            await using(var tx=await db.Database.BeginTransactionAsync()){await WorkflowTaskSeed.SeedAsync(db);await tx.CommitAsync();}
            var admin=await db.Set<StaffUser>().SingleAsync(x=>x.Email=="system-admin@cover.example");
            var actor=new ActorContext(admin.Id,admin.TeamId,null,new HashSet<string>{"system-admin"});
            var factory=new AdministrationFactory(db.Database.GetConnectionString()!);var clock=new AdministrationClock(Now);
            var service=new ConfigurationAdministration(factory,new SqlCommandBoundary(factory,clock),new PolicyDocumentRenderer(),clock);
            var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var original=new OrganisationConfiguration("Fictional office","ZX",false,"Configured signature");
            var input=new ConfigurationEdit("organisation",JsonSerializer.SerializeToElement(original,options),Now,"Configure future work");
            var key=Guid.NewGuid().ToString("N");var saved=await service.SaveAsync(actor,input,"\"new\"",key);
            Assert.Equal(saved.ResourceId,(await service.SaveAsync(actor,input,"\"new\"",key)).ResourceId);
            Assert.Equal(412,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(actor,input,"\"new\"",Guid.NewGuid().ToString("N")))).Status);
            Assert.Equal(original,await AdministrativeConfiguration.Organisation(db,Now));
            await using(var tx=await db.Database.BeginTransactionAsync()){Assert.StartsWith("ZX-",await ClientReferences.NextAsync(db));await tx.CommitAsync();}
            Assert.Equal(400,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(actor,input with{Scope="unknown"},"\"new\"",Guid.NewGuid().ToString("N")))).Status);
            var flag=new ConfigurationEdit("flag-definition/vulnerability",JsonSerializer.SerializeToElement(new FlagConfiguration(false,30,false),options),Now,"Disable new declarations");
            await service.SaveAsync(actor,flag,"\"new\"",Guid.NewGuid().ToString("N"));
            await Assert.ThrowsAsync<SupportFlagOperationException>(()=>AdministrativeConfiguration.DemandFlag(db,"vulnerability",DateOnly.FromDateTime(Now.UtcDateTime).AddDays(5),false,Now,default));
            var source=await db.Set<SettingVersion>().SingleAsync(x=>x.Scope=="workflow-task/demo-referral-review");var before=source.Values;
            var rule=WorkflowTaskRules.Parse(source.Values,source.Scope) with {Title="Configured referral review",AssignmentTeamId=admin.TeamId};
            var next=await service.SaveAsync(actor,new(source.Scope,JsonSerializer.SerializeToElement(rule,options),Now.AddDays(1),"Future workflow"),$"\"{source.Id:N}-{source.Version}\"",Guid.NewGuid().ToString("N"));
            Assert.Equal(source.Id,(await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope==source.Scope&&x.EffectiveFrom<=Now).OrderByDescending(x=>x.Version).FirstAsync()).Id);
            Assert.Equal(next.ResourceId,(await db.Set<SettingVersion>().AsNoTracking().Where(x=>x.Scope==source.Scope&&x.EffectiveFrom<=Now.AddDays(1)).OrderByDescending(x=>x.Version).FirstAsync()).Id);
            Assert.Equal(before,(await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==source.Id)).Values);
            var matching=await db.Set<SettingVersion>().OrderByDescending(x=>x.Version).FirstAsync(x=>x.Scope=="matching-rule");
            var matchingRule=JsonSerializer.Deserialize<BackOffice.Application.Parties.MatchRuleSnapshot>(matching.Values,options)! with{BrokerOfRecordDays=60};
            var matchingClock=new AdministrationClock(matching.EffectiveFrom>Now?matching.EffectiveFrom:Now);
            var matchingService=new ConfigurationAdministration(factory,new SqlCommandBoundary(factory,matchingClock),new PolicyDocumentRenderer(),matchingClock);
            var matchingSaved=await matchingService.SaveAsync(actor,new(matching.Scope,JsonSerializer.SerializeToElement(matchingRule,options),matchingClock.GetUtcNow(),"Configure protection evidence"),$"\"{matching.Id:N}-{matching.Version}\"",Guid.NewGuid().ToString("N"));
            var pinned=await db.Set<SettingVersion>().AsNoTracking().SingleAsync(x=>x.Id==matchingSaved.ResourceId);Assert.Equal(60,JsonSerializer.Deserialize<BackOffice.Application.Parties.MatchRuleSnapshot>(pinned.Values,options)!.BrokerOfRecordDays);
            var template=await db.Set<TemplateVersion>().FirstAsync();var templateBefore=template.ContentJson;
            var edit=new TemplateEdit("Fictional revised heading","Fictional revised notice",Now,Now.AddYears(3),"Publish safe text");
            var preview=await service.PreviewAsync(actor,template.Id,edit);Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(preview,0,4));
            var successor=await service.TemplateAsync(actor,template.Id,edit,$"\"{template.Id:N}-{template.Version}\"",Guid.NewGuid().ToString("N"));
            Assert.NotEqual(template.Id,successor.ResourceId);Assert.Equal(templateBefore,(await db.Set<TemplateVersion>().AsNoTracking().SingleAsync(x=>x.Id==template.Id)).ContentJson);
            await Assert.ThrowsAsync<QuoteOperationException>(()=>service.PreviewAsync(actor,template.Id,edit with{Notice="<script>unsafe</script>"}));
            var assignments=await db.Set<UserRole>().Where(x=>x.UserId==admin.Id).ToArrayAsync();db.RemoveRange(assignments);await db.SaveChangesAsync();
            Assert.Equal(403,(await Assert.ThrowsAsync<QuoteOperationException>(()=>service.SaveAsync(actor,input,"\"new\"",key))).Status);
        });
    }
}
