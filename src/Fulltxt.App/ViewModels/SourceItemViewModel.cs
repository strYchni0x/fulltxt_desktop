using CommunityToolkit.Mvvm.ComponentModel;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Models;

namespace Fulltxt.App.ViewModels;

public sealed partial class SourceItemViewModel : ObservableObject
{
    public FileSource Source { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private int fileCount;

    /// <summary>Davon ohne Volltext: erfasst, aber nicht durchsuchbar (kein Text auslesbar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private int notSearchableCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string statusText = "Bereit";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string details = string.Empty;

    public SourceItemViewModel(FileSource source, int fileCount, int notSearchableCount = 0)
    {
        Source = source;
        this.fileCount = fileCount;
        this.notSearchableCount = notSearchableCount;
    }

    /// <summary>Läuft gerade eine Indexierung, kann sie darüber abgebrochen werden.</summary>
    public CancellationTokenSource? Cancellation { get; set; }

    public string DisplayName => Source.DisplayName;
    public string SubText => Source.Type switch
    {
        SourceType.LocalFolder => Source.RootPath,
        SourceType.OneDrive or SourceType.Dropbox => Source.Username ?? Source.DisplayName,
        _ when !string.IsNullOrEmpty(Source.ServerUrl) => $"{Source.ServerUrl} ({Source.Username})",
        _ => Source.Username ?? Source.DisplayName,
    };
    public string TypeLabel => CloudProviders.BadgeLabel(Source.Type);
    public string IconGlyph => Source.Type == SourceType.LocalFolder ? "\uE8B7" : "\uE753";
    public string Summary => NotSearchableCount > 0
        ? $"{FileCount - NotSearchableCount} durchsuchbar · {NotSearchableCount} nicht durchsuchbar · {StatusText}"
        : $"{FileCount} Dateien · {StatusText}";

    /// <summary>Tooltip: erklärt, was "nicht durchsuchbar" bedeutet.</summary>
    public string Hint => NotSearchableCount > 0 ? IndexingSummary.SkippedExplanation : string.Empty;
}
