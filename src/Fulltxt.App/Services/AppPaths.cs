using System.IO;

namespace Fulltxt.App.Services;

public static class AppPaths
{
    /// <summary>Datenordner des Index. Mit der Umgebungsvariable <c>FULLTXT_DATA_DIR</c> lässt er sich verlegen
    /// (z. B. für einen zweiten, getrennten Index oder zum Testen). Standard: Windows
    /// <c>%LOCALAPPDATA%\Fulltxt</c>, Linux <c>~/.local/share/Fulltxt</c>.</summary>
    private static readonly string DataDirectory =
        Environment.GetEnvironmentVariable("FULLTXT_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fulltxt");

    public static string DatabaseFilePath => Path.Combine(DataDirectory, "index.db");
    public static string KeyFilePath => Path.Combine(DataDirectory, "index.key");
    public static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");
}
