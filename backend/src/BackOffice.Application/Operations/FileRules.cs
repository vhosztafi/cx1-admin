using System.Globalization;

namespace BackOffice.Application.Operations;

public sealed class FileRuleException(string code) : Exception("The file is not accepted.")
{
    public string Code { get; } = code;
}

public sealed record OperationalFileDescription(string Name, string MediaType, long ByteLength);

// Deterministic demo signature screening, not antivirus scanning or a claim
// that the supplied document proves any insurance declaration.
public static class FileRules
{
    public const int MaximumFileBytes = 20 * 1024 * 1024;
    public const int HeaderBytes = 32;
    public const int TrailerBytes = 1024;

    public static void ValidateNameAndType(string name, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != name.Trim() || name.StartsWith('.') || name.EndsWith('.') ||
            name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Surrogate || "/\\:<>\"|?*".Contains(c)))
            throw new FileRuleException("file-name-invalid");
        var stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]))
            throw new FileRuleException("file-name-invalid");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!(mediaType switch
        {
            "application/pdf" => extension == ".pdf",
            "image/png" => extension == ".png",
            "image/jpeg" => extension is ".jpg" or ".jpeg",
            _ => false
        })) throw new FileRuleException("file-media-invalid");
    }

    public static OperationalFileDescription Validate(string name, string mediaType, long actualLength,
        ReadOnlySpan<byte> header, ReadOnlySpan<byte> trailer, int maximumBytes = MaximumFileBytes)
    {
        if (maximumBytes is < 1 or > MaximumFileBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        ValidateNameAndType(name, mediaType);
        if (actualLength < 1 || actualLength > maximumBytes || header.Length > actualLength || trailer.Length > actualLength)
            throw new FileRuleException("file-size-invalid");
        var valid = mediaType switch
        {
            "application/pdf" => Pdf(header, trailer) && actualLength >= 15,
            "image/png" => actualLength >= 45 && header.Length >= 24 &&
                header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82 }) &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[16..20]) > 0 &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[20..24]) > 0 &&
                trailer.EndsWith(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }),
            "image/jpeg" => actualLength >= 5 && header.StartsWith(new byte[] { 255, 216, 255 }) && trailer.EndsWith(new byte[] { 255, 217 }),
            _ => false
        };
        if (!valid) throw new FileRuleException("file-signature-invalid");
        return new(name, mediaType, actualLength);
    }

    private static bool Pdf(ReadOnlySpan<byte> header, ReadOnlySpan<byte> trailer)
    {
        if (header.Length < 9 || header[8] is not (10 or 13) ||
            !(header.StartsWith("%PDF-1."u8) && header[7] is >= (byte)'0' and <= (byte)'7' || header.StartsWith("%PDF-2.0"u8))) return false;
        var end = trailer.Length;
        while (end > 0 && trailer[end - 1] is 9 or 10 or 12 or 13 or 32) end--;
        return trailer[..end].EndsWith("%%EOF"u8);
    }
}
