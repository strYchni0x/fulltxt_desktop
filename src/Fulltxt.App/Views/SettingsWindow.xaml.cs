using System.Windows;
using Fulltxt.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Fulltxt.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        App.Services.GetRequiredService<ThemeService>().Attach(this);
    }
}
