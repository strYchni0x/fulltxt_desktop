using System.ComponentModel;
using System.Diagnostics;

namespace Fulltxt.Linux.Services;

/// <summary>Öffnet Dateien, Ordner und Links mit den Standardprogrammen des Desktops (xdg-open).</summary>
public static class Shell
{
    /// <summary>Wirft <see cref="InvalidOperationException"/> mit verständlicher Meldung, wenn kein Öffner verfügbar ist.</summary>
    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { target } });
            }
            else
            {
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false });
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"„{target}“ konnte nicht geöffnet werden (xdg-open fehlt?): {ex.Message}", ex);
        }
    }

    /// <summary>Zeigt den Ordner der Datei im Dateimanager (ein Auswählen der Datei ist über xdg-open nicht portabel).</summary>
    public static void ShowInFolder(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            return;
        }
        Open(Path.GetDirectoryName(filePath) ?? filePath);
    }
}
