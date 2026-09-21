using System.Text.Json;
using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public static class WorkflowTaskSeed
{
    // Preserve every existing scope, including intentionally draft definitions.
    public static async Task SeedAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Workflow seed requires the held demo seed transaction.");
        WorkflowTaskDefinition[] rules =
        [
            new("workflow-task-1", "published", "demo-referral-review", "referral", "underwriting-referral", "Review underwriting referral", "high", "open", 1, 0,
                [new("source", "Review the linked referral and its authority requirements", true), new("decision", "Record the decision in the linked record", true)]),
            new("workflow-task-1", "published", "demo-information-follow-up", "missing-information", "servicing", "Follow up requested information", "normal", "awaiting-information", 3, 0,
                [new("request", "Review the information requested", true), new("evidence", "Record and review the supplied evidence in the linked record", true)]),
            new("workflow-task-1", "published", "demo-renewal-reminder", "renewal-reminder", "renewal", "Prepare policy renewal", "normal", "open", 0, 30,
                [new("term", "Review the expiring policy term", true), new("renewal", "Record renewal follow-up in the policy", true)]),
            new("workflow-task-1", "published", "demo-agency-follow-up", "agency-follow-up", "agency-onboarding", "Review agency follow-up", "normal", "open", 0, 14,
                [new("agency", "Review the agency obligation and current evidence", true)]),
            new("workflow-task-1", "published", "demo-job-exception", "job-exception", "data-exception", "Review processing exception", "high", "open", 1, 0,
                [new("failure", "Review the failed operation in its linked record", true), new("outcome", "Record the recovery or follow-up outcome", true)])
        ];
        foreach (var rule in rules)
        {
            var scope = "workflow-task/" + rule.Code; WorkflowTaskRules.Validate(rule, scope);
            if (!await db.Set<SettingVersion>().AnyAsync(x => x.Scope == scope, token))
                db.Add(new SettingVersion { Scope = scope, Version = 1, EffectiveFrom = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                    Values = JsonSerializer.Serialize(rule, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
        }
        await db.SaveChangesAsync(token);
    }
}
