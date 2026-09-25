namespace Fulltxt.Core.Models;

/// <summary>Eine Quelle, die indexiert wird: ein lokaler Ordner oder ein Cloud-Konto.</summary>
public sealed class FileSource
{
    public long Id { get; init; }
    public required SourceType Type { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Lokaler Wurzelordner (LocalFolder) oder Startordner in der Cloud (z.B. "/").</summary>
    public required string RootPath { get; init; }

    public string? ServerUrl { get; init; }

    /// <summary>Benutzername (WebDAV) bzw. Kontoname/E-Mail zur Anzeige (OAuth-Konten).</summary>
    public string? Username { get; init; }

    /// <summary>DPAPI-geschützte Zugangsdaten: Passwort (WebDAV) oder Token-JSON (OneDrive/Dropbox).
    /// Nie im Klartext gespeichert.</summary>
    public byte[]? ProtectedCredential { get; init; }

    /// <summary>Delta-Cursor des Anbieters für inkrementelle Synchronisierung (OneDrive/Dropbox).</summary>
    public string? SyncCursor { get; init; }

    public DateTime CreatedUtc { get; init; }
}
