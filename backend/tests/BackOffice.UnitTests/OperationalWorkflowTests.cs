using System.Text.Json;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalWorkflowTests
{
    private static string Rule(string family = "referral", string taskType = "underwriting-referral") => JsonSerializer.Serialize(new
    {
        format = "workflow-task-1", publication = "published", code = "review-referral", family, taskType,
        title = "Review underwriting referral", priority = "high", initialState = "open", dueDays = 2, leadDays = 30,
        checklist = new[] { new { code = "review-source", label = "Review the linked source", required = true } }
    });

    [Theory]
    [InlineData("referral", "underwriting-referral", "quote-referral")]
    [InlineData("referral", "authority-referral", "servicing-referral")]
    [InlineData("missing-information", "servicing", "quote-query")]
    [InlineData("missing-information", "servicing", "servicing-query")]
    [InlineData("missing-information", "servicing", "match-information-request")]
    [InlineData("renewal-reminder", "renewal", "policy-term")]
    [InlineData("agency-follow-up", "agency-onboarding", "agency-follow-up")]
    [InlineData("job-exception", "data-exception", "job-exception")]
    public void PublishedRulesSupportOnlyTheirOwnedSourceFamily(string family, string type, string source)
    {
        var rule = WorkflowTaskRules.Parse(Rule(family, type), "workflow-task/review-referral");
        WorkflowTaskRules.DemandSource(rule, source);
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.DemandSource(rule, "unknown-source"));
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.Parse(Rule(family, "complaint"), "workflow-task/review-referral"));
    }

    [Theory]
    [InlineData("\"publication\":\"published\"", "\"publication\":\"draft\"")]
    [InlineData("\"initialState\":\"open\"", "\"initialState\":\"completed\"")]
    [InlineData("\"dueDays\":2", "\"dueDays\":-1")]
    [InlineData("\"leadDays\":30", "\"leadDays\":366")]
    [InlineData("\"priority\":\"high\"", "\"priority\":\"highest\"")]
    [InlineData("\"required\":true", "\"required\":true,\"completed\":true")]
    public void InvalidOrUnpublishedRulesCannotInventWork(string before, string after)
        => Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.Parse(Rule().Replace(before, after), "workflow-task/review-referral"));

    [Fact]
    public void DuplicateUnknownAndWrongScopeRuleFieldsFailClosed()
    {
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.Parse(Rule().Replace("\"dueDays\":2", "\"dueDays\":2,\"dueDays\":3"), "workflow-task/review-referral"));
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.Parse(Rule().Replace("\"required\":true", "\"required\":true,\"required\":false"), "workflow-task/review-referral"));
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.Parse(Rule(), "workflow-task/some-other-rule"));
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.Parse(Rule().Replace("\"format\":", "\"unexpected\":true,\"format\":"), "workflow-task/review-referral"));
    }

    [Fact]
    public void PollTimeAndRuleVersionAreNotNewSourceEvents()
    {
        var source = Guid.NewGuid(); var rule = WorkflowTaskRules.Parse(Rule(), "workflow-task/review-referral");
        var revised = rule with { Title = "Revised display wording", DueDays = 4 };
        Assert.Equal(WorkflowTaskRules.OperationKey(rule, "quote-referral", source), WorkflowTaskRules.OperationKey(revised, "quote-referral", source));
        Assert.NotEqual(WorkflowTaskRules.OperationKey(rule, "quote-referral", source), WorkflowTaskRules.OperationKey(rule, "quote-referral", Guid.NewGuid()));
        Assert.Throws<TaskRuleException>(() => WorkflowTaskRules.OperationKey(rule, "quote-referral", Guid.Empty));
    }
}
