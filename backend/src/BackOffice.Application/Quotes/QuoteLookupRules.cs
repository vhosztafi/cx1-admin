using System.Security.Cryptography;
using System.Text.Json;
using static BackOffice.Application.Quotes.QuoteSectionValues;

namespace BackOffice.Application.Quotes;

public sealed record QuoteLookupTarget(string Kind, string Scope, Guid? RiskItemId = null);
public sealed record QuoteLookupInput(QuoteLookupTarget Target, string Query, string InputFingerprint);

// Pure input rules only. The command service must first hold current quote authority,
// validate the revision/ETag and resolve the saved version pins. No provider is called here.
public static class QuoteLookupRules
{
    public static QuoteLookupInput Prepare(JsonElement proposal, QuoteLookupTarget target, QuoteVersionPins pins)
    {
        var supported = target.Kind switch
        {
            "address" => target.Scope is "insured" or "driver" or "premises",
            "vehicle" => target.Scope == "vehicle",
            "licence" => target.Scope == "driver",
            _ => false
        };
        if (!supported) throw new QuoteInputException("lookup-target-unsupported");
        JsonElement subject;
        if (target.Scope == "insured")
        {
            if (target.RiskItemId is not null) throw new QuoteInputException("lookup-item-inapplicable");
            subject = At(proposal, "insured");
        }
        else
        {
            if (target.RiskItemId is null || target.RiskItemId == Guid.Empty)
                throw new QuoteInputException("lookup-item-required");
            var collection = target.Scope switch { "driver" => "drivers", "vehicle" => "vehicles", _ => "premises" };
            var matches = Items(At(proposal, "risk." + collection)).Where(row =>
                Guid.TryParse(Text(At(row, "id")), out var id) && id == target.RiskItemId).ToArray();
            if (matches.Length != 1) throw new QuoteInputException("lookup-item-not-in-proposal");
            subject = matches[0];
        }
        if (subject.ValueKind != JsonValueKind.Object) throw new QuoteInputException("lookup-input-required");
        var path = target.Kind switch { "address" => "address.postcode", "vehicle" => "registration", _ => "licence.number" };
        var raw = Text(At(subject, path));
        var maximum = target.Kind switch { "address" => 10, "vehicle" => 12, _ => 40 };
        if (raw is null || raw.Length > maximum || raw.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ')))
            throw new QuoteInputException("lookup-query-invalid");
        var query = raw.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var minimum = target.Kind switch { "address" => 3, "vehicle" => 2, _ => 5 };
        if (query.Length < minimum) throw new QuoteInputException("lookup-query-invalid");

        // Whole-subject fingerprints deliberately invalidate a result after any edit
        // to that subject. Property ordering and unrelated row ordering are immaterial.
        var envelope = JsonSerializer.Serialize(new { format = "quote-lookup-input-1", target.Kind,
            target.Scope, target.RiskItemId, query, subject });
        return new(target, query, QuoteCanonicalJson.Create(envelope, pins).ContentHash);
    }

    public static QuoteLookupInput EnsureCurrent(JsonElement proposal, QuoteLookupTarget target,
        QuoteVersionPins pins, string expectedFingerprint)
    {
        var current = Prepare(proposal, target, pins);
        if (expectedFingerprint.Length != 64 || !expectedFingerprint.All(Uri.IsHexDigit) ||
            !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(current.InputFingerprint), Convert.FromHexString(expectedFingerprint)))
            throw new QuoteInputException("lookup-input-changed");
        return current;
    }

    public static string ManualReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000 || reason.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new QuoteInputException("lookup-manual-reason-required");
        return reason.Trim();
    }

    public static string Scenario(string value) => value is "success" or "no-match" or "multiple" or "reject" or "fail-once" or "timeout-after-success"
        ? value : throw new QuoteInputException("lookup-scenario-unsupported");
}
