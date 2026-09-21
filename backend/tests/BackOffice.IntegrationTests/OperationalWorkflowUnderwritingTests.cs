using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Application.Quotes;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Quotes;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task<SettingVersion> WorkflowRule(BackOfficeDbContext db, DateTimeOffset now, string code, string family, string type, int leadDays = 0)
    {
        var definition = new WorkflowTaskDefinition("workflow-task-1", "published", code, family, type, "Review fictional " + family, "normal",
            family == "missing-information" ? "awaiting-information" : "open", 0, leadDays, []);
        var rule = new SettingVersion { Scope = "workflow-task/" + code, Version = 1, EffectiveFrom = now.AddDays(-1),
            Values = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        db.Add(rule); await db.SaveChangesAsync(); return rule;
    }

    [Fact]
    public async Task RealSqlOperationalWorkflowQuoteReferralAndNewQueryEventsRemainDistinct()
    {
        await WithDatabase(async (db, password) =>
        {
            var f = await ReadyUnderwriting(db, password, proposal =>
            {
                var tools = proposal["cover"]!["requestedSections"]![0]!;
                tools["selected"] = true; tools["limit"] = "10000.00"; tools["excess"] = "250.00";
            });
            var tasks = new TaskService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var workflows = new WorkflowTaskService(f.Factory, tasks, f.Clock);
            var referralRule = await WorkflowRule(db, f.Clock.Current, "quote-referral", "referral", "underwriting-referral");
            var queryRule = await WorkflowRule(db, f.Clock.Current, "missing-information", "missing-information", "servicing");
            var referral = await db.Set<QuoteReferral>().AsNoTracking().FirstAsync(x => x.CycleId == f.CycleId);
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => workflows.Reconcile(referralRule.Id, "quote-referral", referral.Id, default)));
            var taskId = Assert.IsType<Guid>(results[0]); Assert.All(results, result => Assert.Equal(taskId, result));
            Assert.Equal(f.QuoteId, (await db.Set<OperationalSubject>().SingleAsync()).QuoteId);
            var decisions = new QuoteReferralService(f.Factory, f.Clock);
            async Task Decide(string outcome)
            {
                referral = await db.Set<QuoteReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id);
                var quote = await db.Set<Quote>().AsNoTracking().SingleAsync(x => x.Id == f.QuoteId);
                await decisions.DecideAsync(f.Underwriter, f.QuoteId, f.CycleId, quote.RowVersion,
                    [new ReferralDecisionInput(referral.Id, referral.RowVersion, outcome, "Fictional workflow source decision",
                        outcome == "query" ? [JsonSerializer.SerializeToElement(new { code = "provide-trading-history" })] : [],
                        outcome == "query" ? "Supply additional trading history" : null)], Guid.NewGuid().ToString(), Guid.NewGuid());
            }
            await Decide("query");
            var firstQuery = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(x => x.ReferralId == referral.Id && x.Outcome == "query");
            var queryResults = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => workflows.Reconcile(queryRule.Id, "quote-query", firstQuery.Id, default)));
            var queryTaskId = Assert.IsType<Guid>(queryResults[0]); Assert.All(queryResults, result => Assert.Equal(queryTaskId, result));
            Assert.NotEqual(taskId, queryTaskId);
            Assert.Equal("awaiting-information", (await db.Set<OperationalTask>().SingleAsync(x => x.Id == queryTaskId)).State);
            await Decide("reopen");
            Assert.Equal(queryTaskId, await workflows.Reconcile(queryRule.Id, "quote-query", firstQuery.Id, default));
            await Decide("query");
            var secondQuery = await db.Set<QuoteReferralDecision>().AsNoTracking().SingleAsync(x => x.ReferralId == referral.Id && x.Outcome == "query" && x.Id != firstQuery.Id);
            Assert.NotEqual(queryTaskId, await workflows.Reconcile(queryRule.Id, "quote-query", secondQuery.Id, default));
            Assert.Equal(taskId, await new WorkflowTaskService(f.Factory, tasks, f.Clock).Reconcile(referralRule.Id, "quote-referral", referral.Id, default));
            Assert.Equal(3, await db.Set<WorkflowTaskBinding>().CountAsync());
            Assert.Equal("awaiting-information", (await db.Set<OperationalTask>().AsNoTracking().SingleAsync(x => x.Id == queryTaskId)).State);
            var historical = await db.Set<WorkflowTaskBinding>().SingleAsync(x => x.TaskId == queryTaskId);
            Assert.Equal(firstQuery.Id, historical.QuoteQueryDecisionId); Assert.Contains(firstQuery.Question!, historical.SourceSnapshotJson);
        });
    }

    [Fact]
    public async Task RealSqlOperationalWorkflowRenewalReminderUsesIssuedTermAndDueWindow()
    {
        await WithDatabase(async (db, password) =>
        {
            var setup = await AcceptedIssue(db, password); var f = setup.Source;
            await new QuoteIssueService(f.Factory, f.Clock).IssueAsync(f.Underwriter, f.QuoteId, setup.Version, setup.Input, Guid.NewGuid().ToString(), Guid.NewGuid());
            var term = await db.Set<PolicyTerm>().AsNoTracking().SingleAsync();
            var rule = await WorkflowRule(db, f.Clock.Current, "renewal-reminder", "renewal-reminder", "renewal", 30);
            var tasks = new TaskService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
            var workflows = new WorkflowTaskService(f.Factory, tasks, f.Clock);
            f.Clock.Current = term.EndsAt.AddDays(-31);
            Assert.Null(await workflows.Reconcile(rule.Id, "policy-term", term.Id, default));
            Assert.Empty(await db.Set<OperationalTask>().ToArrayAsync());
            f.Clock.Current = term.EndsAt.AddDays(-29);
            var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => workflows.Reconcile(rule.Id, "policy-term", term.Id, default)));
            var taskId = Assert.IsType<Guid>(results[0]); Assert.All(results, result => Assert.Equal(taskId, result));
            var binding = await db.Set<WorkflowTaskBinding>().SingleAsync(); Assert.Equal(term.Id, binding.PolicyTermId);
            Assert.Equal(term.PolicyId, (await db.Set<OperationalSubject>().SingleAsync()).PolicyId);
            Assert.Equal(taskId, await new WorkflowTaskService(f.Factory, tasks, f.Clock).Reconcile(rule.Id, "policy-term", term.Id, default));
            Assert.Single(await db.Set<OperationalTaskEvent>().ToArrayAsync());
        });
    }
}
