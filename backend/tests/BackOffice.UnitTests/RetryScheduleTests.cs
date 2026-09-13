using BackOffice.Application;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class RetryScheduleTests
{
    [Theory]
    [InlineData(1,5)][InlineData(2,30)][InlineData(3,120)][InlineData(4,600)][InlineData(5,1800)]
    public void RetryWindowsAreBoundedAndRepeatable(int attempt,int seconds)
    {
        var first=RetrySchedule.AfterFailure(attempt,"stable-operation");
        Assert.Equal(first,RetrySchedule.AfterFailure(attempt,"stable-operation"));
        Assert.InRange(first,TimeSpan.FromSeconds(seconds),TimeSpan.FromSeconds(seconds*1.1));
    }
    [Theory]
    [InlineData(0)][InlineData(6)][InlineData(7)]
    public void NoDelayIsReturnedBeyondTheAttemptBudget(int attempt) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RetrySchedule.AfterFailure(attempt,"stable-operation"));
}
