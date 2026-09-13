using BackOffice.Application;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class JobRetryBudgetTests
{
    [Theory]
    [InlineData("failed", "provider-timeout", 6, 6, 12)]
    [InlineData("failed", "provider-unavailable", 12, 12, 18)]
    [InlineData("failed", "attempts-exhausted", 6, 6, 12)]
    [InlineData("failed", "provider-timeout", 18, 18, null)]
    [InlineData("failed", "provider-rejected", 6, 6, null)]
    [InlineData("failed", "provider-conflict", 6, 6, null)]
    [InlineData("failed", "invalid-payload", 6, 6, null)]
    [InlineData("pending", "provider-timeout", 6, 6, null)]
    [InlineData("succeeded", "provider-timeout", 6, 6, null)]
    [InlineData("failed", "provider-timeout", 5, 6, null)]
    public void OnlyExhaustedRecoverableFailuresGetABoundedNewCycle(string state, string error, int attempts, int limit, int? expected) =>
        Assert.Equal(expected, JobRetryBudget.ExpandedLimit(state, error, attempts, limit));

    [Fact]
    public void RecoveryCycleRetainsBoundedDelaysWithoutResettingAttemptNumbers()
    {
        Assert.Equal(JobRetryBudget.Delay(1, "stable"), JobRetryBudget.Delay(7, "stable"));
        Assert.Equal(JobRetryBudget.Delay(5, "stable"), JobRetryBudget.Delay(17, "stable"));
    }
}
