using System.Text.Json;
using BackOffice.Infrastructure.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Platform;
using BackOffice.Infrastructure.Policies;
using BackOffice.Infrastructure.Underwriting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public Task RealSqlOperationalWorkflowServicingReferralAndQueryUseOwnedDraft()
        => RunServicingRatingRequests("motor-trade-road-risks", "referral-generation", workflowTasks: true);

    private static async Task VerifyOperationalWorkflowServicing(BackOfficeDbContext db, DecisionFixture f, ServicingCycle cycle, string etag)
    {
        var tasks = new TaskService(f.Factory, new SqlCommandBoundary(f.Factory, f.Clock), f.Clock);
        var workflows = new WorkflowTaskService(f.Factory, tasks, f.Clock);
        var referralRule = await WorkflowRule(db, f.Clock.Current, "servicing-referral", "referral", "underwriting-referral");
        var queryRule = await WorkflowRule(db, f.Clock.Current, "servicing-information", "missing-information", "servicing");
        var referral = await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x => x.CycleId == cycle.Id && x.RuleCode == "cover-tools-equipment");
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => workflows.Reconcile(referralRule.Id, "servicing-referral", referral.Id, default)));
        var referralTask = Assert.IsType<Guid>(results[0]); Assert.All(results, result => Assert.Equal(referralTask, result));
        Assert.Equal(cycle.DraftId, (await db.Set<OperationalSubject>().SingleAsync()).ServicingDraftId);
        static byte[] Version(string value) => Convert.FromBase64String(value.Trim('"'));
        var drafts = new ServicingDraftService(f.Factory, f.Clock);
        var takeover = await drafts.LeaseAsync(f.Underwriter, cycle.DraftId, Version(etag), "takeover", null, "Review fictional servicing request", Guid.NewGuid().ToString(), Guid.NewGuid());
        var fence = JsonSerializer.Deserialize<JsonElement>(takeover.Body).GetProperty("lease").GetProperty("leaseToken").GetGuid();
        var decisions = new ServicingReferralService(f.Factory, f.Clock);
        var query = new ReferralDecisionInput(referral.Id, referral.RowVersion, "query", "Request fictional trading history",
            [JsonSerializer.SerializeToElement(new { code = "provide-trading-history" })], "Confirm current trading history");
        var queried = await decisions.DecideAsync(f.Underwriter, cycle.DraftId, cycle.Id, Version(takeover.Etag!), fence, [query], Guid.NewGuid().ToString(), Guid.NewGuid());
        var decision = await db.Set<ServicingReferralDecision>().SingleAsync(x => x.ReferralId == referral.Id && x.Outcome == "query");
        var queryTask = Assert.IsType<Guid>(await workflows.Reconcile(queryRule.Id, "servicing-query", decision.Id, default));
        Assert.NotEqual(referralTask, queryTask);
        referral = await db.Set<ServicingReferral>().AsNoTracking().SingleAsync(x => x.Id == referral.Id);
        await decisions.DecideAsync(f.Underwriter, cycle.DraftId, cycle.Id, Version(queried.Etag!), fence,
            [new(referral.Id, referral.RowVersion, "reopen", "Review supplied fictional information", [])], Guid.NewGuid().ToString(), Guid.NewGuid());
        Assert.Equal(queryTask, await workflows.Reconcile(queryRule.Id, "servicing-query", decision.Id, default));
        using var view = JsonDocument.Parse((await tasks.Read(f.Underwriter, queryTask, default)).Body);
        Assert.Equal("resolved", view.RootElement.GetProperty("workflow").GetProperty("sourceCondition").GetString());
        Assert.Equal("awaiting-information", view.RootElement.GetProperty("state").GetString());
        Assert.Equal(decision.Id, (await db.Set<WorkflowTaskBinding>().SingleAsync(x => x.TaskId == queryTask)).ServicingQueryDecisionId);
    }
}
