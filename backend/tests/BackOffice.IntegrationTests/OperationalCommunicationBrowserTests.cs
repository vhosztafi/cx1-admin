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
    public Task RealSqlOperationalCommunicationBrowserMotor()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await RunCommunicationBrowser(db,password,f.Factory,f.Clock,f.Underwriter,f.QuoteId,"motor-trade");
    });
    [Fact]
    public Task RealSqlOperationalCommunicationBrowserCommercial()=>CommercialTermsScenario(stopAfterAccepted:true,inspectAccepted:async(db,password)=>
    {
        var cycle=await db.Set<UnderwritingCycle>().AsNoTracking().SingleAsync();var user=await db.Set<StaffUser>().AsNoTracking().SingleAsync(x=>x.Email=="senior-underwriter@cover.example");
        var f=await CommercialIssueCommand(db,cycle,cycle.CurrentAcceptanceId!.Value,user.Id);
        await f.Service.IssueAsync(f.Actor,f.Quote.Id,f.Quote.RowVersion,f.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        await RunCommunicationBrowser(db,password,f.Factory,new RatingClock(),f.Actor,f.Quote.Id,"commercial-combined");
    });
    private static async Task RunCommunicationBrowser(BackOfficeDbContext db,string password,IDbContextFactory<BackOfficeDbContext> factory,TimeProvider clock,ActorContext actor,Guid quoteId,string product)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;Assert.NotNull(root);
        var dist=OperationalBrowserBuild.Select(root.FullName,".local/next-phase9-10-browser");Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")),"Build the communication browser bundle first.");
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        await using(var transaction=await db.Database.BeginTransactionAsync()){await AgencyDemoSeed.SeedAsync(db);await transaction.CommitAsync();}
        var relationship=await db.Set<ClientAgencyRelationship>().SingleAsync(x=>x.Id==policy.RelationshipId);relationship.CreatedAt=clock.GetUtcNow();
        foreach(var contact in await db.Set<Contact>().Where(x=>x.RelationshipId==policy.RelationshipId&&x.EndedAt==null).ToArrayAsync())contact.CreatedAt=clock.GetUtcNow();await db.SaveChangesAsync();
        db.Add(new SettingVersion{Scope=MessageDeliveryService.WorkKind,Version=1,EffectiveFrom=clock.GetUtcNow().AddSeconds(-1),Values="{\"demo\":true,\"kind\":\"operational-delivery\",\"schemaVersion\":\"1\",\"scenario\":\"retry-required\"}"});await db.SaveChangesAsync();
        var boundary=new SqlCommandBoundary(factory,clock);var subject=await new TaskService(factory,boundary,clock).Register(actor,new("policy",policy.Id),"communication-browser-subject",default);
        var output=Path.Combine(root.FullName,".local/browser-evidence/communications",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var store=new OperationalFileStore(Path.Combine(output,"files"),[]);var renderer=new PolicyDocumentRenderService(factory,new PolicyDocumentRenderer());
        var documents=new DocumentService(factory,boundary,renderer,clock,new FileService(factory,boundary,store,clock));
        var template=await db.Set<PolicyDocumentRequest>().AsNoTracking().SingleAsync(x=>x.Kind=="policy-schedule");
        var generated=await documents.Generate(actor,subject.ResourceId,new("policy-schedule",new("policy-version",PolicyVersionId:version.Id),template.TemplateVersionId,"agency","Browser communication file",RelationshipId:policy.RelationshipId),"communication-browser-file",default);
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
            return Process.Start(info)??throw new InvalidOperationException("Communication browser did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try
        {
            using var probe=new HttpClient();var serving=false;
            for(var attempt=0;attempt<80&&!web.HasExited;attempt++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(serving,"Isolated communication web did not start.");
            var fixture=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,email,product,policyId=policy.Id,quoteId,agencyId=policy.AgencyId,clientId=policy.ClientId,relationshipId=policy.RelationshipId,subjectId=subject.ResourceId,versionId=file.Id,documentId=file.DocumentId});
            browser=Start(["scripts/verify-communication-browser.mjs","--worker"],new(){["COVER_COMMUNICATION_BROWSER_FIXTURE"]=fixture,["COVER_COMMUNICATION_BROWSER_PASSWORD"]=password});
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();var leases=new SqlJobLeases(factory,clock);var deliveryWorker=new MessageDeliveryWorker(factory,new FileService(factory,boundary,store,clock),clock);
            var deadline=DateTimeOffset.UtcNow.AddMinutes(8);var successScenario=false;
            while(!browser.HasExited&&DateTimeOffset.UtcNow<deadline)
            {
                await using var poll=await factory.CreateDbContextAsync();
                var pending=await poll.Set<OutboxWork>().AsNoTracking().Where(x=>x.Kind==MessageDeliveryService.WorkKind&&x.State=="pending").OrderBy(x=>x.CreatedAt).FirstOrDefaultAsync();
                if(pending is not null)
                {
                    // Only this owned fixture accelerates retry scheduling; the real
                    // lease/failure/provider/application paths remain unchanged.
                    await poll.Database.ExecuteSqlInterpolatedAsync($"UPDATE OutboxWork SET NextAttemptAt={clock.GetUtcNow()} WHERE Id={pending.Id} AND State=N'pending'");
                    var lease=await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind,pending.Id);
                    if(lease is not null)try
                    {
                        var result=await deliveryWorker.ExecuteProvider(lease);if(result is not null)await deliveryWorker.Apply(lease,result);
                        if(!successScenario)
                        {
                            poll.Add(new SettingVersion{Scope=MessageDeliveryService.WorkKind,Version=2,EffectiveFrom=clock.GetUtcNow().AddSeconds(-1),Values="{\"demo\":true,\"kind\":\"operational-delivery\",\"schemaVersion\":\"1\",\"scenario\":\"success\"}"});await poll.SaveChangesAsync();successScenario=true;
                        }
                    }
                    catch(MessageDeliveryException failure){await leases.FailAsync(lease,failure.Failure);}
                }
                await Task.Delay(150);
            }
            Assert.True(browser.HasExited,"Communication browser exceeded its acceptance deadline.");
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(browser.ExitCode==0,"Communication browser failed; inspect sanitized failure evidence.");
            Assert.Equal(4,await db.Set<InternalNote>().CountAsync(x=>x.Body.StartsWith("Browser ")));
            var message=await db.Set<OperationalMessageDraft>().AsNoTracking().SingleAsync();Assert.Equal("Browser revised agency draft",message.Body);Assert.Equal("sent",message.State);
            Assert.Equal(2,await db.Set<OperationalDelivery>().CountAsync(x=>x.State=="delivered"));
            Assert.Equal(2,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind==MessageDeliveryService.WorkKind));
            Assert.Equal(1,await db.Set<JobException>().CountAsync());
            Assert.Equal(file.Id,(await db.Set<MessageDraftAttachment>().SingleAsync()).DocumentVersionId);Assert.Single(await db.Set<MessageDraftRecipient>().ToArrayAsync());
            Assert.Equal(version.ContentHash,await db.Set<PolicyVersion>().Where(x=>x.Id==version.Id).Select(x=>x.ContentHash).SingleAsync());
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{passed=true,noteCount=4,message.Id,attachmentVersionId=file.Id}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-10-browser"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName,$".local/phase9-10-browser/{product}.json"),JsonSerializer.Serialize(new{output,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
        }
        finally
        {
            if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
        }
    }
}
