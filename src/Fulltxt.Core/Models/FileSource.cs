namespace Fulltxt.Core.Models;

/// <summary>Eine Quelle, die indexiert wird: ein lokaler Ordner oder eine Nextcloud-Verbindung.</summary>
public sealed class FileSource
{
    public long Id { get; init; }
    public required SourceType Type { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>Lokaler Wurzelordner (LocalFolder) oder WebDAV-Basispfad (Nextcloud, z.B. "/").</summary>
    public required string RootPath { get; init; }

    public string? ServerUrl { get; init; }
    public string? Username { get; init; }

    /// <summary>DPAPI-geschütztes App-Passwort. Nur für Nextcloud, nie im Klartext gespeichert.</summary>
    public byte[]? ProtectedCredential { get; init; }

    public DateTime CreatedUtc { get; init; }
}
