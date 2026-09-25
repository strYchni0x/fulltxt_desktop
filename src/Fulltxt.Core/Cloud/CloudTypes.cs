namespace Fulltxt.Core.Cloud;

/// <summary>Eine Datei in der Cloud, wie sie ein Anbieter beim Auflisten meldet.</summary>
/// <param name="RemoteId">Anbieter-interne ID (WebDAV: href, OneDrive/Dropbox: Element-ID) für gezielte Downloads.</param>
/// <param name="Path">Vollständiger Cloud-Pfad inkl. Dateiname, mit "/" beginnend.</param>
/// <param name="ETag">Änderungsmarker (ETag/rev) - unverändert = kein erneuter Download nötig.</param>
/// <param name="WebUrl">Link zur Anzeige der Datei im Browser, falls der Anbieter einen kennt.</param>
public sealed record CloudItem(
    string RemoteId,
    string Path,
    string Name,
    long Size,
    DateTime ModifiedUtc,
    string? ETag,
    string? WebUrl);

/// <summary>Minimale Angaben, um eine einzelne Datei gezielt herunterzuladen.</summary>
public sealed record CloudFileRef(string RemoteId, string Path, string Name);

/// <summary>Ergebnis einer Synchronisierung. Bei <see cref="IsFullListing"/> enthält <see cref="Changed"/>
/// alle Dateien (fehlende gelten als gelöscht), sonst nur Änderungen seit dem Cursor.</summary>
public sealed record CloudChanges(
    IReadOnlyList<CloudItem> Changed,
    IReadOnlyList<string> DeletedRemoteIds,
    IReadOnlyList<string> DeletedPaths,
    bool IsFullListing,
    string? NextCursor);

public interface ICloudConnector : IDisposable
{
    /// <summary>Wirft <see cref="CloudException"/> bei falschen Zugangsdaten oder unerreichbarem Server.</summary>
    Task TestConnectionAsync(CancellationToken ct = default);

    Task<CloudChanges> GetChangesAsync(string? cursor, CancellationToken ct = default);

    /// <summary>Öffnet den Inhalt einer einzelnen Datei als Stream - es wird nichts dauerhaft gespeichert.</summary>
    Task<Stream> OpenReadAsync(CloudFileRef file, CancellationToken ct = default);
}

public class CloudException(string message, bool isAuthError = false, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>True, wenn Zugangsdaten falsch/abgelaufen sind (Konto neu verbinden).</summary>
    public bool IsAuthError { get; } = isAuthError;
}
