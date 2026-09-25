namespace Fulltxt.Core.Models;

public sealed class IndexedFile
{
    public long Id { get; init; }
    public required long SourceId { get; init; }
    public required string RelativePath { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime ModifiedUtc { get; init; }
    public string? ContentHash { get; init; }
    public string? ETag { get; init; }
    public DateTime IndexedUtc { get; init; }

    /// <summary>True, wenn die Datei zu groß ist oder kein Extractor existiert (Metadaten ohne Volltext).</summary>
    public bool Skipped { get; init; }
}
