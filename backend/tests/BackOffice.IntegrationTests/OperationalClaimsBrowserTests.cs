using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalClaimsBrowserMotor()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await RunClaimsBrowser(db,password,f.Factory,f.Clock,f.Underwriter,f.QuoteId,"motor-trade");
    });
    [Fact]
    public Task RealSqlOperationalClaimsBrowserCommercial()
    {
        IDbContextFactory<BackOfficeDbContext>? retainedFactory=null;ActorContext? retainedActor=null;TimeProvider? retainedClock=null;
        return CommercialServicingIssueScenario(false,localTime:"12:00",onIssued:(db,factory,actor,clock)=>{retainedFactory=factory;retainedActor=actor;retainedClock=clock;return Task.CompletedTask;},inspectIssued:async(db,password)=>
        {
            Assert.NotNull(retainedFactory);Assert.NotNull(retainedActor);Assert.NotNull(retainedClock);
            var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
            await RunClaimsBrowser(db,password,retainedFactory,retainedClock,retainedActor,policy.SourceQuoteId,"commercial-combined");
        });
    }
    private static async Task RunClaimsBrowser(BackOfficeDbContext db,string password,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,ActorContext actor,Guid quoteId,string product)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;Assert.NotNull(root);
        const string dist=".local/next-phase9-13-browser";Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")),"Build the incident browser bundle first.");
        var version=await db.Set<PolicyVersion>().AsNoTracking().OrderBy(x=>x.ProcessedAt).ThenBy(x=>x.EffectiveAt).FirstAsync();var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        clock=new RatingClock{Current=term.EndsAt.AddDays(2)};
        var occurrenceDay=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(version.EffectiveAt,TimeZoneInfo.FindSystemTimeZoneById("Europe/London")).DateTime).AddDays(1).ToString("yyyy-MM-dd");
        await using(var seed=await db.Database.BeginTransactionAsync()){await OperationalClaimsSeed.Seed(db);await seed.CommitAsync();}
        var boundary=new SqlCommandBoundary(factory,clock);var subject=await new TaskService(factory,boundary,clock).Register(actor,new("policy",policy.Id),"communication-browser-subject",default);
        var output=Path.Combine(root.FullName,".local/browser-evidence/claims",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var store=new OperationalFileStore(Path.Combine(output,"files"),[]);var renderer=new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer());
        var documents=new DocumentService(factory,boundary,renderer,clock,new FileService(factory,boundary,store,clock));
        var template=await db.Set<PolicyDocumentRequest>().AsNoTracking().FirstAsync(x=>x.Kind=="policy-schedule");
        var generated=await documents.Generate(actor,subject.ResourceId,new("policy-schedule",new("policy-version",PolicyVersionId:version.Id),template.TemplateVersionId,"insurer","Browser claims file"),"communication-browser-file",default);
        var file=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==generated.ResourceId);await new DocumentGenerationWorker(factory,renderer,store,clock).Process(file.WorkId,default);
        var email=await db.Set<StaffUser>().Where(x=>x.Id==actor.UserId).Select(x=>x.Email).SingleAsync();
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",Path.Combine(output,"files"))
            .UseSetting("Cover:DataProtectionPath",Path.Combine(output,"keys")).UseSetting("Cover:FileWorkerEnabled","false").UseSetting("Cover:DocumentWorkerEnabled","false")
            .UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:WorkflowTaskWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false").ConfigureServices(services=>services.AddSingleton<TimeProvider>(clock)));
        host.UseKestrel(0);using var client=host.CreateClient();var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);Assert.NotEqual(5000,new Uri(api).Port);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var webOrigin=$"http://127.0.0.1:{port}";
        Process Start(IEnumerable<string> args,Dictionary<string,string> env)
        {
            var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)info.ArgumentList.Add(arg);foreach(var (key,value) in env)info.Environment[key]=value;
            return Process.Start(info)??throw new InvalidOperationException("Incident browser did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try
        {
            using var probe=new HttpClient();var serving=false;
            for(var attempt=0;attempt<80&&!web.HasExited;attempt++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(serving,"Isolated incident web did not start.");
            var fixture=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,email,product,policyId=policy.Id,quoteId,agencyId=policy.AgencyId,clientId=policy.ClientId,relationshipId=policy.RelationshipId,subjectId=subject.ResourceId,versionId=file.Id,documentId=file.DocumentId,occurrenceDay});
            browser=Start(["scripts/verify-claims-browser.mjs","--worker"],new(){["COVER_CLAIMS_BROWSER_FIXTURE"]=fixture,["COVER_CLAIMS_BROWSER_PASSWORD"]=password});
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(7));await browser.WaitForExitAsync(deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(browser.ExitCode==0,"Incident browser failed; inspect sanitized failure evidence.");
            var incident=await db.Set<OperationalIncident>().AsNoTracking().SingleAsync();Assert.Equal("handed-off",incident.State);
            Assert.True(await db.Set<IncidentRevision>().CountAsync()>=4);Assert.True(await db.Set<IncidentOccurrenceRecord>().CountAsync()>=1);
            Assert.Equal(file.Id,(await db.Set<IncidentEvidence>().Where(x=>x.RevisionId==incident.CurrentRevisionId).SingleAsync()).DocumentVersionId);
            var handoff=await db.Set<ClaimsHandoff>().AsNoTracking().SingleAsync();Assert.Equal("acknowledged",handoff.State);Assert.Equal(version.Id,handoff.SourceVersionId);Assert.Equal(incident.CurrentRevisionId,handoff.RevisionId);
            Assert.Equal(2,await db.Set<ClaimsSummary>().CountAsync());Assert.Equal(3,await db.Set<ClaimsRequest>().CountAsync());Assert.Equal(3,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==ClaimsHandoffService.WorkKind));
            using var submitted=JsonDocument.Parse(handoff.RequestJson);Assert.Equal(file.Id,submitted.RootElement.GetProperty("evidence")[0].GetProperty("versionId").GetGuid());
            Assert.Equal(0,submitted.RootElement.GetProperty("withheldEvidenceCount").GetInt32());
            var expectedSource=Convert.ToHexStringLower(version.ContentHash);Assert.Equal(expectedSource,submitted.RootElement.GetProperty("sourceHash").GetString());
            Assert.Equal(version.ContentHash,await db.Set<PolicyVersion>().Where(x=>x.Id==version.Id).Select(x=>x.ContentHash).SingleAsync());
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{passed=true,incident.Id,state=incident.State,evidenceVersionId=file.Id}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-13-browser"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName,$".local/phase9-13-browser/{product}.json"),JsonSerializer.Serialize(new{output,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
        }
        finally
        {
            if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
        }
    }
}
