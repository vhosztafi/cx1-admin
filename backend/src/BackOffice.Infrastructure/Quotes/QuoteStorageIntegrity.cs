using BackOffice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Quotes;

// Internal integrity diagnostic, not an unscoped API. SQL permits a null pointer
// during creation; a privileged writer could commit one outside the service.
public static class QuoteStorageIntegrity
{
    public static Task<List<Guid>> MissingCurrentRevisionsAsync(BackOfficeDbContext db, CancellationToken cancellationToken = default) =>
        db.Set<Quote>().AsNoTracking().Where(x => x.CurrentRevisionId == null).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(cancellationToken);
}
