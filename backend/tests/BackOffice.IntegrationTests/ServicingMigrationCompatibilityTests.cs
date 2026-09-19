using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed partial class UnderwritingRuntimeTests
{
    private static async Task VerifyRetainedTemplateDowngradeProtection(BackOfficeDbContext db, string target)
    {
        // Latest demo configuration contains immutable templates that an older
        // schema cannot represent. Reject that downgrade, then restore latest.
        var templates = await db.Set<TemplateVersion>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Contains(templates, x => x.Kind is "servicing-terms" or "renewal-invitation");
        var before = JsonSerializer.Serialize(templates);
        var migrator = db.GetService<IMigrator>();
        var failure = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(target));
        Assert.Equal(547, failure.Number);
        Assert.Contains("CK_TemplateVersion_Kind", failure.Message);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(before, JsonSerializer.Serialize(await db.Set<TemplateVersion>().AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
    }
}
