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
    public Task RealSqlOperationalMidBrowserRecoversOneFailedSubmission()=>WithDatabase(async(db,password)=>
    {
        var setup=await AcceptedIssue(db,password);var f=setup.Source;
        await new QuoteIssueService(f.Factory,f.Clock).IssueAsync(f.Underwriter,f.QuoteId,setup.Version,setup.Input,Guid.NewGuid().ToString(),Guid.NewGuid());
        var term=await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();f.Clock.Current=term.StartsAt.AddDays(2);
        await RunMidBrowser(db,password,f.Factory,f.Clock,f.Underwriter);
    });
    private static async Task RunMidBrowser(BackOfficeDbContext db,string password,IDbContextFactory<BackOfficeDbContext> factory,RatingClock clock,ActorContext actor)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"package.json")))root=root.Parent;Assert.NotNull(root);
        const string dist=".local/next-phase9-14-browser";Assert.True(File.Exists(Path.Combine(root.FullName,"apps/backoffice",dist,"BUILD_ID")),"Build the MID browser bundle first.");
        var version=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();var policy=await db.Set<Policy>().AsNoTracking().SingleAsync();
        await using(var tx=await db.Database.BeginTransactionAsync()){await OperationalMidSeed.Seed(db);await tx.CommitAsync();}
        db.Add(new SettingVersion{Scope=OperationalMidSeed.Scope,Version=2,EffectiveFrom=clock.Current,Values="{\"demo\":true,\"kind\":\"operational-mid\",\"schemaVersion\":\"2\",\"scenario\":\"retry-required\"}"});await db.SaveChangesAsync();
        var intent=await db.Set<PolicyMidIntent>().AsNoTracking().SingleAsync();await new MidSubmissionRegistration(factory,clock).Register(intent.WorkId);
        var leases=new SqlJobLeases(factory,clock);var worker=new MidSubmissionWorker(factory,clock);
        for(var n=0;n<6;n++)
        {
            var lease=await leases.ClaimWorkAsync("mid-update",intent.WorkId);Assert.NotNull(lease);var failure=await Assert.ThrowsAsync<MidWorkerException>(()=>worker.ExecuteProvider(lease));Assert.Equal(JobFailure.ProviderUnavailable,failure.Failure);Assert.True(await leases.FailAsync(lease,failure.Failure));
            var current=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId);clock.Current=current.NextAttemptAt>clock.Current?current.NextAttemptAt.AddSeconds(1):clock.Current.AddSeconds(1);
        }
        var work=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==intent.WorkId);Assert.Equal("failed",work.State);var submission=await db.Set<MidSubmission>().AsNoTracking().SingleAsync();
        var exception=await db.Set<JobException>().AsNoTracking().SingleAsync(x=>x.WorkId==work.Id);var boundary=new SqlCommandBoundary(factory,clock);
        var rule=await WorkflowRule(db,clock.Current,"mid-browser-failure","job-exception","data-exception");var workflows=new WorkflowTaskService(factory,new TaskService(factory,boundary,clock),clock);
        var taskId=await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default);Assert.NotNull(taskId);Assert.Equal(taskId,await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default));
        var output=Path.Combine(root.FullName,".local/browser-evidence/mid",db.Database.GetDbConnection().Database);Directory.CreateDirectory(output);
        var email=await db.Set<StaffUser>().Where(x=>x.Id==actor.UserId).Select(x=>x.Email).SingleAsync();
        using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development").UseUrls("http://127.0.0.1:0")
            .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString())
            .UseSetting("Cover:DataProtectionPath",Path.Combine(output,"keys")).UseSetting("Cover:FileWorkerEnabled","false").UseSetting("Cover:DocumentWorkerEnabled","false")
            .UseSetting("Cover:OperationalDeliveryWorkerEnabled","false").UseSetting("Cover:WorkflowTaskWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false").ConfigureServices(services=>services.AddSingleton<TimeProvider>(clock)));
        host.UseKestrel(0);using var client=host.CreateClient();var api=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();Assert.True(new Uri(api).IsLoopback);Assert.NotEqual(5000,new Uri(api).Port);
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();var webOrigin=$"http://127.0.0.1:{port}";
        Process Start(IEnumerable<string> args,Dictionary<string,string> env)
        {
            var info=new ProcessStartInfo("node"){WorkingDirectory=root.FullName,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in args)info.ArgumentList.Add(arg);foreach(var (key,value) in env)info.Environment[key]=value;
            return Process.Start(info)??throw new InvalidOperationException("MID browser did not start.");
        }
        using var web=Start(["apps/backoffice/node_modules/next/dist/bin/next","start","apps/backoffice","--hostname","127.0.0.1","--port",port.ToString()],new(){["BACKOFFICE_API_ORIGIN"]=api,["COVER_NEXT_DIST_DIR"]=dist});
        var webOut=web.StandardOutput.ReadToEndAsync();var webErr=web.StandardError.ReadToEndAsync();Process? browser=null;
        try
        {
            using var probe=new HttpClient();var serving=false;
            for(var attempt=0;attempt<80&&!web.HasExited;attempt++){try{using var response=await probe.GetAsync(webOrigin+"/login");if(response.IsSuccessStatusCode){serving=true;break;}}catch(HttpRequestException){}await Task.Delay(250);}
            Assert.True(serving,"Isolated MID web did not start.");
            var fixture=JsonSerializer.Serialize(new{apiOrigin=api,webOrigin,output,email,policyId=policy.Id,versionId=version.Id,submissionId=submission.Id,jobId=work.Id,taskId});
            browser=Start(["scripts/verify-mid-browser.mjs","--worker"],new(){["COVER_MID_BROWSER_FIXTURE"]=fixture,["COVER_MID_BROWSER_PASSWORD"]=password});
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
            using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(7));await browser.WaitForExitAsync(deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"browser.log"),await stdout+await stderr);Assert.True(browser.ExitCode==0,"MID browser failed; inspect sanitized failure evidence.");
            var completed=await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x=>x.Id==work.Id);Assert.Equal("succeeded",completed.State);Assert.Equal(7,completed.Attempts);
            Assert.Equal(1,await db.Set<MidSubmission>().CountAsync());Assert.Equal(1,await db.Set<MidResult>().CountAsync());Assert.Equal(1,await db.Set<DemoProviderOperation>().CountAsync(x=>x.Kind=="mid-update"));
            Assert.Equal(1,await db.Set<JobException>().CountAsync(x=>x.WorkId==work.Id));Assert.Equal(1,await db.Set<WorkflowTaskBinding>().CountAsync(x=>x.JobExceptionId==exception.Id));
            Assert.Equal(submission.RequestJson,await db.Set<MidSubmission>().Select(x=>x.RequestJson).SingleAsync());Assert.Equal(version.ContentHash,await db.Set<PolicyVersion>().Select(x=>x.ContentHash).SingleAsync());
            Assert.Equal(taskId,await workflows.Reconcile(rule.Id,"job-exception",exception.Id,default));
            await File.WriteAllTextAsync(Path.Combine(output,"sql-readback.json"),JsonSerializer.Serialize(new{passed=true,submissionId=submission.Id,versionId=version.Id,attempts=completed.Attempts,taskId}));
            Directory.CreateDirectory(Path.Combine(root.FullName,".local/phase9-14-browser"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName,".local/phase9-14-browser/motor-trade.json"),JsonSerializer.Serialize(new{output,passed=true,verifiedAt=DateTimeOffset.UtcNow}));
        }
        finally
        {
            if(browser is not null){if(!browser.HasExited)browser.Kill(true);browser.Dispose();}
            if(!web.HasExited)web.Kill(true);await web.WaitForExitAsync();await File.WriteAllTextAsync(Path.Combine(output,"web.log"),await webOut+await webErr);
        }
    }
}
