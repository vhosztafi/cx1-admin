using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BackOffice.Application.Operations;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace BackOffice.Infrastructure.Operations;

public sealed class PolicyDocumentRenderer : IPolicyDocumentRenderer
{
    public const string Version = "pdfsharp-migradoc-6.2.4-cover-1";
    public const string FontVersion = "ibm-plex-sans-6.4.0-383c681f";

    public RenderedPolicyDocument Render(DocumentRenderInput input)
    {
        var contract = DocumentRenderContract.Create(input);
        var content = DocumentPolicyProjection.Create(contract);
        DocumentFontResolver.Initialize();
        var document = new Document();
        document.Info.Title = contract.Title; document.Info.Author = "Cover MGA demo";
        document.Info.Subject = input.Reference + " / " + input.Kind;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = DocumentFontResolver.Family; normal.Font.Size = 9;
        normal.Font.Color = Color.FromRgb(20, 22, 28); normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.OddAndEvenPagesHeaderFooter = false;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2); section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.6); section.PageSetup.RightMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(.8); section.PageSetup.FooterDistance = Unit.FromCentimeter(.8);
        var header = section.Headers.Primary.AddParagraph("COVER  /  MGA BACK OFFICE");
        header.Format.Font.Bold = true; header.Format.Font.Size = 9; header.Format.Font.Color = Color.FromRgb(63, 94, 245);
        var footer = section.Footers.Primary.AddParagraph(input.Reference + "  •  Fictional demonstration  |  Page ");
        footer.Format.Font.Size = 8; footer.AddPageField(); footer.AddText(" of "); footer.AddNumPagesField();
        var title = section.AddParagraph(contract.Title); title.Format.Font.Size = 22; title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(8);
        section.AddParagraph(input.Reference + "  •  " + ProductName(input.ProductCode));
        var disclaimer = section.AddParagraph("DEMONSTRATION ONLY — no insurance is provided by this document.");
        disclaimer.Format.Font.Bold = true; disclaimer.Format.SpaceAfter = Unit.FromPoint(12);
        if (!contract.LegacyTemplate) section.AddParagraph(contract.Notice);

        foreach (var group in content) AddSection(section, group);
        AddSection(section, new("Document provenance", [
            new("Immutable source", input.SourceKind + " / " + input.SourceId),
            new("Source SHA-256", input.SourceHash),
            new("Template version", input.TemplateId.ToString()),
            new("Template SHA-256", input.TemplateHash),
            new("Question labels SHA-256", DocumentPolicyProjection.QuestionLabelsHash),
            new("Rendering", Version + " / " + DocumentPolicyProjection.Version + " / " + FontVersion)]));
        if (contract.LegacyTemplate)
            AddSection(section, new("Retained template wording (historical provenance)", [
                new("Original notice", contract.Notice),
                new("Meaning", "The original wording is retained unchanged. It describes the historical template, not the generation state of this PDF.")]));
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument();
        var pageCount = renderer.PdfDocument.PageCount;
        if (pageCount > 300) throw new DocumentRenderException("document-render-page-limit");
        using var output = new MemoryStream(); renderer.PdfDocument.Save(output, closeStream: false);
        if (output.Length > FileRules.MaximumFileBytes) throw new DocumentRenderException("document-render-file-limit");
        var bytes = output.ToArray();
        return new(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), pageCount, Version, DocumentPolicyProjection.Version, FontVersion);
    }

    private static void AddSection(Section section, DocumentSection group)
    {
        var heading = section.AddParagraph(group.Title); heading.Format.Font.Bold = true; heading.Format.Font.Size = 12;
        heading.Format.SpaceBefore = Unit.FromPoint(12); heading.Format.SpaceAfter = Unit.FromPoint(6); heading.Format.KeepWithNext = true;
        var table = section.AddTable(); table.Borders.Width = .35; table.Borders.Color = Color.FromRgb(228, 230, 236);
        table.AddColumn(Unit.FromCentimeter(7)); table.AddColumn(Unit.FromCentimeter(10.8));
        table.Rows.LeftIndent = Unit.FromPoint(0); table.TopPadding = Unit.FromPoint(4); table.BottomPadding = Unit.FromPoint(4);
        var header = table.AddRow(); header.HeadingFormat = true; header.Shading.Color = Color.FromRgb(246, 247, 249);
        header.Format.Font.Bold = true; header.Cells[0].AddParagraph("Recorded field"); header.Cells[1].AddParagraph("Value");
        foreach (var field in group.Fields)
        {
            // MigraDoc keeps table rows together. Bounded continuations prevent
            // a long declaration from becoming a single row taller than a page.
            var labels = Chunks(field.Label); var values = Chunks(field.Value);
            for (var index = 0; index < Math.Max(labels.Count, values.Count); index++)
            {
                var row = table.AddRow(); row.VerticalAlignment = VerticalAlignment.Top;
                row.Cells[0].AddParagraph(index < labels.Count ? WrapLongTokens(labels[index], 24) : "(continued)");
                row.Cells[1].AddParagraph(index < values.Count ? WrapLongTokens(values[index], 40) : "");
            }
        }
    }

    private static List<string> Chunks(string text)
    {
        var parts = new List<string>();
        while (text.Length > 600)
        {
            var boundary = text.LastIndexOf(' ', 600, 300);
            if (boundary < 300) boundary = 600;
            if (char.IsHighSurrogate(text[boundary - 1])) boundary--;
            parts.Add(text[..boundary]); text = text[boundary..];
        }
        parts.Add(text); return parts;
    }
    private static string WrapLongTokens(string text, int width) => Regex.Replace(text, @"\S+", match =>
    {
        if (match.Length <= width) return match.Value;
        var value = match.Value; var parts = new List<string>();
        while (value.Length > width)
        {
            var take = char.IsHighSurrogate(value[width - 1]) ? width - 1 : width;
            parts.Add(value[..take]); value = value[take..];
        }
        parts.Add(value); return string.Join("\n", parts);
    }, RegexOptions.CultureInvariant);
    private static string ProductName(string code) => code switch { "motor-trade-road-risks" => "Motor Trade Road Risks", "motor-trade-combined" => "Motor Trade Combined", _ => "Commercial Combined" };
}
