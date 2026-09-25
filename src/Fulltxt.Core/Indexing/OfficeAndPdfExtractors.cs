using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace Fulltxt.Core.Indexing;

public sealed class PdfExtractor : IContentExtractor
{
    public bool CanHandle(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string?> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        using var seekable = StreamBuffer.EnsureSeekable(content);
        try
        {
            using var document = PdfDocument.Open(seekable);
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
            }
            var result = text.ToString();
            // Gescannte PDFs ohne Textebene liefern keinen Text (OCR ist ein späterer Schritt).
            return Task.FromResult(string.IsNullOrWhiteSpace(result) ? null : result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kaputte oder passwortgeschützte PDFs überspringen, statt den ganzen Lauf abzubrechen.
            return Task.FromResult<string?>(null);
        }
    }
}

public sealed class XlsxExtractor : IContentExtractor
{
    public bool CanHandle(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase);

    public Task<string?> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        using var seekable = StreamBuffer.EnsureSeekable(content);
        try
        {
            using var document = SpreadsheetDocument.Open(seekable, isEditable: false);
            var text = new StringBuilder();
            var workbook = document.WorkbookPart;
            if (workbook is null) return Task.FromResult<string?>(null);

            foreach (var sheet in workbook.Workbook?.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>() ?? [])
            {
                if (sheet.Name?.Value is { } name) text.AppendLine(name);
            }
            // Zellentexte liegen zentral in der Shared-String-Tabelle; Zahlen sind für die Volltextsuche uninteressant.
            var shared = workbook.SharedStringTablePart?.SharedStringTable;
            if (shared is not null)
            {
                foreach (var item in shared.Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>())
                {
                    ct.ThrowIfCancellationRequested();
                    text.AppendLine(item.InnerText);
                }
            }
            var result = text.ToString();
            return Task.FromResult(string.IsNullOrWhiteSpace(result) ? null : result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult<string?>(null);
        }
    }
}

public sealed class PptxExtractor : IContentExtractor
{
    public bool CanHandle(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".pptx", StringComparison.OrdinalIgnoreCase);

    public Task<string?> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        using var seekable = StreamBuffer.EnsureSeekable(content);
        try
        {
            using var document = PresentationDocument.Open(seekable, isEditable: false);
            var text = new StringBuilder();
            foreach (var slide in document.PresentationPart?.SlideParts ?? [])
            {
                ct.ThrowIfCancellationRequested();
                AppendParagraphs(text, slide.Slide?.Descendants<Drawing.Paragraph>());
                AppendParagraphs(text, slide.NotesSlidePart?.NotesSlide?.Descendants<Drawing.Paragraph>());
            }
            var result = text.ToString();
            return Task.FromResult(string.IsNullOrWhiteSpace(result) ? null : result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private static void AppendParagraphs(StringBuilder text, IEnumerable<Drawing.Paragraph>? paragraphs)
    {
        if (paragraphs is null) return;
        foreach (var paragraph in paragraphs)
        {
            var line = paragraph.InnerText;
            if (!string.IsNullOrWhiteSpace(line)) text.AppendLine(line);
        }
    }
}
