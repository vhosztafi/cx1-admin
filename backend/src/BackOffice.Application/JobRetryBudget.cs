namespace BackOffice.Application;

public static class JobRetryBudget
{
    public const int InitialLimit = 6;
    public const int MaximumLimit = 18;

    // Two explicitly authorized recovery cycles; history and provider identity survive.
    public static int? ExpandedLimit(string state, string? errorCode, int attempts, int limit) =>
        state == "failed" && errorCode is "provider-unavailable" or "provider-timeout" or "attempts-exhausted" &&
        limit is InitialLimit or 12 && attempts == limit ? limit + InitialLimit : null;

    public static TimeSpan Delay(int attempt, string operationKey) => RetrySchedule.AfterFailure(
        Math.Min(((attempt - 1) % InitialLimit) + 1, InitialLimit - 1), operationKey);
}
