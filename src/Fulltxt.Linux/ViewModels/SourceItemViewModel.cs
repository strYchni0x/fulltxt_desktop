using CommunityToolkit.Mvvm.ComponentModel;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Models;

namespace Fulltxt.Linux.ViewModels;

public sealed partial class SourceItemViewModel : ObservableObject
{
    public FileSource Source { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int fileCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string statusText = "Bereit";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool isBusy;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string details = string.Empty;

    public SourceItemViewModel(FileSource source, int fileCount)
    {
        Source = source;
        this.fileCount = fileCount;
    }

    public string DisplayName => Source.DisplayName;
    public string SubText => Source.Type switch
    {
        SourceType.LocalFolder => Source.RootPath,
        SourceType.OneDrive or SourceType.Dropbox => Source.Username ?? Source.DisplayName,
        _ when !string.IsNullOrEmpty(Source.ServerUrl) => $"{Source.ServerUrl} ({Source.Username})",
        _ => Source.Username ?? Source.DisplayName,
    };
    public string TypeLabel => CloudProviders.BadgeLabel(Source.Type);
    public bool IsLocal => Source.Type == SourceType.LocalFolder;
    public bool IsCloud => !IsLocal;
    public bool IsIdle => !IsBusy;
    public string Summary => $"{FileCount} Dateien · {StatusText}";
}
