using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Styling;

namespace Fulltxt.Linux.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Speichert die Hell/Dunkel-Wahl lokal; bei "System" folgt die App dem Farbmodus des Desktops.</summary>
public sealed class ThemeService
{
    private Application? application;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void Initialize(Application app)
    {
        application = app;
        Mode = LoadMode();
        Apply(Mode, save: false);
    }

    public void Apply(ThemeMode mode, bool save = true)
    {
        Mode = mode;
        if (application is not null)
        {
            application.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        if (save) SaveMode(mode);
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
}
