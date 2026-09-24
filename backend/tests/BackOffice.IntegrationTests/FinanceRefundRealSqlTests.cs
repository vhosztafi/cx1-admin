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
    public async Task RealSqlFinanceRefundRequiresCollectedCreditAndIndependentVersionedApproval()
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
            Assert.True(invoice.InvoiceDue > 0.01m);
            var drafts = new ServicingDraftService(f.Factory, f.Clock);
            var review = new CancellationReviewService(f.Factory, f.Clock);
            var created = await drafts.CreateAsync(f.Underwriter, basis.TermId,
                Version((await drafts.ListAsync(f.Underwriter, basis.TermId)).Etag),
                new("cancellation", basis.Id, JsonSerializer.SerializeToElement(new
                { localDate = "2026-10-15", localTime = "00:00", timeZone = "Europe/London" }),
                    "Fictional refund entitlement cancellation"), Key(), Guid.NewGuid());
            var lease = await drafts.LeaseAsync(f.Underwriter, created.ResourceId, Version(created.Etag!),
                "acquire", null, null, Key(), Guid.NewGuid());
            var body = JsonNode.Parse(lease.Body)!;
            var fence = body["lease"]!["leaseToken"]!.GetValue<Guid>();
            body["proposal"]!["cancellationReasonCode"] = "insured-request";
            var saved = await drafts.SaveAsync(f.Underwriter, created.ResourceId, Version(lease.Etag!),
                fence, body["proposal"]!.ToJsonString(), Key(), Guid.NewGuid());
            var upload = await review.UploadAsync(f.Underwriter, created.ResourceId, Version(saved.Etag!),
                fence, "cancellation-request", null, "request.txt", "text/plain",
                Encoding.UTF8.GetBytes("Fictional insured cancellation request retained as source evidence."),
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
            Assert.True(credit.InvoiceDue < 0);
            Assert.True(await db.Set<Journal>().AnyAsync(x => x.ObligationId == credit.Id && x.PostedAt != null));
            var actor = await FinanceLedgerActor(db);
            var boundary = new SqlCommandBoundary(f.Factory, f.Clock);
            var receipts = new FinanceReceiptService(f.Factory, boundary, f.Clock);
            var refunds = new FinanceRefundService(f.Factory, boundary, f.Clock);
            var date = DateOnly.FromDateTime(f.Clock.GetUtcNow().Date);
            var payerId = invoice.DebtorKind == "agency" ? invoice.DebtorAgencyId!.Value : invoice.DebtorRelationshipId!.Value;

            async Task<Guid> Collect(decimal amount)
            {
                var receipt = await receipts.RecordAsync(actor, invoice.AgencyId,
                    FinanceLedgerMath.Money(amount), "GBP", date, "Fictional collected premium",
                    "manual", Guid.NewGuid(), invoice.DebtorKind, payerId, Key(), Guid.NewGuid());
                var detail = await receipts.DetailAsync(actor, receipt.ResourceId);
                var result = await receipts.AllocateAsync(actor, receipt.ResourceId, detail.AssignmentId,
                    [new ReceiptAllocationInput(invoice.Id, FinanceLedgerMath.Money(amount))], Key(), Guid.NewGuid());
                return result.ResourceId;
            }

            var tinyAllocation = await Collect(0.01m);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => refunds.RequestAsync(actor,
                credit.Id, "0.01", [new RefundSourceInput(tinyAllocation, "0.01")],
                "Attempt to refund before debt is covered", Key(), Guid.NewGuid()))).Status);
            var largeAllocation = await Collect(invoice.InvoiceDue - 0.01m);
            var refundable = Math.Min(-credit.InvoiceDue, invoice.InvoiceDue);
            var chosen = refundable <= 0.01m
                ? new[] { new RefundSourceInput(tinyAllocation, FinanceLedgerMath.Money(refundable)) }
                : new[] { new RefundSourceInput(tinyAllocation, "0.01"),
                    new RefundSourceInput(largeAllocation, FinanceLedgerMath.Money(refundable - 0.01m)) };
            var amountText = FinanceLedgerMath.Money(refundable);
            var attempts = Enumerable.Range(0, 2).Select(_ => refunds.RequestAsync(actor, credit.Id,
                amountText, chosen, "Return collected cancellation premium", Key(), Guid.NewGuid())).ToArray();
            var outcomes = await Task.WhenAll(attempts.Select(async task =>
            {
                try { return (Saved: true, Outcome: await task, Error: ""); }
                catch (QuoteOperationException error) { return (Saved: false, Outcome: default(CommandOutcome), Error: error.Code); }
            }));
            var first = Assert.Single(outcomes, x => x.Saved).Outcome;
            Assert.NotNull(first);
            Assert.Single(outcomes, x => !x.Saved);
            Assert.Single(await db.Set<RefundRequest>().AsNoTracking().ToArrayAsync());
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => refunds.RequestAsync(actor,
                credit.Id, amountText, chosen, "Competing source reservation must fail", Key(), Guid.NewGuid()))).Status);
            Assert.Equal(409, (await Assert.ThrowsAsync<QuoteOperationException>(() => receipts.ReverseAsync(actor,
                tinyAllocation, "Cannot reverse reserved collected cash", Key(), Guid.NewGuid()))).Status);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT Allocation(Id,ReceiptId,ObligationId,Amount,ReversalOfId,Reason,AppliedAt,OperationId,Ordinal,AccountingPeriodId,PostingDate,CreatedAt,CreatedBy)
                SELECT {Guid.NewGuid()},a.ReceiptId,a.ObligationId,a.Amount,a.Id,'Forged reserved cash reversal',
                    {f.Clock.GetUtcNow()},{Guid.NewGuid()},1,a.AccountingPeriodId,a.PostingDate,{f.Clock.GetUtcNow()},{actor.UserId}
                FROM Allocation a WHERE a.Id={tinyAllocation}
                """));
            var firstView = await refunds.DetailAsync(actor, first.ResourceId);
            Assert.Equal("pending", firstView.State);
            Assert.Equal(amountText, firstView.Amount);
            Assert.Equal(chosen.Length, firstView.Sources.Count);
            Assert.Equal(2, firstView.RequiredApprovals);
            Assert.Equal("finance-refund-rule-v1", (await db.Set<RefundApprovalRule>().SingleAsync()).Code);
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE RefundApprovalRule SET SecondApprovalThreshold={0.01m} WHERE Id={firstView.RuleId}"));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT RefundDecision(Id,RefundRequestId,Kind,ActorId,AuthorityLimitSnapshot,RuleId,Reason,DecidedAt,CreatedAt,CreatedBy)
                VALUES({Guid.NewGuid()},{first.ResourceId},'approve',{actor.UserId},10000.00,{firstView.RuleId},
                    'Forged requester self approval',{f.Clock.GetUtcNow()},{f.Clock.GetUtcNow()},{actor.UserId})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT RefundRequest(Id,AgencyId,PolicyId,CreditObligationId,DebtorKind,DebtorId,Currency,Amount,State,RuleId,RequestedBy,RequestedAt,Reason,CreatedAt,CreatedBy,UpdatedAt)
                VALUES({Guid.NewGuid()},{credit.AgencyId},{credit.PolicyId},{credit.Id},{credit.DebtorKind},{Guid.NewGuid()},
                    'GBP',{refundable},'building',{firstView.RuleId},{actor.UserId},{f.Clock.GetUtcNow()},
                    'Forged wrong payee credit request',{f.Clock.GetUtcNow()},{actor.UserId},{f.Clock.GetUtcNow()})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT RefundRequest(Id,AgencyId,PolicyId,CreditObligationId,DebtorKind,DebtorId,Currency,Amount,State,RuleId,RequestedBy,RequestedAt,Reason,CreatedAt,CreatedBy,UpdatedAt)
                VALUES({Guid.NewGuid()},{credit.AgencyId},{credit.PolicyId},{credit.Id},{credit.DebtorKind},{payerId},
                    'GBP',{refundable},'building',{firstView.RuleId},{f.Underwriter.UserId},{f.Clock.GetUtcNow()},
                    'Forged requester without finance authority',{f.Clock.GetUtcNow()},{f.Underwriter.UserId},{f.Clock.GetUtcNow()})
                """));
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT RefundRequest(Id,AgencyId,PolicyId,CreditObligationId,DebtorKind,DebtorId,Currency,Amount,State,RuleId,RequestedBy,RequestedAt,Reason,CreatedAt,CreatedBy,UpdatedAt)
                VALUES({Guid.NewGuid()},{credit.AgencyId},{credit.PolicyId},{credit.Id},{credit.DebtorKind},{payerId},
                    'GBP',{refundable},'pending',{firstView.RuleId},{actor.UserId},{f.Clock.GetUtcNow()},
                    'Forged pending request without cash reservation',{f.Clock.GetUtcNow()},{actor.UserId},{f.Clock.GetUtcNow()})
                """));
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => refunds.DecideAsync(actor,
                first.ResourceId, Version(firstView.Etag), "approve", "Self approval forbidden by rule",
                Key(), Guid.NewGuid()))).Status);

            async Task<ActorContext> Approver(string label)
            {
                var email = $"refund-{label}-{Guid.NewGuid():N}@example.invalid";
                var user = new StaffUser { Email = email, NormalizedEmail = email.ToUpperInvariant(),
                    DisplayName = $"Fictional {label}", State = "active" };
                db.Add(user); await db.SaveChangesAsync();
                var roleId = await db.Set<Role>().Where(x => x.Code == "finance").Select(x => x.Id).SingleAsync();
                db.Add(new UserRole { UserId = user.Id, RoleId = roleId }); await db.SaveChangesAsync();
                return new ActorContext(user.Id, null, null, new HashSet<string>(["finance"]));
            }
            var approver1 = await Approver("approver-one");
            var approver2 = await Approver("approver-two");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RefundRoleAuthority SET [Limit]={0.01m} WHERE RoleCode='finance'");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => refunds.DecideAsync(approver1,
                first.ResourceId, Version(firstView.Etag), "reject", "Insufficient current amount authority",
                Key(), Guid.NewGuid()))).Status);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RefundRoleAuthority SET [Limit]={10000m} WHERE RoleCode='finance'");
            await refunds.DecideAsync(approver1, first.ResourceId, Version(firstView.Etag), "reject",
                "Refund request rejected after review", Key(), Guid.NewGuid());
            Assert.Equal("rejected", (await refunds.DetailAsync(actor, first.ResourceId)).State);
            await using (var probe = await db.Database.BeginTransactionAsync())
            {
                var fakeId = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT RefundRequest(Id,AgencyId,PolicyId,CreditObligationId,DebtorKind,DebtorId,Currency,Amount,State,RuleId,RequestedBy,RequestedAt,Reason,CreatedAt,CreatedBy,UpdatedAt)
                    VALUES({fakeId},{credit.AgencyId},{credit.PolicyId},{credit.Id},{credit.DebtorKind},{payerId},
                        'GBP',{refundable},'building',{firstView.RuleId},{actor.UserId},{f.Clock.GetUtcNow()},
                        'Direct source reservation overcap probe',{f.Clock.GetUtcNow()},{actor.UserId},{f.Clock.GetUtcNow()})
                    """);
                await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT RefundCashReservation(Id,RefundRequestId,AllocationId,Amount,CreatedAt,CreatedBy)
                    VALUES({Guid.NewGuid()},{fakeId},{tinyAllocation},{refundable},{f.Clock.GetUtcNow()},{actor.UserId})
                    """));
                await probe.RollbackAsync();
            }
            var next = await refunds.RequestAsync(actor, credit.Id, amountText, chosen,
                "Re-request after audited rejection", Key(), Guid.NewGuid());
            var current = await refunds.DetailAsync(actor, next.ResourceId);
            var approvalKey = Key();
            var originalVersion = Version(current.Etag);
            await refunds.DecideAsync(approver1, next.ResourceId, originalVersion, "approve",
                "Independent finance approver confirmed cash", approvalKey, Guid.NewGuid());
            current = await refunds.DetailAsync(actor, next.ResourceId);
            await Assert.ThrowsAsync<CommandKeyConflictException>(() => refunds.DecideAsync(approver1,
                next.ResourceId, Version(current.Etag), "approve",
                "Independent finance approver confirmed cash", approvalKey, Guid.NewGuid()));
            if (current.RequiredApprovals == 2)
            {
                Assert.Equal("pending", current.State);
                await refunds.DecideAsync(approver2, next.ResourceId, Version(current.Etag), "approve",
                    "Second independent approver confirmed credit", Key(), Guid.NewGuid());
                current = await refunds.DetailAsync(actor, next.ResourceId);
            }
            Assert.Equal("approved", current.State);
            Assert.Equal(current.RequiredApprovals, current.Decisions.Count(x => x.Kind == "approve"));
            Assert.Empty(await db.Set<FinancePosting>().Where(x => x.SourceKind == "refund").ToArrayAsync());
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE UserRole WHERE UserId={approver1.UserId}");
            Assert.Equal(403, (await Assert.ThrowsAsync<QuoteOperationException>(() => refunds.DecideAsync(approver1,
                next.ResourceId, originalVersion, "approve",
                "Independent finance approver confirmed cash", approvalKey, Guid.NewGuid()))).Status);
            Assert.False(db.Database.HasPendingModelChanges());
        });
    }
}
