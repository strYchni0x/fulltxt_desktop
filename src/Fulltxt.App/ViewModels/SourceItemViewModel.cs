using CommunityToolkit.Mvvm.ComponentModel;
using Fulltxt.Core.Models;

namespace Fulltxt.App.ViewModels;

public sealed partial class SourceItemViewModel : ObservableObject
{
    public FileSource Source { get; }

    [ObservableProperty]
    private int fileCount;

    [ObservableProperty]
    private string statusText = "Bereit";

    [ObservableProperty]
    private bool isBusy;

    public SourceItemViewModel(FileSource source, int fileCount)
    {
        Source = source;
        this.fileCount = fileCount;
    }

    public string DisplayName => Source.DisplayName;
    public string SubText => Source.Type == SourceType.LocalFolder
        ? Source.RootPath
        : $"{Source.ServerUrl} ({Source.Username})";
    public string TypeLabel => Source.Type == SourceType.LocalFolder ? "Lokaler Ordner" : "Nextcloud";
}
