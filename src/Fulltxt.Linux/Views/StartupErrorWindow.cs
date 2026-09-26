using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fulltxt.Linux.Views;

/// <summary>Einfaches Fenster für Fehler beim Start (z.B. kein Schlüsselbund), damit die App nicht kommentarlos abstürzt.</summary>
public sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(string message)
    {
        Title = "FullTXT kann nicht starten";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var close = new Button { Content = "Schließen", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "FullTXT kann nicht starten", FontSize = 20, FontWeight = FontWeight.ExtraBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                close,
            },
        };
    }
}
