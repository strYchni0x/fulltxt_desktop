using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Fulltxt.App.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Lädt Styles + Hell/Dunkel-Farbpalette zur Laufzeit, speichert die Wahl lokal und
/// folgt bei "System" live dem Windows-Farbmodus.</summary>
public sealed class ThemeService
{
    private const string BaseUri = "pack://application:,,,/Fulltxt.App;component/Themes/";

    private ResourceDictionary? activePalette;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void Initialize()
    {
        Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri(BaseUri + "Styles.xaml") });

        Mode = LoadMode();
        Apply(Mode, save: false);

        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (Mode == ThemeMode.System)
            {
                Application.Current.Dispatcher.BeginInvoke(() => Apply(ThemeMode.System, save: false));
            }
        };
    }

    public void Apply(ThemeMode mode, bool save = true)
    {
        Mode = mode;
        var dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemUsesDarkTheme());

        var merged = Application.Current.Resources.MergedDictionaries;
        if (activePalette is not null) merged.Remove(activePalette);
        activePalette = new ResourceDictionary { Source = new Uri(BaseUri + (dark ? "Dark.xaml" : "Light.xaml")) };
        merged.Add(activePalette);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window, dark);
        }

        if (save) SaveMode(mode);
    }

    /// <summary>Färbt die Titelleiste passend zum Theme (sobald das Fenster ein Handle hat).</summary>
    public void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyTitleBar(window, IsDark);
    }

    private bool IsDark => Mode == ThemeMode.Dark || (Mode == ThemeMode.System && SystemUsesDarkTheme());

    private static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref value, sizeof(int));
    }

    private static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    private static ThemeMode LoadMode()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFilePath)) return ThemeMode.System;
            var settings = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(AppPaths.SettingsFilePath));
            return settings?.Theme ?? ThemeMode.System;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ThemeMode.System;
        }
    }

    private static void SaveMode(ThemeMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFilePath)!);
            File.WriteAllText(AppPaths.SettingsFilePath, JsonSerializer.Serialize(new PersistedSettings { Theme = mode }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Theme bleibt für diese Sitzung aktiv, nur die Persistenz schlägt fehl.
        }
    }

    private sealed class PersistedSettings
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ThemeMode Theme { get; set; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
