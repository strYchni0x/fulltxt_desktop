namespace Fulltxt.Core.Models;

/// <summary>Quellentyp. Der Name wird in der Datenbank gespeichert - bestehende Werte nie umbenennen.</summary>
public enum SourceType
{
    LocalFolder,
    Nextcloud,
    OwnCloud,
    MagentaCloud,
    StratoHidrive,
    Yandex,
    OneDrive,
    Dropbox,
}

public static class SourceTypeExtensions
{
    public static bool IsCloud(this SourceType type) => type != SourceType.LocalFolder;
}
