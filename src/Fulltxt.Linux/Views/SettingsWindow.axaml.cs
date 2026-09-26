using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Fulltxt.Linux.Services;
using Fulltxt.Linux.ViewModels;

namespace Fulltxt.Linux.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SyncThemeRadios();
    }

    private void SyncThemeRadios()
    {
        if (DataContext is not MainViewModel viewModel) return;

        ThemeSystem.IsChecked = viewModel.SelectedTheme == ThemeMode.System;
        ThemeLight.IsChecked = viewModel.SelectedTheme == ThemeMode.Light;
        ThemeDark.IsChecked = viewModel.SelectedTheme == ThemeMode.Dark;

        ThemeSystem.IsCheckedChanged += (_, _) => Select(viewModel, ThemeSystem, ThemeMode.System);
        ThemeLight.IsCheckedChanged += (_, _) => Select(viewModel, ThemeLight, ThemeMode.Light);
        ThemeDark.IsCheckedChanged += (_, _) => Select(viewModel, ThemeDark, ThemeMode.Dark);
    }

    private static void Select(MainViewModel viewModel, RadioButton button, ThemeMode mode)
    {
        if (button.IsChecked == true) viewModel.SelectedTheme = mode;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }
}
