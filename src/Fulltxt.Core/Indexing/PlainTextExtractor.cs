namespace Fulltxt.Core.Indexing;

/// <summary>Deckt reine Textformate ab: Text, Markdown, Quellcode, Konfigurationsdateien, Logs.</summary>
public sealed class PlainTextExtractor : IContentExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".ini", ".cfg", ".conf", ".cs", ".ts", ".js", ".py", ".java", ".kt", ".c", ".cpp", ".h",
        ".html", ".htm", ".css", ".sql", ".sh", ".ps1", ".bat",
    };

    public bool CanHandle(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<string?> ExtractTextAsync(Stream content, CancellationToken ct = default)
    {
        using var reader = new StreamReader(content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
