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

/// <summary>Ergebnis eines Indexierungslaufs. "Nicht durchsuchbar" (Skipped) sind Dateien, die zwar erfasst wurden,
/// deren Inhalt aber nicht im Volltextindex liegt - und die deshalb auch nicht in Suchergebnissen erscheinen.</summary>
public sealed record IndexingSummary(
    int Added,
    int Updated,
    int Deleted,
    int Unchanged,
    int Failed = 0,
    int SkippedUnsupported = 0,
    int SkippedNoText = 0,
    int SkippedTooLarge = 0)
{
    public int Total => Added + Updated + Unchanged;

    public int Skipped => SkippedUnsupported + SkippedNoText + SkippedTooLarge;

    /// <summary>Erklärung für Tooltips: was "nicht durchsuchbar" bedeutet.</summary>
    public const string SkippedExplanation =
        "Nicht durchsuchbar: Diese Dateien wurden gefunden, aber es konnte kein Text ausgelesen werden. " +
        "Sie erscheinen nicht in den Suchergebnissen. Gründe: Dateityp wird nicht unterstützt (z. B. Bilder, Archive), " +
        "kein Text enthalten (z. B. gescannte PDFs ohne Textebene, leere oder passwortgeschützte Dateien) " +
        "oder die Datei ist größer als das Größenlimit. Alle anderen Dateien sind vollständig durchsuchbar.";

    /// <summary>Kurztext für die Quellenkachel, z. B. "4229 neu, 0 aktualisiert, 0 entfernt".</summary>
    public string Describe()
    {
        var text = $"{Added} neu, {Updated} aktualisiert, {Deleted} entfernt";

        if (Skipped > 0)
        {
            var reasons = new List<string>();
            if (SkippedUnsupported > 0) reasons.Add($"{SkippedUnsupported} Dateityp nicht unterstützt");
            if (SkippedNoText > 0) reasons.Add($"{SkippedNoText} ohne lesbaren Text");
            if (SkippedTooLarge > 0) reasons.Add($"{SkippedTooLarge} zu groß");
            text += $"{Environment.NewLine}{Skipped} nicht durchsuchbar: {string.Join(", ", reasons)}";
        }

        if (Failed > 0) text += $"{Environment.NewLine}{Failed} nicht lesbar, wird beim nächsten Lauf erneut versucht";
        return text;
    }
}
