using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalCancellationDeliversOnceAndAppliesOnlyAtEffectiveTime() => WithDatabase(async (db, password) =>
    {
        var setup = await AcceptedIssue(db, password); var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
        var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
        var commands = new SqlCommandBoundary(f.Factory, f.Clock); var tasks = new TaskService(f.Factory, commands, f.Clock);
        var rule = await WorkflowRule(db, f.Clock.Current, "cancellation-renewal", "renewal-reminder", "renewal", 365);
        var renewalId = await new WorkflowTaskService(f.Factory, tasks, f.Clock).Reconcile(rule.Id, "policy-term", term.Id, default); Assert.NotNull(renewalId);
        var subject = await tasks.Register(f.Underwriter, new("policy", term.PolicyId), "cancellation-subject", default);
        var manual = await tasks.Create(f.Underwriter, subject.ResourceId, new("renewal", "Manual renewal follow-up", "normal", new("unassigned"), null), "manual-renewal", default);
        var complaint = await tasks.Create(f.Underwriter, subject.ResourceId, new("complaint", "Keep complaint investigation open", "normal", new("unassigned"), null), "keep-complaint", default);
        await VerifyCancellationDocument(db, f.Factory, f.Clock, f.Underwriter, afterIssue: async () =>
        {
            var consequences = await db.Set<CancellationConsequence>().AsNoTracking().ToArrayAsync();
            var notice = consequences.Single(x => x.Kind == "notice"); var withdrawal = consequences.Single(x => x.Kind == "certificate-withdrawal"); var close = consequences.Single(x => x.Kind == "task-close");
            var effective = await db.Set<CancellationIssueDecision>().Select(x => x.EffectiveAt).SingleAsync();
            var originals = consequences.ToDictionary(x => x.Id, x => (x.PayloadJson, Convert.ToHexString(x.PayloadHash)));
            await using (var tx = await db.Database.BeginTransactionAsync()) { await DocumentTemplateSeed.SeedAsync(db); await OperationalDeliverySeed.Seed(db); await tx.CommitAsync(); }
            var root = Path.GetFullPath(Path.Combine(".local", "cancellation-operations", db.Database.GetDbConnection().Database));
            var store = new OperationalFileStore(root, []);
            var files = new FileService(f.Factory, commands, store, f.Clock); var sources = new PolicyDocumentRenderService(f.Factory, new PolicyDocumentRenderer());
            var docs = new DocumentService(f.Factory, commands, sources, f.Clock, files);
            var basis=await db.Set<PolicyVersion>().AsNoTracking().SingleAsync(x=>x.TermId==term.Id&&x.Sequence==1);
            var certificateTemplate=await db.Set<TemplateVersion>().AsNoTracking().Where(x=>x.ProductId==term.ProductId&&x.Kind=="policy-certificate"&&x.State=="published"&&x.EffectiveFrom<=f.Clock.Current&&x.EffectiveTo>f.Clock.Current).OrderByDescending(x=>x.Version).FirstAsync();
            async Task<DocumentVersion> Certificate(string key)
            {
                var generated=await docs.Generate(f.Underwriter,subject.ResourceId,new("policy-certificate",new("policy-version",PolicyVersionId:basis.Id),certificateTemplate.Id,"internal","Retain the original issued certificate"),key,default);
                var saved=await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x=>x.Id==generated.ResourceId);
                Assert.True(await new DocumentGenerationWorker(f.Factory,sources,store,f.Clock).Process(saved.WorkId,default));return saved;
            }
            var certificate=await Certificate("before-withdrawal");
            var originalCertificateFile=await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync(x=>x.VersionId==certificate.Id);
            await docs.RegisterCancellationNotice(f.Underwriter, notice.Id, "new-notice", default);
            var document = await db.Set<DocumentVersion>().AsNoTracking().SingleAsync(x => x.CancellationConsequenceId == notice.Id);
            Assert.True(await new DocumentGenerationWorker(f.Factory, sources, store, f.Clock).Process(document.WorkId, default));
            var fileBefore = await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync(x => x.VersionId == document.Id);
            var worker = new CancellationOperationsWorker(f.Factory, tasks, f.Clock); var deliveries = new MessageDeliveryService(commands, f.Clock); var leases = new SqlJobLeases(f.Factory, f.Clock);
            Assert.False(await worker.PrepareNotice(notice.WorkId, deliveries)); Assert.False(await worker.PrepareNotice(notice.WorkId, deliveries));
            var dispatch = await db.Set<CancellationNoticeDispatch>().AsNoTracking().SingleAsync(); var delivery = await db.Set<OperationalDelivery>().AsNoTracking().SingleAsync(x => x.Id == dispatch.DeliveryId);
            Assert.Equal(document.Id, dispatch.DocumentVersionId);
            var deliveryLease = await leases.ClaimWorkAsync(MessageDeliveryService.WorkKind, delivery.WorkId); Assert.NotNull(deliveryLease);
            WebApplicationFactory<Program> Host(bool deliver) => new WebApplicationFactory<Program>().WithWebHostBuilder(b=>b.UseEnvironment("Development")
                .UseSetting("Cover:SqlConnection",db.Database.GetConnectionString()).UseSetting("Cover:FileStoragePath",root)
                .UseSetting("Cover:DataProtectionPath",Path.Combine(root,"keys")).UseSetting("Cover:OperationalDeliveryWorkerEnabled",deliver?"true":"false")
                .UseSetting("Cover:OperationalCancellationWorkerEnabled","false").UseSetting("Cover:OperationalMidWorkerEnabled","false")
                .UseSetting("Cover:DocumentWorkerEnabled","false").UseSetting("Cover:DiagnosticWorkerEnabled","false")
                .ConfigureServices(s=>s.AddSingleton<TimeProvider>(f.Clock)));
            await using(var firstHost=Host(false))
            {
                using var client=firstHost.CreateClient();await using var scope=firstHost.Services.CreateAsyncScope();
                var sender=scope.ServiceProvider.GetRequiredService<MessageDeliveryWorker>();Assert.NotNull(await sender.ExecuteProvider(deliveryLease));
                Assert.Equal("queued",await db.Set<OperationalDelivery>().Where(x=>x.Id==delivery.Id).Select(x=>x.State).SingleAsync());
            }
            f.Clock.Current+=SqlJobLeases.LeaseDuration+TimeSpan.FromSeconds(1);
            await using(var secondHost=Host(true))
            {
                using var client=secondHost.CreateClient();var delivered=false;
                for(var poll=0;poll<120&&!delivered;poll++)
                {delivered=await db.Set<OperationalDelivery>().AnyAsync(x=>x.Id==delivery.Id&&x.State=="delivered");if(!delivered)await Task.Delay(250);}
                Assert.True(delivered,"A restarted API host must recover the saved provider effect.");
            }
            Assert.Equal(2,await db.Set<AdapterAttempt>().CountAsync(x=>x.WorkId==delivery.WorkId));
            Assert.True(await worker.PrepareNotice(notice.WorkId, deliveries));
            var noticeLease = await leases.ClaimWorkAsync("cancellation-notice", notice.WorkId); Assert.NotNull(noticeLease); Assert.True(await worker.ApplyNotice(noticeLease));
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == MessageDeliveryService.WorkKind)); Assert.Empty(await db.Set<CancellationNoticeReceipt>().ToArrayAsync());
            f.Clock.Current = effective.AddTicks(-1);
            Assert.Null(await leases.ClaimWorkAsync("cancellation-certificate-withdrawal", withdrawal.WorkId)); Assert.Null(await leases.ClaimWorkAsync("cancellation-task-close", close.WorkId));
            Assert.Empty(await db.Set<CertificateWithdrawal>().ToArrayAsync()); Assert.Empty(await db.Set<CancellationTaskClosure>().ToArrayAsync());
            f.Clock.Current = effective;
            var first = await leases.ClaimWorkAsync("cancellation-certificate-withdrawal", withdrawal.WorkId); Assert.NotNull(first);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_Cancellation_TestRollback ON CertificateWithdrawal AFTER INSERT AS THROW 52099,'Owned rollback probe.',1;");
            try { var error = await Assert.ThrowsAsync<DbUpdateException>(() => worker.Apply(first)); Assert.Equal(52099, Assert.IsType<SqlException>(error.InnerException).Number); }
            finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER TR_Cancellation_TestRollback;"); }
            Assert.Empty(await db.Set<CertificateWithdrawal>().ToArrayAsync()); Assert.False(await db.Set<CancellationOperationalReceipt>().AnyAsync(x => x.ConsequenceId == withdrawal.Id));
            Assert.True(await worker.Apply(first)); Assert.False(await worker.Apply(first));
            var closeLease = await leases.ClaimWorkAsync("cancellation-task-close", close.WorkId); Assert.NotNull(closeLease); Assert.True(await worker.Apply(closeLease));
            Assert.Equal("cancelled", await db.Set<OperationalTask>().Where(x => x.Id == renewalId).Select(x => x.State).SingleAsync());
            Assert.Equal("open", await db.Set<OperationalTask>().Where(x => x.Id == manual.ResourceId).Select(x => x.State).SingleAsync());
            Assert.Equal("open", await db.Set<OperationalTask>().Where(x => x.Id == complaint.ResourceId).Select(x => x.State).SingleAsync());
            Assert.Single(await db.Set<CancellationTaskClosure>().ToArrayAsync()); Assert.Single(await db.Set<CertificateWithdrawal>().ToArrayAsync());
            Assert.Equal(effective, await db.Set<CertificateWithdrawal>().Select(x => x.EffectiveAt).SingleAsync());
            using(var metadata=JsonDocument.Parse((await docs.ReadVersion(f.Underwriter,certificate.Id,default)).Body))Assert.Equal(effective,metadata.RootElement.GetProperty("withdrawnEffectiveAt").GetDateTimeOffset());
            await using(var download=await docs.DownloadVersion(f.Underwriter,certificate.Id,default))Assert.True(download.Content.CanRead);
            Assert.Equal(originalCertificateFile.FileObjectId,(await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync(x=>x.VersionId==certificate.Id)).FileObjectId);
            var laterCertificate=await Certificate("later-historical-render");
            using(var metadata=JsonDocument.Parse((await docs.ReadVersion(f.Underwriter,laterCertificate.Id,default)).Body))Assert.Equal(effective,metadata.RootElement.GetProperty("withdrawnEffectiveAt").GetDateTimeOffset());
            Assert.Equal(fileBefore.FileObjectId, (await db.Set<DocumentVersionContent>().AsNoTracking().SingleAsync(x => x.VersionId == document.Id)).FileObjectId);
            await using (var download = await docs.DownloadVersion(f.Underwriter, document.Id, default)) { Assert.True(download.Content.CanRead); }
            foreach (var row in await db.Set<CancellationConsequence>().AsNoTracking().ToArrayAsync()) Assert.Equal(originals[row.Id], (row.PayloadJson, Convert.ToHexString(row.PayloadHash)));
            var immutable=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlRawAsync("UPDATE CertificateWithdrawal SET EffectiveAt=DATEADD(second,1,EffectiveAt);"));Assert.Equal(52030,immutable.Number);
            db.Add(new CancellationNoticeReceipt{ConsequenceId=notice.Id,WorkId=notice.WorkId,PayloadHash=notice.PayloadHash,Outcome="demo-delivered",CreatedAt=f.Clock.Current});
            var mixed=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal(52036,Assert.IsType<SqlException>(mixed.InnerException).Number);db.ChangeTracker.Clear();
            Assert.Empty(await db.Set<CancellationNoticeReceipt>().ToArrayAsync());
            var downgrade=await Assert.ThrowsAsync<SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20260922135419_OperationalMid"));Assert.Equal(52035,downgrade.Number);
            Assert.Single(await db.Set<CertificateWithdrawal>().ToArrayAsync());
        });
    });
}
