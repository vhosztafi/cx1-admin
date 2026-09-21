using System.Text;
using BackOffice.Application.Operations;
using Xunit;

namespace BackOffice.UnitTests;

public sealed class OperationalFileTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n");

    [Theory]
    [InlineData("../policy.pdf")]
    [InlineData("folder\\policy.pdf")]
    [InlineData("C:policy.pdf")]
    [InlineData("policy.pdf:stream")]
    [InlineData("policy.pdf\r\nX-Header: injected")]
    [InlineData(" policy.pdf")]
    [InlineData("policy.pdf ")]
    [InlineData(".policy.pdf")]
    [InlineData("policy.pdf.")]
    [InlineData("CON.pdf")]
    [InlineData("lpt1.pdf")]
    [InlineData("policy\u202Efdp.pdf")]
    public void UnsafeDownloadNamesAreRejected(string name)
        => Assert.Throws<FileRuleException>(() => FileRules.Validate(name, "application/pdf", Pdf.Length, Pdf, Pdf));

    [Fact]
    public void ActualAllowedSignaturesAndCanonicalMetadataAreRequired()
    {
        var result = FileRules.Validate("Fictional policy.PDF", "application/pdf", Pdf.Length, Pdf, Pdf);
        Assert.Equal("Fictional policy.PDF", result.Name); Assert.Equal("application/pdf", result.MediaType); Assert.Equal(Pdf.Length, result.ByteLength);
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD1sAAAAASUVORK5CYII=");
        Assert.Equal("image/png", FileRules.Validate("evidence.png", "image/png", png.Length, png, png).MediaType);
        byte[] jpeg = [0xff, 0xd8, 0xff, 0xe0, 0x00, 0x02, 0xff, 0xd9];
        Assert.Equal("image/jpeg", FileRules.Validate("evidence.jpeg", "image/jpeg", jpeg.Length, jpeg, jpeg).MediaType);
        // This is deterministic signature screening, not an image decoder or antivirus claim.
    }

    [Theory]
    [InlineData("policy.png", "application/pdf")]
    [InlineData("policy.pdf", "text/html")]
    [InlineData("policy.pdf", "application/pdf; charset=utf-8")]
    [InlineData("policy.svg", "image/svg+xml")]
    public void ClaimedMimeAndExtensionCannotOverrideClosedAllowlist(string name, string media)
        => Assert.Throws<FileRuleException>(() => FileRules.Validate(name, media, Pdf.Length, Pdf, Pdf));

    [Fact]
    public void TruncatedSpoofedAndOversizeContentFailsClosed()
    {
        Assert.Throws<FileRuleException>(() => FileRules.Validate("policy.pdf", "application/pdf", 0, [], []));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("policy.pdf", "application/pdf", FileRules.MaximumFileBytes + 1L, Pdf, Pdf));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("policy.pdf", "application/pdf", Pdf.Length, Pdf, Pdf, maximumBytes: 10));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("policy.pdf", "application/pdf", Pdf.Length, "<html>"u8, Pdf));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("policy.pdf", "application/pdf", Pdf.Length, Pdf, "not finished"u8));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("policy.pdf", "application/pdf", Pdf.Length, Pdf, "%%EOF<script>"u8));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("evidence.png", "image/png", 8, [137,80,78,71,13,10,26,10], []));
        Assert.Throws<FileRuleException>(() => FileRules.Validate("evidence.jpg", "image/jpeg", 8, [255,216,255,224], [255,0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => FileRules.Validate("policy.pdf", "application/pdf", Pdf.Length, Pdf, Pdf, maximumBytes: FileRules.MaximumFileBytes + 1));
    }
}
