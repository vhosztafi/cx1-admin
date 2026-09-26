using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using BackOffice.Infrastructure.Persistence;

namespace BackOffice.Infrastructure.Parties;

public static class ClientReferences
{
    public static async Task<string> NextAsync(BackOfficeDbContext db,CancellationToken token = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Allocate a client reference inside its creation transaction.");
        await using var command=db.Database.GetDbConnection().CreateCommand();
        command.Transaction=db.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText="SELECT NEXT VALUE FOR [ClientReferenceSequence]";
        var number=Convert.ToInt64(await command.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);
        // D7 pads short values but never truncates a longer sequence value. Gaps after rollback are intentional.
        var organisation = await Administration.AdministrativeConfiguration.Organisation(db, DateTimeOffset.UtcNow, token);
        return organisation.ClientReferencePrefix+"-"+number.ToString("D7",CultureInfo.InvariantCulture);
    }
}
