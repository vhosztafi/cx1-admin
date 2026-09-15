using System.Security.Cryptography;
using System.Text.Json;

namespace BackOffice.Application.Quotes;

public sealed record QuoteRegistrationInput(Guid VehicleId, string NormalizedRegistration);
public sealed record PreparedQuoteCapture(CanonicalQuoteInput Input, string TermIntentJson, IReadOnlyList<QuoteRegistrationInput> Registrations);
public sealed class QuoteValidationException(IReadOnlyList<QuoteFieldIssue> issues) : Exception("Quote input is invalid.")
{
    public IReadOnlyList<QuoteFieldIssue> Issues { get; } = issues;
}

public static class QuoteRules
{
    public static PreparedQuoteCapture Prepare(string? proposal, string trustedProductCode, QuoteVersionPins pins)
    {
        if (trustedProductCode is not ("motor-trade-road-risks" or "motor-trade-combined") || pins.ProductVersionId == Guid.Empty || pins.AgencyTermsVersionId == Guid.Empty)
            throw new InvalidOperationException("Trusted capture identity is unavailable.");
        proposal ??= JsonSerializer.Serialize(new { schemaVersion = pins.SchemaVersion, productCode = trustedProductCode });
        var validated = QuoteCaptureBoundary.Validate(proposal, pins);
        if (validated.Input is null) throw new QuoteValidationException(validated.Issues);
        using var document = JsonDocument.Parse(validated.Input.Json);
        var root = document.RootElement;
        if (root.GetProperty("productCode").GetString() != trustedProductCode)
            throw new QuoteValidationException([new("quote-product-mismatch", "/productCode")]);
        var registrations = new List<QuoteRegistrationInput>();
        if (root.TryGetProperty("risk", out var risk) && risk.TryGetProperty("vehicles", out var vehicles))
            foreach (var vehicle in vehicles.EnumerateArray())
                if (vehicle.TryGetProperty("registration", out var registration))
                {
                    var normalized = registration.GetString()!.Replace(" ", "", StringComparison.Ordinal);
                    // Incomplete declarations remain in the revision. Only searchable
                    // values enter the current projection; this is not readiness.
                    if (normalized.Length >= 2) registrations.Add(new(vehicle.GetProperty("id").GetGuid(), normalized));
                }
        return new(validated.Input, root.TryGetProperty("termIntent", out var intent) ? intent.GetRawText() : "{}", registrations.ToArray());
    }

    public static void EnsureEditable(string state, DateTimeOffset? captureClosedAt)
    {
        if (state != "draft" || captureClosedAt is not null) throw new QuoteInputException("quote-capture-closed");
    }

    public static void EnsureRetainedPins(QuoteVersionPins saved, QuoteVersionPins resolved)
    {
        if (saved != resolved) throw new QuoteInputException("quote-pinned-configuration-unavailable");
    }

    public static bool IsUnchanged(byte[] savedHash, PreparedQuoteCapture prepared) =>
        CryptographicOperations.FixedTimeEquals(savedHash, Convert.FromHexString(prepared.Input.ContentHash));
}
