namespace Fulltxt.Core.Indexing;

public sealed class IndexingOptions
{
    /// <summary>Dateien über diesem Limit werden nur als Metadaten geführt (kein Volltext),
    /// analog zum 50-MB-Default der Android-App.</summary>
    public long MaxFileSizeBytes { get; init; } = 50 * 1024 * 1024;

    public static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", "$RECYCLE.BIN", "System Volume Information",
    };
}

public sealed record IndexingSummary(int Added, int Updated, int Deleted, int Skipped, int Unchanged, int Failed = 0)
{
    public int Total => Added + Updated + Unchanged;
}
