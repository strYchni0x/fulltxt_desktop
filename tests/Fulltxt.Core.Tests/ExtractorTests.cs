using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using DocumentFormat.OpenXml.Spreadsheet;
using Fulltxt.Core.Indexing;
using Microsoft.Extensions.DependencyInjection;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace Fulltxt.Core.Tests;

public sealed class ExtractorTests
{
    /// <summary>Wie ein HTTP-Antwortstream: nicht durchsuchbar, damit die Pufferung mitgetestet wird.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    [Fact]
    public async Task Pdf_ExtractsText_FromForwardOnlyStream()
    {
        var pdf = BuildMinimalPdf("Solarertrag steigt");

        var text = await new PdfExtractor().ExtractTextAsync(new ForwardOnlyStream(pdf));

        Assert.Contains("Solarertrag", text);
    }

    [Fact]
    public async Task Pdf_CorruptFile_ReturnsNullInsteadOfThrowing()
    {
        var text = await new PdfExtractor().ExtractTextAsync(new MemoryStream(Encoding.ASCII.GetBytes("das ist kein pdf")));

        Assert.Null(text);
    }

    [Fact]
    public async Task Xlsx_ExtractsSharedStringsAndSheetNames()
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook(new Sheets(new Sheet { Name = "Energiebilanz", SheetId = 1, Id = "rId1" }));
            var shared = workbook.AddNewPart<SharedStringTablePart>();
            shared.SharedStringTable = new SharedStringTable(
                new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text("Einspeisevergütung")),
                new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text("Eigenverbrauch")));
        }
        stream.Position = 0;

        var text = await new XlsxExtractor().ExtractTextAsync(new ForwardOnlyStream(stream.ToArray()));

        Assert.Contains("Energiebilanz", text);
        Assert.Contains("Einspeisevergütung", text);
    }

    [Fact]
    public async Task Pptx_ExtractsSlideText()
    {
        var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation, autoSave: true))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new Presentation();
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = new Slide(new CommonSlideData(new ShapeTree(
                new Shape(new TextBody(new Drawing.BodyProperties(), new Drawing.Paragraph(
                    new Drawing.Run(new Drawing.Text("Quartalsplanung Photovoltaik")))))))
            );
            presentationPart.Presentation.SlideIdList = new SlideIdList(
                new SlideId { Id = 256U, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        }
        stream.Position = 0;

        var text = await new PptxExtractor().ExtractTextAsync(new ForwardOnlyStream(stream.ToArray()));

        Assert.Contains("Quartalsplanung", text);
    }

    [Fact]
    public void Registry_ResolvedFromDiContainer_HasDefaultExtractors()
    {
        // Regression: ein Konstruktor mit IEnumerable-Parameter bekam vom Container eine leere Liste,
        // die App indexierte dadurch gar nichts (Unit-Tests erzeugen die Registry direkt und sahen es nicht).
        using var provider = new ServiceCollection()
            .AddSingleton<ContentExtractorRegistry>()
            .BuildServiceProvider();

        var registry = provider.GetService(typeof(ContentExtractorRegistry)) as ContentExtractorRegistry;

        Assert.NotNull(registry!.FindExtractor("notiz.txt"));
        Assert.NotNull(registry.FindExtractor("bericht.pdf"));
    }

    [Theory]
    [InlineData("bericht.PDF", true)]
    [InlineData("tabelle.xlsx", true)]
    [InlineData("folien.pptx", true)]
    [InlineData("foto.jpg", false)]
    public void Registry_KnowsSupportedFormats(string fileName, bool supported) =>
        Assert.Equal(supported, new ContentExtractorRegistry().FindExtractor(fileName) is not null);

    /// <summary>Kleinste gültige PDF mit einer Textzeile; die xref-Offsets werden berechnet.</summary>
    private static byte[] BuildMinimalPdf(string text)
    {
        var content = $"BT /F1 18 Tf 72 720 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) builder.Append($"{offset:D10} 00000 n \n");
        builder.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
