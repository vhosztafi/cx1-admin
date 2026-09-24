using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackOffice.Application;
using BackOffice.Infrastructure.Finance;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlFinancePaymentFailOnceReusesStableOperationAndPostsOnce()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, actor, refundId, version, amount) = await ApprovedPaymentRefund(db, password);
            db.Add(new SettingVersion { Scope = "finance-refund-payment-demo", Version = 2,
                EffectiveFrom = f.Clock.GetUtcNow(), Values = "{\"scenario\":\"fail-once\"}" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var payments = new FinancePaymentService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var queued = await payments.QueueAsync(actor, refundId, version, null,
                "Queue fail-once demo refund", Guid.NewGuid().ToString("N"), Guid.NewGuid());
            var workId = (await payments.DetailAsync(actor, queued.ResourceId)).WorkId;
            var leases = new SqlJobLeases(f.Factory, f.Clock);
            var first = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind, workId));
            var worker = new FinancePaymentWorker(f.Factory, f.Clock);
            var transient = await Assert.ThrowsAsync<FinancePaymentWorkerException>(() => worker.ExecuteProviderAsync(first));
            Assert.Equal(JobFailure.ProviderUnavailable, transient.Failure);
            Assert.True(await leases.FailAsync(first, transient.Failure));
            Assert.Equal("queued", (await payments.DetailAsync(actor, queued.ResourceId)).State);
            Assert.Empty(await db.Set<FinancePosting>().Where(x => x.SourceKind == "refund").ToArrayAsync());
            var pending = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == workId);
            f.Clock.Current = pending.NextAttemptAt.AddSeconds(1);
            var second = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind, workId));
            var accepted = Assert.IsType<FinancePaymentProviderOutcome>(await new FinancePaymentWorker(f.Factory, f.Clock)
                .ExecuteProviderAsync(second));
            Assert.Equal(InboxApplication.Applied, await worker.ApplyAsync(second, accepted));
            Assert.Equal(InboxApplication.Duplicate, await worker.ApplyAsync(second, accepted));
            Assert.Equal("paid", (await payments.DetailAsync(actor, queued.ResourceId)).State);
            Assert.Equal(-amount, Assert.Single(await db.Set<FinancePosting>().AsNoTracking()
                .Where(x => x.SourceKind == "refund").ToArrayAsync()).CashDelta);
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinancePaymentWorker.WorkKind));
            Assert.Equal(2, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == workId));
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    [Fact]
    public async Task RealSqlFinancePaymentDefiniteRejectionRequiresReviewedLinkedRetry()
    {
        await WithDatabase(async (db, password) =>
        {
            var (f, actor, refundId, version, amount) = await ApprovedPaymentRefund(db, password);
            static string Key() => Guid.NewGuid().ToString("N");
            var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
            var payments = new FinancePaymentService(f.Factory, boundary, f.Clock);
            var leases = new SqlJobLeases(f.Factory, f.Clock);
            var worker = new FinancePaymentWorker(f.Factory, f.Clock);
            db.Add(new SettingVersion { Scope = "finance-refund-payment-demo", Version = 2,
                EffectiveFrom = f.Clock.GetUtcNow(), Values = "{\"scenario\":\"reject\"}" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var first = await payments.QueueAsync(actor, refundId, version, null,
                "First reviewed demo payment attempt", Key(), Guid.NewGuid());
            var firstDetail = await payments.DetailAsync(actor, first.ResourceId);
            var firstLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                firstDetail.WorkId));
            var refusal = Assert.IsType<FinancePaymentProviderOutcome>(await worker.ExecuteProviderAsync(firstLease));
            Assert.Equal("rejected", refusal.State);
            Assert.Equal(InboxApplication.Applied, await worker.ApplyAsync(firstLease, refusal));
            Assert.Equal("rejected", (await payments.DetailAsync(actor, first.ResourceId)).State);
            Assert.Empty(await db.Set<FinancePosting>().Where(x => x.SourceKind == "refund").ToArrayAsync());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.QueueAsync(actor,
                refundId, version, null, "Unreviewed replacement payment", Key(), Guid.NewGuid()))).Status);
            var second = await payments.QueueAsync(actor, refundId, version, first.ResourceId,
                "Reviewed second attempt after provider rejection", Key(), Guid.NewGuid());
            var secondDetail = await payments.DetailAsync(actor, second.ResourceId);
            var secondLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                secondDetail.WorkId));
            var financeRoleId = await db.Set<Role>().Where(x => x.Code == "finance").Select(x => x.Id).SingleAsync();
            var underwriterRoleId = await db.Set<Role>().Where(x => x.Code == "underwriter").Select(x => x.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={underwriterRoleId} WHERE UserId={actor.UserId}");
            var denied = await Assert.ThrowsAsync<FinancePaymentWorkerException>(() => worker.ExecuteProviderAsync(secondLease));
            Assert.Equal(JobFailure.Superseded, denied.Failure);
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinancePaymentWorker.WorkKind));
            Assert.True(await leases.FailAsync(secondLease, denied.Failure));
            var failedVersion = await db.Set<FinanceRefundPayment>().AsNoTracking()
                .Where(x => x.Id == second.ResourceId).Select(x => x.RowVersion).SingleAsync();
            var resumeKey = Key();
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.ResumeAsync(actor,
                second.ResourceId, failedVersion, "Reviewed resume after finance authority was restored",
                resumeKey, Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={financeRoleId} WHERE UserId={actor.UserId}");
            var failedDetail = await payments.DetailAsync(actor, second.ResourceId);
            Assert.Equal("failed", failedDetail.State);
            var resumed = await payments.ResumeAsync(actor, second.ResourceId,
                Convert.FromBase64String(failedDetail.Etag.Trim('"')),
                "Reviewed resume after finance authority was restored", resumeKey, Guid.NewGuid());
            Assert.Equal(second.ResourceId, resumed.ResourceId);
            Assert.Equal(secondDetail.WorkId, (await payments.DetailAsync(actor, second.ResourceId)).WorkId);
            Assert.Equal(2, await db.Set<FinanceRefundPayment>().CountAsync());
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinancePaymentWorker.WorkKind));
            Assert.Contains(await db.Set<AuditEvent>().AsNoTracking().ToArrayAsync(),
                x => x.SubjectRecordId == second.ResourceId &&
                    x.EventType == "finance.refund.payment-resume-reviewed" &&
                    x.Reason == "Reviewed resume after finance authority was restored");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={underwriterRoleId} WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.ResumeAsync(actor,
                second.ResourceId, failedVersion, "Reviewed resume after finance authority was restored",
                resumeKey, Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={financeRoleId} WHERE UserId={actor.UserId}");
            var resumedLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                secondDetail.WorkId));
            var secondRefusal = Assert.IsType<FinancePaymentProviderOutcome>(await worker.ExecuteProviderAsync(resumedLease));
            Assert.Equal("rejected", secondRefusal.State);
            Assert.Equal(InboxApplication.Applied, await worker.ApplyAsync(resumedLease, secondRefusal));
            Assert.Equal("rejected", (await payments.DetailAsync(actor, second.ResourceId)).State);
            var rejectedVersion = Convert.FromBase64String((await payments.DetailAsync(actor, second.ResourceId)).Etag.Trim('"'));
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.ResumeAsync(actor,
                second.ResourceId, rejectedVersion, "Rejected provider result cannot resume payment",
                Key(), Guid.NewGuid()))).Status);
            db.Add(new SettingVersion { Scope = "finance-refund-payment-demo", Version = 3,
                EffectiveFrom = f.Clock.GetUtcNow(), Values = "{\"scenario\":\"success\"}" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var third = await payments.QueueAsync(actor, refundId, version, second.ResourceId,
                "Reviewed third attempt after second definite rejection", Key(), Guid.NewGuid());
            var thirdDetail = await payments.DetailAsync(actor, third.ResourceId);
            var thirdLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                thirdDetail.WorkId));
            var accepted = Assert.IsType<FinancePaymentProviderOutcome>(await worker.ExecuteProviderAsync(thirdLease));
            Assert.Equal("accepted", accepted.State);
            Assert.Equal(InboxApplication.Applied, await worker.ApplyAsync(thirdLease, accepted));
            Assert.Equal("paid", (await payments.DetailAsync(actor, third.ResourceId)).State);
            var posting = Assert.Single(await db.Set<FinancePosting>().AsNoTracking()
                .Where(x => x.SourceKind == "refund").ToArrayAsync());
            Assert.Equal(-amount, posting.CashDelta);
            Assert.Equal(3, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinancePaymentWorker.WorkKind));
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinancePaymentWorker.WorkKind && x.State == "accepted"));
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }

    private static async Task<(DecisionFixture Source, ActorContext FinanceActor, Guid RefundId,
        byte[] Version, decimal Amount)> ApprovedPaymentRefund(BackOfficeDbContext db, string password)
    {
        static string Key() => Guid.NewGuid().ToString("N");
        static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
        var setup = await AcceptedIssue(db, password);
        var f = setup.Source;
        await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
            setup.Version, setup.Input, Key(), Guid.NewGuid());
        db.ChangeTracker.Clear();
        var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
        var invoice = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x => x.InvoiceDue > 0);
        var drafts = new ServicingDraftService(f.Factory, f.Clock);
        var review = new CancellationReviewService(f.Factory, f.Clock);
        var created = await drafts.CreateAsync(f.Underwriter, basis.TermId,
            Version((await drafts.ListAsync(f.Underwriter, basis.TermId)).Etag),
            new("cancellation", basis.Id, JsonSerializer.SerializeToElement(new
            { localDate = "2026-10-15", localTime = "00:00", timeZone = "Europe/London" }),
                "Fictional payment rejection cancellation"), Key(), Guid.NewGuid());
        var lease = await drafts.LeaseAsync(f.Underwriter, created.ResourceId, Version(created.Etag!),
            "acquire", null, null, Key(), Guid.NewGuid());
        var body = JsonNode.Parse(lease.Body)!;
        var fence = body["lease"]!["leaseToken"]!.GetValue<Guid>();
        body["proposal"]!["cancellationReasonCode"] = "insured-request";
        var saved = await drafts.SaveAsync(f.Underwriter, created.ResourceId, Version(lease.Etag!),
            fence, body["proposal"]!.ToJsonString(), Key(), Guid.NewGuid());
        var upload = await review.UploadAsync(f.Underwriter, created.ResourceId, Version(saved.Etag!),
            fence, "cancellation-request", null, "request.txt", "text/plain",
            Encoding.UTF8.GetBytes("Fictional cancellation source for payment rejection."),
            Key(), Guid.NewGuid());
        var evidence = await review.ReviewEvidenceAsync(f.Underwriter, created.ResourceId,
            upload.ResourceId, Version(upload.Etag!), fence, "accepted",
            "Insured cancellation request reviewed", Key(), Guid.NewGuid());
        var preview = await review.ReadAsync(f.Underwriter, created.ResourceId);
        Assert.Empty(preview.Blockers);
        var prepared = await review.PrepareAsync(f.Underwriter, created.ResourceId,
            Version(evidence.Etag!), fence, preview.PreviewHash, Key(), Guid.NewGuid());
        var approved = await review.ApproveAsync(f.Underwriter, created.ResourceId,
            Version(prepared.Etag!), fence, prepared.ResourceId, preview.PreviewHash,
            "Cancellation request independently checked", Key(), Guid.NewGuid());
        await review.IssueAsync(f.Underwriter, created.ResourceId, Version(approved.Etag!), fence,
            new CancellationIssueInput(prepared.ResourceId, approved.ResourceId,
                preview.PreviewHash, "Issue the reviewed cancellation credit"), Key(), Guid.NewGuid());
        db.ChangeTracker.Clear();
        var credit = await db.Set<IssueFinancialObligation>().AsNoTracking()
            .SingleAsync(x => x.Purpose == "cancellation");
        var actor = await FinanceLedgerActor(db);
        var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
        var receipts = new FinanceReceiptService(f.Factory, boundary, f.Clock);
        var refunds = new FinanceRefundService(f.Factory, boundary, f.Clock);
        var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
        var payerId = invoice.DebtorKind == "agency" ? invoice.DebtorAgencyId!.Value : invoice.DebtorRelationshipId!.Value;
        var cash = await receipts.RecordAsync(actor, invoice.AgencyId, FinanceLedgerMath.Money(invoice.InvoiceDue),
            "GBP", date, "Fictional collected premium", "manual", Guid.NewGuid(), invoice.DebtorKind,
            payerId, Key(), Guid.NewGuid());
        var cashView = await receipts.DetailAsync(actor, cash.ResourceId);
        var allocation = await receipts.AllocateAsync(actor, cash.ResourceId, cashView.AssignmentId,
            [new ReceiptAllocationInput(invoice.Id, FinanceLedgerMath.Money(invoice.InvoiceDue))], Key(), Guid.NewGuid());
        var amount = Math.Min(invoice.InvoiceDue, -credit.InvoiceDue);
        var requested = await refunds.RequestAsync(actor, credit.Id, FinanceLedgerMath.Money(amount),
            [new RefundSourceInput(allocation.ResourceId, FinanceLedgerMath.Money(amount))],
            "Return collected cancellation premium", Key(), Guid.NewGuid());
        var view = await refunds.DetailAsync(actor, requested.ResourceId);
        for (var n = 0; n < view.RequiredApprovals; n++)
        {
            var email = $"payment-retry-approver-{n}-{Guid.NewGuid():N}@example.invalid";
            var user = new StaffUser { Email = email, NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = $"Fictional retry approver {n}", State = "active" };
            db.Add(user); await db.SaveChangesAsync();
            var roleId = await db.Set<Role>().Where(x => x.Code == "finance").Select(x => x.Id).SingleAsync();
            db.Add(new UserRole { UserId = user.Id, RoleId = roleId }); await db.SaveChangesAsync();
            var approver = new ActorContext(user.Id, null, null, new HashSet<string>(["finance"]));
            await refunds.DecideAsync(approver, requested.ResourceId, Version(view.Etag), "approve",
                "Independent payment retry approval", Key(), Guid.NewGuid());
            view = await refunds.DetailAsync(actor, requested.ResourceId);
        }
        Assert.Equal("approved", view.State);
        return (f, actor, requested.ResourceId, Version(view.Etag), amount);
    }

    [Fact]
    public async Task RealSqlFinancePaymentRecoversOneAcceptedEffectAndOneCashPosting()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password);
            var f = setup.Source;
            static string Key() => Guid.NewGuid().ToString("N");
            static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId,
                setup.Version, setup.Input, Key(), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var basis = await db.Set<PolicyVersion>().AsNoTracking().SingleAsync();
            var invoice = await db.Set<IssueFinancialObligation>().AsNoTracking().SingleAsync(x => x.InvoiceDue > 0);
            var drafts = new ServicingDraftService(f.Factory, f.Clock);
            var review = new CancellationReviewService(f.Factory, f.Clock);
            var created = await drafts.CreateAsync(f.Underwriter, basis.TermId,
                Version((await drafts.ListAsync(f.Underwriter, basis.TermId)).Etag),
                new("cancellation", basis.Id, JsonSerializer.SerializeToElement(new
                { localDate = "2026-10-15", localTime = "00:00", timeZone = "Europe/London" }),
                    "Fictional payment recovery cancellation"), Key(), Guid.NewGuid());
            var lease = await drafts.LeaseAsync(f.Underwriter, created.ResourceId, Version(created.Etag!),
                "acquire", null, null, Key(), Guid.NewGuid());
            var body = JsonNode.Parse(lease.Body)!;
            var fence = body["lease"]!["leaseToken"]!.GetValue<Guid>();
            body["proposal"]!["cancellationReasonCode"] = "insured-request";
            var saved = await drafts.SaveAsync(f.Underwriter, created.ResourceId, Version(lease.Etag!),
                fence, body["proposal"]!.ToJsonString(), Key(), Guid.NewGuid());
            var upload = await review.UploadAsync(f.Underwriter, created.ResourceId, Version(saved.Etag!),
                fence, "cancellation-request", null, "request.txt", "text/plain",
                Encoding.UTF8.GetBytes("Fictional insured cancellation request for payment recovery."),
                Key(), Guid.NewGuid());
            var evidence = await review.ReviewEvidenceAsync(f.Underwriter, created.ResourceId,
                upload.ResourceId, Version(upload.Etag!), fence, "accepted",
                "Insured cancellation request reviewed", Key(), Guid.NewGuid());
            var preview = await review.ReadAsync(f.Underwriter, created.ResourceId);
            Assert.Empty(preview.Blockers);
            var prepared = await review.PrepareAsync(f.Underwriter, created.ResourceId,
                Version(evidence.Etag!), fence, preview.PreviewHash, Key(), Guid.NewGuid());
            var approved = await review.ApproveAsync(f.Underwriter, created.ResourceId,
                Version(prepared.Etag!), fence, prepared.ResourceId, preview.PreviewHash,
                "Cancellation request independently checked", Key(), Guid.NewGuid());
            await review.IssueAsync(f.Underwriter, created.ResourceId, Version(approved.Etag!), fence,
                new CancellationIssueInput(prepared.ResourceId, approved.ResourceId,
                    preview.PreviewHash, "Issue the reviewed cancellation credit"), Key(), Guid.NewGuid());
            db.ChangeTracker.Clear();
            var credit = await db.Set<IssueFinancialObligation>().AsNoTracking()
                .SingleAsync(x => x.Purpose == "cancellation");
            var actor = await FinanceLedgerActor(db);
            var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
            var receipts = new FinanceReceiptService(f.Factory, boundary, f.Clock);
            var refunds = new FinanceRefundService(f.Factory, boundary, f.Clock);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            var payerId = invoice.DebtorKind == "agency" ? invoice.DebtorAgencyId!.Value : invoice.DebtorRelationshipId!.Value;
            var cash = await receipts.RecordAsync(actor, invoice.AgencyId, FinanceLedgerMath.Money(invoice.InvoiceDue),
                "GBP", date, "Fictional collected premium", "manual", Guid.NewGuid(),
                invoice.DebtorKind, payerId, Key(), Guid.NewGuid());
            var cashView = await receipts.DetailAsync(actor, cash.ResourceId);
            var allocation = await receipts.AllocateAsync(actor, cash.ResourceId, cashView.AssignmentId,
                [new ReceiptAllocationInput(invoice.Id, FinanceLedgerMath.Money(invoice.InvoiceDue))], Key(), Guid.NewGuid());
            var refundAmount = Math.Min(invoice.InvoiceDue, -credit.InvoiceDue);
            var requested = await refunds.RequestAsync(actor, credit.Id, FinanceLedgerMath.Money(refundAmount),
                [new RefundSourceInput(allocation.ResourceId, FinanceLedgerMath.Money(refundAmount))],
                "Return collected cancellation premium", Key(), Guid.NewGuid());
            var refundView = await refunds.DetailAsync(actor, requested.ResourceId);
            for (var n = 0; n < refundView.RequiredApprovals; n++)
            {
                var user = new StaffUser { Email = $"payment-approver-{n}-{Guid.NewGuid():N}@example.invalid",
                    NormalizedEmail = $"PAYMENT-APPROVER-{n}-{Guid.NewGuid():N}@EXAMPLE.INVALID",
                    DisplayName = $"Fictional approver {n}", State = "active" };
                db.Add(user); await db.SaveChangesAsync();
                var roleId = await db.Set<Role>().Where(x => x.Code == "finance").Select(x => x.Id).SingleAsync();
                db.Add(new UserRole { UserId = user.Id, RoleId = roleId }); await db.SaveChangesAsync();
                var approver = new ActorContext(user.Id, null, null, new HashSet<string>(["finance"]));
                await refunds.DecideAsync(approver, requested.ResourceId, Version(refundView.Etag), "approve",
                    "Independent payment approval", Key(), Guid.NewGuid());
                refundView = await refunds.DetailAsync(actor, requested.ResourceId);
            }
            Assert.Equal("approved", refundView.State);
            db.Add(new SettingVersion { Scope = "finance-refund-payment-demo", Version = 2,
                EffectiveFrom = f.Clock.GetUtcNow(), Values = "{\"scenario\":\"timeout-after-success\"}" });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var payments = new FinancePaymentService(f.Factory, boundary, f.Clock);
            var queueKey = Key();
            var queued = await payments.QueueAsync(actor, requested.ResourceId, Version(refundView.Etag),
                null, "Queue approved demo refund", queueKey, Guid.NewGuid());
            var detail = await payments.DetailAsync(actor, queued.ResourceId);
            Assert.Equal("queued", detail.State);
            Assert.Equal(requested.ResourceId, detail.RefundRequestId);
            var forgedProvider = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE FinanceRefundPayment SET ProviderState='accepted',ProviderOperationId={Guid.NewGuid()},
                    ProviderEventId='forged-event',AppliedAt={f.Clock.GetUtcNow()}
                WHERE Id={queued.ResourceId}
                """));
            Assert.Equal(52604, forgedProvider.Number);
            var held = await db.Set<AccountingPeriod>().AsNoTracking()
                .SingleAsync(x => x.StartsOn <= date && date < x.EndsOn);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT FinancePosting(Id,SourceKind,SourceId,AgencyId,RelationshipId,PolicyId,TransactionId,
                    DebtorKind,AccountingPeriodId,PostingDate,EffectiveAt,PostedAt,Currency,DebtorDelta,
                    ProviderDelta,CashDelta,InternalDelta,Reason,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},'refund',{queued.ResourceId},{credit.AgencyId},{credit.RelationshipId},
                    {credit.PolicyId},{credit.TransactionId},{credit.DebtorKind},{held.Id},{date},
                    {f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},'GBP',{refundAmount},0,{-refundAmount},0,
                    'Forged refund cash before provider acceptance',{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            Assert.Equal(queued.ResourceId, (await payments.QueueAsync(actor, requested.ResourceId,
                Version(refundView.Etag), null, "Queue approved demo refund", queueKey, Guid.NewGuid())).ResourceId);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => payments.QueueAsync(actor,
                requested.ResourceId, Version(refundView.Etag), null, "Changed payment intent under same key",
                queueKey, Guid.NewGuid()));
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.QueueAsync(actor,
                requested.ResourceId, Version(refundView.Etag), null, "Second key cannot pay again",
                Key(), Guid.NewGuid()))).Status);
            var leases = new SqlJobLeases(f.Factory, f.Clock);
            var firstLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind, detail.WorkId));
            var worker = new FinancePaymentWorker(f.Factory, f.Clock);
            var timeout = await Assert.ThrowsAsync<FinancePaymentWorkerException>(() => worker.ExecuteProviderAsync(firstLease));
            Assert.Equal(JobFailure.ProviderTimeout, timeout.Failure);
            Assert.True(await leases.FailAsync(firstLease, timeout.Failure));
            var pendingApplication = await payments.DetailAsync(actor, queued.ResourceId);
            Assert.Equal("provider-acknowledged/application-pending", pendingApplication.State);
            Assert.Equal("accepted", pendingApplication.ProviderState);
            Assert.NotNull(pendingApplication.ProviderOperationId);
            Assert.Single(await db.Set<DemoProviderOperation>().Where(x => x.Kind == FinancePaymentWorker.WorkKind)
                .ToArrayAsync());
            Assert.Empty(await db.Set<FinancePosting>().Where(x => x.SourceKind == "refund").ToArrayAsync());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.ResumeAsync(actor,
                queued.ResourceId, Version(pendingApplication.Etag),
                "Acknowledged provider payment cannot be resumed as unsent", Key(), Guid.NewGuid()))).Status);
            await SetLegacyPeriodState(db, $"UPDATE AccountingPeriod SET State='closed' WHERE Id={held.Id}");
            var financeRoleId = await db.Set<Role>().Where(x => x.Code == "finance").Select(x => x.Id).SingleAsync();
            var underwriterRoleId = await db.Set<Role>().Where(x => x.Code == "underwriter").Select(x => x.Id).SingleAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={underwriterRoleId} WHERE UserId={actor.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.QueueAsync(actor,
                requested.ResourceId, Version(refundView.Etag), null, "Queue approved demo refund",
                queueKey, Guid.NewGuid()))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => payments.DetailAsync(actor,
                queued.ResourceId))).Status);
            var work = await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == detail.WorkId);
            f.Clock.Current = work.NextAttemptAt.AddSeconds(1);
            var secondLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind, detail.WorkId));
            var recovered = Assert.IsType<FinancePaymentProviderOutcome>(await new FinancePaymentWorker(f.Factory, f.Clock)
                .ExecuteProviderAsync(secondLease));
            for (var attempt = 3; attempt <= 6; attempt++)
            {
                f.Clock.Current = f.Clock.GetUtcNow().Add(SqlJobLeases.LeaseDuration).AddSeconds(1);
                var crashedLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                    detail.WorkId));
                Assert.Equal(attempt, crashedLease.Attempt);
                var afterCrash = Assert.IsType<FinancePaymentProviderOutcome>(await new FinancePaymentWorker(f.Factory, f.Clock)
                    .ExecuteProviderAsync(crashedLease));
                Assert.Equal(recovered.OperationId, afterCrash.OperationId);
            }
            f.Clock.Current = f.Clock.GetUtcNow().Add(SqlJobLeases.LeaseDuration).AddSeconds(1);
            Assert.Null(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind, detail.WorkId));
            Assert.Equal(12, (await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == detail.WorkId)).AttemptLimit);
            f.Clock.Current = f.Clock.GetUtcNow().AddSeconds(6);
            for (var attempt = 7; attempt <= 18; attempt++)
            {
                var crashedLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                    detail.WorkId));
                Assert.Equal(attempt, crashedLease.Attempt);
                var afterCrash = Assert.IsType<FinancePaymentProviderOutcome>(await new FinancePaymentWorker(f.Factory, f.Clock)
                    .ExecuteProviderAsync(crashedLease));
                Assert.Equal(recovered.OperationId, afterCrash.OperationId);
                f.Clock.Current = f.Clock.GetUtcNow().Add(SqlJobLeases.LeaseDuration).AddSeconds(1);
                if (attempt != 12 && attempt != 18) continue;
                Assert.Null(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind, detail.WorkId));
                Assert.Equal(attempt == 12 ? 18 : int.MaxValue,
                    (await db.Set<OutboxWork>().AsNoTracking().SingleAsync(x => x.Id == detail.WorkId)).AttemptLimit);
                f.Clock.Current = f.Clock.GetUtcNow().AddSeconds(6);
            }
            var recoveryLease = Assert.IsType<JobLease>(await leases.ClaimWorkAsync(FinancePaymentWorker.WorkKind,
                detail.WorkId));
            Assert.Equal(19, recoveryLease.Attempt);
            var finalOutcome = Assert.IsType<FinancePaymentProviderOutcome>(await new FinancePaymentWorker(f.Factory, f.Clock)
                .ExecuteProviderAsync(recoveryLease));
            Assert.Equal(recovered.OperationId, finalOutcome.OperationId);
            Assert.Equal(InboxApplication.Applied, await worker.ApplyAsync(recoveryLease, finalOutcome));
            Assert.Equal(InboxApplication.Duplicate, await worker.ApplyAsync(recoveryLease, finalOutcome));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE UserRole SET RoleId={financeRoleId} WHERE UserId={actor.UserId}");
            Assert.Equal("paid", (await payments.DetailAsync(actor, queued.ResourceId)).State);
            var posting = Assert.Single(await db.Set<FinancePosting>().AsNoTracking()
                .Where(x => x.SourceKind == "refund" && x.SourceId == queued.ResourceId).ToArrayAsync());
            Assert.Equal(-refundAmount, posting.CashDelta);
            Assert.Equal(refundAmount, posting.DebtorDelta);
            Assert.NotEqual(held.Id, posting.AccountingPeriodId);
            Assert.True(posting.PostingDate >= held.EndsOn);
            Assert.Equal(1, await db.Set<DemoProviderOperation>().CountAsync(x => x.Kind == FinancePaymentWorker.WorkKind));
            Assert.Equal(1, await db.Set<AdapterInbox>().CountAsync(x => x.Provider == "finance-refund-demo"));
            Assert.Equal(19, await db.Set<AdapterAttempt>().CountAsync(x => x.WorkId == detail.WorkId));
            var reconciliation = new FinanceReconciliationService(f.Factory, boundary, f.Clock);
            var bankLine = await reconciliation.ImportAsync(actor, posting.AgencyId, Key(), posting.PostingDate,
                "Fictional refund bank debit", FinanceLedgerMath.Money(-refundAmount), "GBP", "{\"demo\":true}",
                Key(), Guid.NewGuid());
            var recon = await reconciliation.CreateAsync(actor, posting.AgencyId, posting.PostingDate,
                posting.PostingDate.AddDays(1), Key(), Guid.NewGuid());
            await reconciliation.MatchAsync(actor, recon.ResourceId, bankLine.ResourceId, posting.Id,
                FinanceLedgerMath.Money(-refundAmount), "Match saved paid refund to negative bank line",
                Key(), Guid.NewGuid());
            Assert.Single(await db.Set<ReconciliationMatch>().Where(x => x.FinancePostingId == posting.Id).ToArrayAsync());
            var reconciled = await reconciliation.DetailAsync(actor, recon.ResourceId);
            Assert.Contains(reconciled.Targets, x => x.FinancePostingId == posting.Id &&
                x.SourceKind == "refund" && x.Residual == "0.00");
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
