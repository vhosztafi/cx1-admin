using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalCancellationTests
{
    private static readonly DateTimeOffset Effective = new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(1));
    [Theory]
    [InlineData("certificate-withdrawal", -1, false)]
    [InlineData("certificate-withdrawal", 0, true)]
    [InlineData("certificate-withdrawal", 1, true)]
    [InlineData("task-close", -1, false)]
    [InlineData("task-close", 0, true)]
    [InlineData("task-close", 1, true)]
    [InlineData("notice", -1, true)]
    [InlineData("unknown", 1, false)]
    [InlineData("mid-removal", 1, false)]
    public void ConsequencesRespectTheExactInstantAndMidHasItsOwnWorker(string kind, int ticks, bool expected)
        => Assert.Equal(expected, CancellationOperationsRules.CanApply(kind, Effective, Effective.ToUniversalTime().AddTicks(ticks)));

    [Theory]
    [InlineData("policy-term", "renewal", "open", false, true, true)]
    [InlineData("policy-term", "renewal", "blocked", false, true, true)]
    [InlineData("policy-term", "renewal", "in-progress", false, true, true)]
    [InlineData("policy-term", "renewal", "awaiting-information", false, true, true)]
    [InlineData("policy-term", "renewal", "completed", false, true, false)]
    [InlineData("policy-term", "renewal", "cancelled", false, true, false)]
    [InlineData("policy-term", "renewal", "open", true, true, false)]
    [InlineData("policy-term", "renewal", "open", false, false, false)]
    [InlineData(null, "renewal", "open", false, true, false)]
    [InlineData("job-exception", "data-exception", "open", false, true, false)]
    [InlineData("policy-term", "complaint", "open", false, true, false)]
    [InlineData("policy-term", "renewal", "unknown", false, true, false)]
    public void OnlyUnchangedLiveRenewalTasksBoundToTheCancelledTermCanClose(string? source, string type,
        string state, bool changed, bool sameTerm, bool expected)
    {
        var term = Guid.NewGuid();
        Assert.Equal(expected, CancellationOperationsRules.CanCloseRenewal(term, sameTerm ? term : Guid.NewGuid(), source, type, state, changed));
    }

    [Theory][InlineData(true, false)][InlineData(false, true)]
    public void LegacyNoticeEvidenceNeverCausesAutomaticResending(bool legacy, bool expected)
        => Assert.Equal(expected, CancellationOperationsRules.NeedsNoticeDelivery(legacy));
}
