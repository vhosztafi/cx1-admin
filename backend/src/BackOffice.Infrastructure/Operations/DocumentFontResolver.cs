using System.Security.Cryptography;
using MigraDoc;
using PdfSharp.Fonts;

namespace BackOffice.Infrastructure.Operations;

public sealed class DocumentFontResolver : IFontResolver
{
    public const string Family = "IBM Plex Sans";
    private static readonly object Gate = new();
    private static readonly Lazy<byte[]> Regular = new(() => Read("IBMPlexSans-Regular.ttf", "975dcda37d80f038dcd143c22e33ca2d97a0cc5a929aace1c749153b0fe1afa5"));
    private static readonly Lazy<byte[]> Bold = new(() => Read("IBMPlexSans-Bold.ttf", "9e6c74a889a700d707613d24548fe4ffa6bc59559a0689d2cf9e133bdcdafb2f"));

    public static void Initialize()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is DocumentFontResolver) return;
            if (GlobalFontSettings.FontResolver is not null) throw new InvalidOperationException("A different PDF font resolver is already registered.");
            _ = Regular.Value; _ = Bold.Value;
            GlobalFontSettings.FontResolver = new DocumentFontResolver();
            PredefinedFontsAndChars.ErrorFontName = Family;
            PredefinedFontsAndChars.RtfDocumentInfoFontName = Family;
            PredefinedFontsAndChars.Bullets.Level1FontName = Family;
            PredefinedFontsAndChars.Bullets.Level2FontName = Family;
            PredefinedFontsAndChars.Bullets.Level3FontName = Family;
            PredefinedFontsAndChars.Bullets.Level1Character = '\u2022';
            PredefinedFontsAndChars.Bullets.Level2Character = '\u2022';
            PredefinedFontsAndChars.Bullets.Level3Character = '\u2022';
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        string.Equals(familyName, Family, StringComparison.OrdinalIgnoreCase) ? new(bold ? "plex-bold" : "plex-regular", false, italic) : null;

    public byte[]? GetFont(string faceName) => faceName switch
    {
        "plex-regular" => Regular.Value.ToArray(), "plex-bold" => Bold.Value.ToArray(), _ => null
    };

    private static byte[] Read(string file, string expectedHash)
    {
        using var stream = typeof(DocumentFontResolver).Assembly.GetManifestResourceStream("DocumentFonts." + file)
            ?? throw new InvalidOperationException("A pinned PDF font is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expectedHash)))
            throw new InvalidOperationException("A pinned PDF font does not match its manifest.");
        return bytes;
    }
}
