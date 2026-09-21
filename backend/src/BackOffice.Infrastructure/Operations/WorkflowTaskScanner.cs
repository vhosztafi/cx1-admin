using BackOffice.Application.Operations;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Operations;

public sealed record WorkflowScanIssue(Guid RuleVersionId, string Code, string? SourceKind = null, Guid? SourceEventId = null);
public sealed record WorkflowScanResult(int Examined, int Associated, IReadOnlyList<WorkflowScanIssue> Issues);

public sealed class WorkflowTaskScanner(IDbContextFactory<BackOfficeDbContext> factory, WorkflowTaskService workflows, TimeProvider time)
{
    public async Task<WorkflowScanResult> Scan(IDictionary<string, int> offsets, CancellationToken token)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var now = time.GetUtcNow(); var examined = 0; var associated = 0; var issues = new List<WorkflowScanIssue>();
        var rules = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope.StartsWith("workflow-task/") && x.EffectiveFrom <= now &&
            !db.Set<SettingVersion>().Any(other => other.Scope == x.Scope && other.EffectiveFrom <= now && (other.EffectiveFrom > x.EffectiveFrom || other.EffectiveFrom == x.EffectiveFrom && other.Version > x.Version)))
            .OrderBy(x => x.Scope).ToArrayAsync(token);
        foreach (var version in rules)
        {
            WorkflowTaskDefinition rule;
            try { rule = WorkflowTaskRules.Parse(version.Values, version.Scope); }
            catch (TaskRuleException) { issues.Add(new(version.Id, "invalid-workflow-rule")); continue; }
            string[] kinds = rule.Family switch
            {
                "referral" => ["quote-referral", "servicing-referral"],
                "missing-information" => ["quote-query", "servicing-query", "match-information-request"],
                "renewal-reminder" => ["policy-term"], "agency-follow-up" => ["agency-follow-up"], "job-exception" => ["job-exception"], _ => []
            };
            foreach (var kind in kinds)
            {
                var key = rule.Code + "/" + kind; var offset = offsets.TryGetValue(key, out var saved) && saved >= 0 ? saved : 0;
                IQueryable<Guid> query = kind switch
                {
                    "quote-referral" => db.Set<QuoteReferral>().Select(x => x.Id),
                    "servicing-referral" => db.Set<ServicingReferral>().Select(x => x.Id),
                    "quote-query" => db.Set<QuoteReferralDecision>().Where(x => x.Outcome == "query").Select(x => x.Id),
                    "servicing-query" => db.Set<ServicingReferralDecision>().Where(x => x.Outcome == "query").Select(x => x.Id),
                    "match-information-request" => db.Set<MatchInformationRequest>().Select(x => x.Id),
                    "policy-term" => db.Set<PolicyTerm>().Select(x => x.Id),
                    "agency-follow-up" => db.Set<AgencyFollowUp>().Select(x => x.Id),
                    _ => db.Set<JobException>().Select(x => x.Id)
                };
                var ids = await query.OrderBy(x => x).Skip(offset).Take(64).ToArrayAsync(token);
                offsets[key] = ids.Length < 64 || offset > int.MaxValue - 64 ? 0 : offset + 64;
                foreach (var id in ids)
                {
                    examined++;
                    try { if (await workflows.Reconcile(version.Id, kind, id, token) is not null) associated++; }
                    catch (OperationalAccessException error) { issues.Add(new(version.Id, error.Code, kind, id)); }
                    catch (TaskRuleException) { issues.Add(new(version.Id, "invalid-workflow-rule", kind, id)); }
                    catch (QuoteOperationException) { issues.Add(new(version.Id, "workflow-source-unavailable", kind, id)); }
                }
            }
        }
        return new(examined, associated, issues);
    }
}
