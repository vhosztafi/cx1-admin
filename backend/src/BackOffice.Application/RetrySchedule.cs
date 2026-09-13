using System.Security.Cryptography;
using System.Text;

namespace BackOffice.Application;

public static class RetrySchedule
{
    public const int MaximumAttempts = 6;
    public static TimeSpan AfterFailure(int attempt,string operationKey)
    {
        int[] seconds = [5,30,120,600,1800];
        if (attempt < 1 || attempt >= MaximumAttempts) throw new ArgumentOutOfRangeException(nameof(attempt));
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes($"{operationKey}:{attempt}"));
        var milliseconds=seconds[attempt-1]*1000;
        return TimeSpan.FromMilliseconds(milliseconds+(int)(milliseconds*0.1*hash[0]/255));
    }
}
