using System.IO;

namespace Fulltxt.App.Services;

public static class AppPaths
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fulltxt");

    public static string DatabaseFilePath => Path.Combine(DataDirectory, "index.db");
    public static string KeyFilePath => Path.Combine(DataDirectory, "index.key");
}
