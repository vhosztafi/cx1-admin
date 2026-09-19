using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    [Fact]
    public async Task RealSqlCancellationPostingCalculatesOriginalComponentReturnsWithExactRounding()
    {
        await WithDatabase(async(db,_)=>
        {
            var london=TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
            DateTimeOffset Day(string date)=>new(TimeZoneInfo.ConvertTimeToUtc(DateTime.Parse(date),london));
            async Task<decimal?> Return(decimal amount,string start,string end,string effective,string code="premium")=>
                await db.Database.SqlQuery<decimal?>($"SELECT dbo.CancellationReturnAmount({amount},{Day(start)},{Day(end)},{Day(effective)},{code}) AS Value").SingleAsync();
            Assert.Equal(-355.07m,await Return(1200,"2026-01-01","2027-01-01","2026-09-15"));
            Assert.Equal(-42.61m,await Return(144,"2026-01-01","2027-01-01","2026-09-15","tax"));
            Assert.Equal(-35.51m,await Return(120,"2026-01-01","2027-01-01","2026-09-15","commission"));
            Assert.Equal(64.11m,await Return(-75.62m,"2026-10-01","2027-01-01","2026-10-15"));
            Assert.Equal(-276m,await Return(366,"2024-01-01","2025-01-01","2024-03-31"));
            Assert.Equal(-66m,await Return(366,"2024-01-01","2025-01-01","2024-10-27"));
            Assert.Equal(-0.01m,await Return(0.01m,"2026-01-01","2026-01-03","2026-01-02"));
            Assert.Equal(0.01m,await Return(-0.01m,"2026-01-01","2026-01-03","2026-01-02"));
            Assert.Equal(-9999999999999.99m,await Return(9999999999999.99m,"2026-01-01","2027-01-01","2026-01-01"));
            Assert.Equal(0m,await Return(35,"2026-01-01","2027-01-01","2026-09-15","fee"));
            Assert.Equal(0m,await Return(7,"2026-01-01","2027-01-01","2026-09-15","fee-share"));
            Assert.Equal(0m,await Return(100,"2026-01-01","2026-03-01","2026-09-15"));
            Assert.Null(await Return(100,"2026-01-01","2027-01-01","2025-12-31"));
            Assert.Null(await Return(100,"2026-01-01","2026-01-01","2026-01-01"));
            Assert.Null(await Return(100,"2026-01-01","2027-01-01","2026-09-15","unknown"));
        });
    }
}
