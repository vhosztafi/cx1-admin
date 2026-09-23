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
    private static async Task AssertRetainedMidDowngradeRefused(BackOfficeDbContext db, string target)
    {
        async Task<string> Snapshot() => JsonSerializer.Serialize(new
        {
            intents = await db.Set<PolicyMidIntent>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            submissions = await db.Set<MidSubmission>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            results = await db.Set<MidResult>().AsNoTracking().OrderBy(x => x.Id).ToListAsync()
        });
        var before = await Snapshot();
        Assert.NotEmpty(await db.Set<PolicyMidIntent>().AsNoTracking().Where(x => x.Purpose == "new-business").ToListAsync());
        var migrator = db.GetService<IMigrator>();
        try
        {
            var failure = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(target));
            Assert.Equal(52024, failure.Number);
            Assert.Contains("Retained MID history prevents downgrade.", failure.Message);
        }
        finally
        {
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
        }
        Assert.Equal(before, await Snapshot());
    }

    private static async Task VerifyRetainedTemplateDowngradeProtection(BackOfficeDbContext db, string target, bool requiresTemplateGuard = true)
    {
        // A current Motor Trade issue also owns immutable initial MID work.
        // That newer retention guard must reject before the older template
        // constraint. Neither guard permits deleting history to test a downgrade.
        var templates = await db.Set<TemplateVersion>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Contains(templates, x => x.Kind is "servicing-terms" or "renewal-invitation");
        var before = JsonSerializer.Serialize(templates);
        var midIntents = await db.Set<PolicyMidIntent>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var midSubmissions = await db.Set<MidSubmission>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var midResults = await db.Set<MidResult>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var retainedMid = midIntents.Any(x => x.Purpose == "new-business") || midSubmissions.Count > 0 || midResults.Count > 0;
        var midBefore = JsonSerializer.Serialize(new { midIntents, midSubmissions, midResults });
        var migrator = db.GetService<IMigrator>();
        try
        {
            if (retainedMid || requiresTemplateGuard)
            {
                var failure = await Assert.ThrowsAsync<SqlException>(() => migrator.MigrateAsync(target));
                Assert.Equal(retainedMid ? 52024 : 547, failure.Number);
                Assert.Contains(retainedMid ? "Retained MID history prevents downgrade." : "CK_TemplateVersion_Kind", failure.Message);
            }
            else await migrator.MigrateAsync(target);
        }
        finally
        {
            await migrator.MigrateAsync();
            db.ChangeTracker.Clear();
        }
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(before, JsonSerializer.Serialize(await db.Set<TemplateVersion>().AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
        Assert.Equal(midBefore, JsonSerializer.Serialize(new
        {
            midIntents = await db.Set<PolicyMidIntent>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            midSubmissions = await db.Set<MidSubmission>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            midResults = await db.Set<MidResult>().AsNoTracking().OrderBy(x => x.Id).ToListAsync()
        }));
    }
}
