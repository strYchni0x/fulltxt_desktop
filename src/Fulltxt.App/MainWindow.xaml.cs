using System.Windows;
using System.Windows.Input;
using Fulltxt.App.ViewModels;
using Fulltxt.Core.Models;

namespace Fulltxt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void SearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SearchResultsList.SelectedItem is SearchHit hit)
        {
            viewModel.OpenResultCommand.Execute(hit);
        }
    }
}
