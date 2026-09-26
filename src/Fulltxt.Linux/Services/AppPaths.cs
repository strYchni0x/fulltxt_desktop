namespace Fulltxt.Linux.Services;

public static class AppPaths
{
    // Windows: %LOCALAPPDATA%\Fulltxt, Linux: $XDG_DATA_HOME/Fulltxt (Standard ~/.local/share/Fulltxt)
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fulltxt");

    public static string DatabaseFilePath => Path.Combine(DataDirectory, "index.db");
    public static string KeyFilePath => Path.Combine(DataDirectory, "index.key");
    public static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");
}
