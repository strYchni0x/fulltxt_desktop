using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Fulltxt.Core.Models;
using Fulltxt.Linux.ViewModels;

namespace Fulltxt.Linux.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = null!;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        Opened += (_, _) => SearchBox.Focus();
    }

    private void SearchResultsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Doppelklicks auf die Aktionsbuttons nicht zusätzlich als Karten-Doppelklick werten.
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null)) return;

        if (SearchResultsList.SelectedItem is SearchHit hit)
        {
            viewModel.PrimaryActionCommand.Execute(hit);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
