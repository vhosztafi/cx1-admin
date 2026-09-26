using System.Text.Json;
using BackOffice.Infrastructure.Persistence;
using BackOffice.Infrastructure.Quotes;
using Microsoft.EntityFrameworkCore;

namespace BackOffice.Infrastructure.Administration;

public sealed record OrganisationConfiguration(string Name, string ClientReferencePrefix, bool NotificationsEnabled, string NotificationSignature);
public sealed record FlagConfiguration(bool Enabled, int MaximumReviewDays, bool AgencySharingAllowed);
public sealed record MessageTemplateConfiguration(string Name, string Body, bool Enabled);

public static class AdministrativeConfiguration
{
    public static readonly OrganisationConfiguration DefaultOrganisation = new("Cover MGA", "CN", true, "");
    public static readonly FlagConfiguration DefaultFlag = new(true, 3650, true);
    public static readonly string[] FlagTypes = ["vulnerability", "third-party-authority", "financial-difficulty", "accessible-format", "interpreter-required", "deceased-or-business-ceased"];
    public static async Task<OrganisationConfiguration> Organisation(BackOfficeDbContext db, DateTimeOffset now, CancellationToken ct = default)
        => await Read<OrganisationConfiguration>(db, "organisation", now, ct) ?? DefaultOrganisation;
    public static async Task<FlagConfiguration> Flag(BackOfficeDbContext db, string code, DateTimeOffset now, CancellationToken ct = default)
        => await Read<FlagConfiguration>(db, "flag-definition/" + code, now, ct) ?? DefaultFlag;
    private static async Task<T?> Read<T>(BackOfficeDbContext db, string scope, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Set<SettingVersion>().AsNoTracking().Where(x => x.Scope == scope && x.EffectiveFrom <= now)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        return row is null ? default : JsonSerializer.Deserialize<T>(row.Values, ProductAdministration.Json)
            ?? throw new QuoteOperationException(503, "administration-configuration-unavailable");
    }
    public static async Task DemandFlag(BackOfficeDbContext db, string code, DateOnly review, bool sharing, DateTimeOffset now, CancellationToken ct)
    {
        var config = await Flag(db, code, now, ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Europe/London").DateTime);
        if (!config.Enabled || sharing && !config.AgencySharingAllowed || review > today.AddDays(config.MaximumReviewDays))
            throw new Parties.SupportFlagOperationException(409, "support-flag-configuration-denied");
    }
}
