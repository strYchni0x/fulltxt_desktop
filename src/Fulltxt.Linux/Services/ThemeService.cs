using Avalonia;
using Avalonia.Styling;
using Fulltxt.Core.Settings;

namespace Fulltxt.Linux.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Speichert die Hell/Dunkel-Wahl lokal; bei "System" folgt die App dem Farbmodus des Desktops.</summary>
public sealed class ThemeService(UserSettings settings)
{
    private Application? application;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public void Initialize(Application app)
    {
        application = app;
        Mode = Enum.TryParse<ThemeMode>(settings.Theme, out var stored) ? stored : ThemeMode.System;
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

        if (save)
        {
            settings.Theme = mode.ToString();
            settings.Save();
        }
    }
}
