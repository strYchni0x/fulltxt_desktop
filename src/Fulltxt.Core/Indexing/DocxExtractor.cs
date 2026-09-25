using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Fulltxt.Core.Indexing;

public sealed class DocxExtractor : IContentExtractor
{
    public bool CanHandle(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<string?> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        // OpenXml braucht Seek-Zugriff; bei Netzwerk-/Antwort-Streams (z.B. WebDAV) vorher puffern.
        Stream seekable = content;
        MemoryStream? buffered = null;
        if (!content.CanSeek)
        {
            buffered = new MemoryStream();
            content.CopyTo(buffered);
            buffered.Position = 0;
            seekable = buffered;
        }

        try
        {
            using var document = WordprocessingDocument.Open(seekable, isEditable: false);
            var body = document.MainDocumentPart?.Document?.Body;
            var text = body?.InnerText;
            return Task.FromResult(string.IsNullOrWhiteSpace(text) ? null : text);
        }
        catch (Exception)
        {
            // Kaputte/verschlüsselte .docx-Dateien werden übersprungen statt den Scan abzubrechen.
            return Task.FromResult<string?>(null);
        }
        finally
        {
            buffered?.Dispose();
        }
    }
}
