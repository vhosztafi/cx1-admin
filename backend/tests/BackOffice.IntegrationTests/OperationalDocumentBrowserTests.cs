using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackOffice.Application;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Platform;
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
    [Theory]
    [InlineData("motor-trade-road-risks")]
    [InlineData("motor-trade-combined")]
    public Task RealSqlOperationalDocumentBrowser(string product)=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password,product);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await RunDocumentBrowser(db,password,f.Factory,f.Clock,f.Underwriter,f.QuoteId,product);
    });

    [Fact]
    public Task RealSqlOperationalDocumentBrowserCommercial()=>CommercialTermsScenario(stopAfterAccepted:true,inspectAccepted:async(db,password)=>
    {
        var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();
        var senior=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
        var f=await CommercialIssueCommand(db,cycle,cycle.CurrentAcceptanceId!.Value,senior.Id);
        await f.Service.IssueAsync(f.Actor,f.Quote.Id,f.Quote.RowVersion,f.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await RunDocumentBrowser(db,password,f.Factory,new RatingClock(),f.Actor,f.Quote.Id,"commercial-combined");
    });

    [Fact]
    public Task RealSqlOperationalDocumentBrowserRenewal()=>VerifyRenewalLifecycle("motor-trade-road-risks",false,false,onPreparedBrowser:async(db,password,f,terms)=>
        await RunDocumentBrowser(db,password,f.Factory,f.Clock,f.Underwriter,f.QuoteId,"renewal",terms));

    private static async Task RunDocumentBrowser(BackOfficeDbContext db,string password,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,ActorContext actor,Guid quoteId,string product,ServicingTermsVersion? servicingTerms=null)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;
        Assert.NotNull(root);const string dist=".local/next-phase9-08-browser";
        Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")),"Build the isolated document browser bundle first.");
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var policy=await db.Set<Policy>().AsNoTracking().SingleAsync(x=>x.Id==version.PolicyId);
        // Quote/issue helpers add their agency after the initial demo seed.
        // Run the missing-only onboarding seed for this isolated fixture.
        await using(var seed=await db.Database.BeginTransactionAsync()){await AgencyDemoSeed.SeedAsync(db);await seed.CommitAsync();}
        var agencyId=policy.AgencyId;
        // The retained workflow clock precedes wall-clock-created helper rows;
        // paged relationship reads correctly exclude rows after their as-of.
        var relationship=await db.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==policy.RelationshipId);
        relationship.CreatedAt=clock.GetUtcNow();await db.SaveChangesAsync();
        var tasks=new TaskService(factory,new SqlCommandBoundary(factory,clock),clock);
        var subject=await tasks.Register(actor,new("policy",policy.Id),"browser-document-policy",default);
        var task=await tasks.Create(actor,subject.ResourceId,new("servicing","Review browser document evidence","normal",new("unassigned"),null),"browser-document-task",default);
        var email=await db.Set<StaffUser>().Where(x=>x.Id==actor.UserId).Select(x=>x.Email).SingleAsync();
        var output=Path.Combine(root.FullName,".local/browser-evidence/documents",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var store=new OperationalFileStore(Path.Combine(output,"files"),[]);
        var boundary=new SqlCommandBoundary(factory,clock);
        var documents=new DocumentService(factory,boundary,new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer()),clock,new FileService(factory,boundary,store,clock));
        var template=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.VersionId==version.Id&&x.Kind=="policy-schedule");
        var pending=await documents.Generate(actor,subject.ResourceId,new("policy-schedule",new("policy-version",PolicyVersionId:version.Id),template.TemplateVersionId,"internal","Browser scheduled pending document"),"browser-pending-document",default);
        var pendingVersion=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==pending.ResourceId);
        var pendingWork=await db.Set<OutboxWork>().SingleAsync(x=>x.Id==pendingVersion.WorkId);pendingWork.NextAttemptAt=clock.GetUtcNow().AddYears(1);await db.SaveChangesAsync();
        var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync(x=>x.QuoteId==quoteId&&x.CurrentAcceptanceId!=null);
        var acceptance=await db.Set<QuoteAcceptance>().AsNoTracking().SingleAsync(x=>x.Id==cycle.CurrentAcceptanceId);
        var terms=await db.Set<QuoteTermsVersion>().AsNoTracking().SingleAsync(x=>x.Id==acceptance.TermsVersionId);
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",Path.Combine(output,"files"))
            .UseSetting("Cover:DataProtectionPath",Path.Combine(output,"keys")).UseSetting("Cover:FileWorkerEnabled","true")
            .UseSetting("Cover:DocumentWorkerEnabled","true").UseSetting("Cover:WorkflowTaskWorkerEnabled","false")
            .ConfigureServices(services=>services.AddSingleton<TimeProvider>(clock)));
        host.UseKestrel(0);using var client=host.CreateClient();
        var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);Assert.NotEqual(5000,new Uri(api).Port);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var webOrigin=$"http://127.0.0.1:{port}";
        Process Start(IEnumerable<string> args,Dictionary<string,string> env)
        {
            var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)info.ArgumentList.Add(arg);foreach(var (key,value) in env)info.Environment[key]=value;
            return Process.Start(info)??throw new InvalidOperationException("Document browser process did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try
        {
            using var probe=new HttpClient();var serving=false;
            for(var attempt=0;attempt<80&&!web.HasExited;attempt++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(serving,"Isolated document preview did not start.");
            var fixture=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,product,email,policyId=version.PolicyId,versionId=version.Id,termId=version.TermId,taskId=task.ResourceId,quoteId,agencyId,clientId=policy.ClientId,pendingVersionId=pendingVersion.Id,pendingDocumentId=pendingVersion.DocumentId,quoteTermsId=terms.Id,quoteRevisionId=cycle.QuoteRevisionId,servicingDraftId=servicingTerms?.DraftId,servicingTermsId=servicingTerms?.Id});
            browser=Start(["scripts/verify-document-browser.mjs","--worker"],new(){["COVER_DOCUMENT_BROWSER_FIXTURE"]=fixture,["COVER_DOCUMENT_BROWSER_PASSWORD"]=password});
            var browserOut=browser.StandardOutput.ReadToEndAsync();var browserErr=browser.StandardError.ReadToEndAsync();await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(8));
            var errors=await browserErr;await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await browserOut+errors);Assert.True(browser.ExitCode==0,errors);
            var generated=await db.Set<DocumentVersion>().AsNoTracking().Where(x=>x.Reason=="Browser historical schedule").SingleAsync();
            Assert.Equal(version.Id,generated.PolicyVersionId);
            var versions=await db.Set<DocumentVersion>().AsNoTracking().Where(x=>x.DocumentId==generated.DocumentId).OrderBy(x=>x.Number).ToArrayAsync();Assert.Equal(2,versions.Length);
            Assert.Equal("Browser replacement schedule",versions[1].Reason);
            Assert.Equal(2,await db.Set<DocumentVersion>().CountAsync(x=>x.Reason=="Browser original evidence"||x.Reason=="Browser replacement evidence"));
            Assert.Equal(version.ContentHash,await db.Set<PolicyVersion>().Where(x=>x.Id==version.Id).Select(x=>x.ContentHash).SingleAsync());
            var attachment=await db.Set<TaskDocumentAttachment>().AsNoTracking().SingleAsync(x=>x.TaskId==task.ResourceId);
            Assert.Equal(versions[1].Id,attachment.DocumentVersionId);Assert.NotNull(attachment.RemovedAt);Assert.Equal("Browser remove exact link",attachment.RemovalReason);
            foreach(var reason in new[]{"Browser quote evidence","Browser agency evidence","Browser relationship evidence"})Assert.Equal(1,await db.Set<DocumentVersion>().CountAsync(x=>x.Reason==reason));
            var quotation=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Reason=="Browser exact quotation terms");Assert.Equal(terms.Id,quotation.QuoteTermsVersionId);Assert.Equal(cycle.QuoteRevisionId,quotation.QuoteRevisionId);
            if(servicingTerms is not null)Assert.Equal(servicingTerms.Id,(await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Reason=="Browser exact renewal terms")).ServicingTermsVersionId);
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{passed=true,generated.Id,generated.DocumentId,generated.PolicyVersionId,versionCount=versions.Length}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-08-browser"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName,".local/phase9-08-browser/latest.json"),JsonSerializer.Serialize(new{output,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
            await File.WriteAllTextAsync(Path.Combine(root.FullName,$".local/phase9-08-browser/{product}.json"),JsonSerializer.Serialize(new{output,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
        }
        finally
        {
            if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
        }
    }
}
