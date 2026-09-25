using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Fulltxt.App.Services;
using Fulltxt.App.ViewModels;
using Fulltxt.Core.Models;

namespace Fulltxt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel, ThemeService themeService)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        themeService.Attach(this);
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Doppelklicks auf die Aktionsbuttons nicht zusätzlich als Karten-Doppelklick werten.
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase) return;
        }

        if (SearchResultsList.SelectedItem is SearchHit hit)
        {
            viewModel.PrimaryActionCommand.Execute(hit);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
