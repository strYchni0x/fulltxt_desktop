using Fulltxt.Core.Models;

namespace Fulltxt.Core.Cloud;

public enum CloudAuthKind
{
    /// <summary>Benutzername + (App-)Passwort per WebDAV/Basic-Auth.</summary>
    Password,

    /// <summary>OAuth-Anmeldung im Standardbrowser (kein Passwort in dieser App).</summary>
    OAuth,
}

public sealed record CloudProviderInfo(
    SourceType Type,
    string Name,
    string BadgeLabel,
    CloudAuthKind Auth,
    string? DefaultServerUrl,
    bool ServerEditable,
    string UsernameLabel,
    string SecretLabel,
    string Description);

/// <summary>Katalog der unterstützten Cloud-Anbieter (gleicher Umfang wie die Android-App, ohne Google Drive).</summary>
public static class CloudProviders
{
    public static readonly IReadOnlyList<CloudProviderInfo> All =
    [
        new(SourceType.OneDrive, "OneDrive", "ONEDRIVE", CloudAuthKind.OAuth, null, false, "", "",
            "Anmeldung im Browser bei Microsoft. Nur Lesezugriff."),
        new(SourceType.Dropbox, "Dropbox", "DROPBOX", CloudAuthKind.OAuth, null, false, "", "",
            "Anmeldung im Browser bei Dropbox. Nur Lesezugriff."),
        new(SourceType.Nextcloud, "Nextcloud", "NEXTCLOUD", CloudAuthKind.Password, null, true,
            "Benutzername", "App-Passwort",
            "App-Passwort erstellen unter Einstellungen → Sicherheit → App-Passwörter."),
        new(SourceType.MagentaCloud, "MagentaCloud", "MAGENTACLOUD", CloudAuthKind.Password, "https://magentacloud.de", true,
            "Benutzername", "App-Passwort",
            "Telekom MagentaCloud (Nextcloud-Backend)."),
        new(SourceType.StratoHidrive, "Strato HiDrive", "HIDRIVE", CloudAuthKind.Password, "https://webdav.hidrive.strato.com", false,
            "Strato-Benutzername", "Passwort",
            "Strato HiDrive per WebDAV."),
        new(SourceType.OwnCloud, "ownCloud", "OWNCLOUD", CloudAuthKind.Password, null, true,
            "Benutzername", "Passwort oder App-Passwort",
            "Eigene ownCloud-Instanz."),
        new(SourceType.Yandex, "Yandex Disk", "YANDEX", CloudAuthKind.Password, "https://webdav.yandex.com", false,
            "Yandex-Login", "Passwort oder App-Passwort",
            "Bei aktiver Zwei-Faktor-Anmeldung ein App-Passwort verwenden."),
    ];

    public static CloudProviderInfo? Find(SourceType type) => All.FirstOrDefault(p => p.Type == type);

    public static string BadgeLabel(SourceType type) =>
        type == SourceType.LocalFolder ? "ORDNER" : Find(type)?.BadgeLabel ?? type.ToString().ToUpperInvariant();
}
