using System.Security.Cryptography;
using BackOffice.Infrastructure.Operations;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf.IO;
using Xunit;

namespace BackOffice.IntegrationTests;

public sealed class OperationalPdfFontTests
{
    [Fact]
    public void BundledFontsHavePinnedHashesAndRenderWithoutInstalledFontFallback()
    {
        DocumentFontResolver.Initialize(); DocumentFontResolver.Initialize();
        var resolver = new DocumentFontResolver();
        Assert.Null(resolver.ResolveTypeface("Unapproved remote font", false, false));
        Assert.Equal("975dcda37d80f038dcd143c22e33ca2d97a0cc5a929aace1c749153b0fe1afa5", Convert.ToHexStringLower(SHA256.HashData(resolver.GetFont("plex-regular")!)));
        Assert.Equal("9e6c74a889a700d707613d24548fe4ffa6bc59559a0689d2cf9e133bdcdafb2f", Convert.ToHexStringLower(SHA256.HashData(resolver.GetFont("plex-bold")!)));
        var document = new Document(); document.Styles[StyleNames.Normal]!.Font.Name = DocumentFontResolver.Family;
        var section = document.AddSection(); section.PageSetup.PageFormat = PageFormat.A4;
        section.AddParagraph("Cover MGA - fictional font verification").Format.Font.Bold = true;
        section.AddParagraph("£1,234.56 | €987.65 | Café Noël | Ω | Ж | •");
        var italic = section.AddParagraph("Fictional italic style"); italic.Format.Font.Italic = true;
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument();
        using var output = new MemoryStream(); renderer.PdfDocument.Save(output, closeStream: false);
        using var parsed = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import); Assert.Equal(1, parsed.PageCount);
        var path = Path.GetFullPath(Path.Combine(".local", "phase9-06-fonts", "font-proof.pdf")); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, output.ToArray());
        Assert.True(output.Length > 5000);
    }
}
