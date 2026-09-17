namespace BackOffice.Application.Underwriting;

public static class QuoteTermsRules
{
    public static bool AcceptanceWindow(string deliveryState, DateTimeOffset? deliveredAt,
        DateTimeOffset acceptedAt, DateTimeOffset now, DateTimeOffset expiresAt) =>
        deliveryState == "delivered" && deliveredAt is not null && deliveredAt <= acceptedAt &&
        acceptedAt <= now && now < expiresAt && acceptedAt < expiresAt;

    public static bool ValidAcceptanceIdentity(string name, string channel) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 200 && !name.Any(char.IsControl) &&
        channel is "email" or "written" or "telephone";
}
