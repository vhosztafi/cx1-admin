using Microsoft.EntityFrameworkCore;
using BackOffice.Infrastructure.Persistence;

namespace BackOffice.Infrastructure.Operations;

public sealed partial class FileService
{
    public async Task<int> CleanupExpiredTemporary(CancellationToken token)
    {
        var cutoff = time.GetUtcNow().AddHours(-24); var deleted = 0;
        foreach (var id in store.ExpiredTemporary(cutoff, 100))
        {
            token.ThrowIfCancellationRequested();
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            // Same lock as pending-metadata insertion. An old unreferenced
            // stage cannot be deleted in the middle of becoming referenced.
            await Lock(db, $"CoverMGA.File.{id:N}", token);
            if (await store.DeleteExpiredTemporary(id, cutoff, ct => db.Set<FileObject>().AnyAsync(x => x.Id == id, ct), token)) deleted++;
            await transaction.CommitAsync(token);
        }
        return deleted;
    }
}
