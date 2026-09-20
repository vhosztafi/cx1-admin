using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BackOffice.Infrastructure.Policies;

public static class CommercialExposureLock
{
    public const string Resource = "CoverMGA.CommercialExposure";

    // Trusted type hint only; scope is checked after acquiring the fence. The
    // statement takes no retained quote/policy/draft locks and discloses no data.
    public static async Task<bool> ForQuoteAsync(BackOfficeDbContext db, Guid quoteId, CancellationToken token = default)
    {
        var commercial = await (from quote in db.Set<Quote>().AsNoTracking() join product in db.Set<Product>().AsNoTracking() on quote.ProductId equals product.Id
            where quote.Id == quoteId && product.Code == "commercial-combined" select quote.Id).AnyAsync(token);
        if (commercial) await AcquireAsync(db, token);
        return commercial;
    }

    public static async Task AcquireAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Commercial exposure lock requires a held transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = Resource; command.Parameters.Add(parameter);
        try
        {
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(token));
            if (result < 0) throw new QuoteOperationException(409, "commercial-exposure-busy");
        }
        catch (SqlException error) when (error.Number is 1205 or -2)
        { throw new QuoteOperationException(409, "commercial-exposure-busy"); }
    }

    public static async Task RequireAsync(BackOfficeDbContext db, CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Commercial exposure writer requires a held transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = "SELECT APPLOCK_MODE('public',@resource,'Transaction');";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = Resource; command.Parameters.Add(parameter);
        if (!string.Equals(await command.ExecuteScalarAsync(token) as string, "Exclusive", StringComparison.Ordinal))
            throw new InvalidOperationException("Acquire the commercial exposure fence before scoped row locks.");
    }
}
