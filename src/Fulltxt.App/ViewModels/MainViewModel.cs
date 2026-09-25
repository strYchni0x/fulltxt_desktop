using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fulltxt.App.Services;
using Fulltxt.App.Views;
using Fulltxt.Core.Cloud;
using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Models;
using Fulltxt.Core.Search;

namespace Fulltxt.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SourceRepository sourceRepository;
    private readonly FileIndexRepository fileIndexRepository;
    private readonly SearchService searchService;
    private readonly IndexingService indexingService;
    private readonly CloudFileService cloudFileService;
    private readonly ThemeService themeService;

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];
    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string noticeText = string.Empty;

    [ObservableProperty]
    private bool noticeIsError;

    [ObservableProperty]
    private bool showEmptyState = true;

    [ObservableProperty]
    private string emptyStateTitle = string.Empty;

    [ObservableProperty]
    private string emptyStateText = string.Empty;

    [ObservableProperty]
    private ThemeMode selectedTheme;

    public bool HasQuery => !string.IsNullOrWhiteSpace(SearchQuery);
    public bool HasNoSources => Sources.Count == 0;

    public MainViewModel(SourceRepository sourceRepository, FileIndexRepository fileIndexRepository,
        SearchService searchService, IndexingService indexingService, CloudFileService cloudFileService,
        ThemeService themeService)
    {
        this.sourceRepository = sourceRepository;
        this.fileIndexRepository = fileIndexRepository;
        this.searchService = searchService;
        this.indexingService = indexingService;
        this.cloudFileService = cloudFileService;
        this.themeService = themeService;
        selectedTheme = themeService.Mode;

        foreach (var source in sourceRepository.GetAll())
        {
            Sources.Add(new SourceItemViewModel(source, fileIndexRepository.CountFiles(source.Id)));
        }
        RefreshDerivedState();
    }

    partial void OnSelectedThemeChanged(ThemeMode value) => themeService.Apply(value);

    partial void OnSearchQueryChanged(string value) => Search();

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow { DataContext = this, Owner = ActiveWindow() };
        window.ShowDialog();
    }

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    private async Task AddLocalFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner zum Indexieren auswählen" };
        if (dialog.ShowDialog() != true) return;

        var folderName = dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar);
        var displayName = Path.GetFileName(folderName);
        var source = new FileSource
        {
            Type = SourceType.LocalFolder,
            DisplayName = string.IsNullOrEmpty(displayName) ? folderName : displayName,
            RootPath = dialog.FolderName,
            CreatedUtc = DateTime.UtcNow,
        };

        await AddAndIndexAsync(source);
    }

    [RelayCommand]
    private async Task AddCloudAccountAsync()
    {
        var dialog = new AddCloudAccountWindow { Owner = ActiveWindow() };
        if (dialog.ShowDialog() != true || dialog.CreatedSource is null) return;

        await AddAndIndexAsync(dialog.CreatedSource);
    }

    [RelayCommand]
    private async Task ReindexSourceAsync(SourceItemViewModel item) => await IndexSourceAsync(item);

    [RelayCommand]
    private async Task ReindexAllAsync()
    {
        foreach (var item in Sources.ToList())
        {
            await IndexSourceAsync(item);
        }
    }

    [RelayCommand]
    private void RemoveSource(SourceItemViewModel item)
    {
        sourceRepository.Delete(item.Source.Id);
        Sources.Remove(item);
        Search();
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        NoticeText = string.Empty;
        if (HasQuery)
        {
            try
            {
                foreach (var hit in searchService.Search(SearchQuery))
                {
                    SearchResults.Add(hit);
                }
            }
            catch (Exception ex)
            {
                ShowNotice($"Fehler bei der Suche: {ex.Message}", isError: true);
            }
        }
        RefreshDerivedState();
    }

    /// <summary>Doppelklick: lokale Datei öffnen; Cloud-Datei im Browser anzeigen (oder, falls der Anbieter
    /// keinen Link kennt, gezielt herunterladen).</summary>
    [RelayCommand]
    private async Task PrimaryActionAsync(SearchHit hit)
    {
        if (!hit.IsCloud) OpenFile(hit);
        else if (hit.HasWebUrl) OpenInBrowser(hit);
        else await DownloadAsync(hit);
    }

    [RelayCommand]
    private void OpenFile(SearchHit hit)
    {
        var path = LocalPath(hit);
        if (path is null || !File.Exists(path))
        {
            ShowNotice("Die Datei wurde nicht gefunden.", isError: true);
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowInFolder(SearchHit hit)
    {
        var path = LocalPath(hit);
        if (path is null || !File.Exists(path))
        {
            ShowNotice("Die Datei wurde nicht gefunden.", isError: true);
            return;
        }
        RevealInExplorer(path);
    }

    [RelayCommand]
    private void OpenInBrowser(SearchHit hit)
    {
        if (!Uri.TryCreate(hit.WebUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            ShowNotice("Für diese Datei gibt es keinen Browser-Link. Bitte herunterladen.", isError: true);
            return;
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task DownloadAsync(SearchHit hit)
    {
        ShowNotice($"Lade „{hit.FileName}“ herunter …", isError: false);
        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var saved = await cloudFileService.DownloadAsync(hit.FileId, downloads);
            ShowNotice($"Gespeichert in Downloads: {Path.GetFileName(saved)}", isError: false);
            RevealInExplorer(saved);
        }
        catch (Exception ex) when (ex is CloudException or HttpRequestException or IOException)
        {
            ShowNotice($"Download fehlgeschlagen: {ex.Message}", isError: true);
        }
    }

    private string? LocalPath(SearchHit hit)
    {
        var source = Sources.FirstOrDefault(s => s.Source.Id == hit.SourceId)?.Source;
        return source is null || source.Type != SourceType.LocalFolder ? null : Path.Combine(source.RootPath, hit.RelativePath.TrimStart('\\', '/'));
    }

    private static void RevealInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private void ShowNotice(string text, bool isError)
    {
        NoticeText = text;
        NoticeIsError = isError;
    }

    private async Task AddAndIndexAsync(FileSource source)
    {
        var id = sourceRepository.Add(source);
        var saved = sourceRepository.GetById(id)!;
        var vm = new SourceItemViewModel(saved, 0);
        Sources.Add(vm);
        RefreshDerivedState();
        await IndexSourceAsync(vm);
    }

    private async Task IndexSourceAsync(SourceItemViewModel item)
    {
        item.IsBusy = true;
        item.HasError = false;
        item.StatusText = "Indexiere …";
        var progress = new Progress<string>(relativePath => item.Details = relativePath);

        try
        {
            var summary = await Task.Run(() => indexingService.IndexSourceAsync(item.Source, progress));
            item.FileCount = fileIndexRepository.CountFiles(item.Source.Id);
            item.HasError = summary.Failed > 0;
            item.StatusText = summary.Failed > 0 ? $"{summary.Failed} nicht lesbar" : "Bereit";
            item.Details = $"{summary.Added} neu, {summary.Updated} aktualisiert, " +
                           $"{summary.Deleted} entfernt, {summary.Skipped} ohne Volltext" +
                           (summary.Failed > 0 ? $", {summary.Failed} später erneut versuchen" : "");
        }
        catch (CloudException ex)
        {
            item.HasError = true;
            item.StatusText = ex.IsAuthError ? "Neu anmelden" : "Fehler";
            item.Details = ex.Message;
        }
        catch (Exception ex)
        {
            item.HasError = true;
            item.StatusText = "Fehler";
            item.Details = ex.Message;
        }
        finally
        {
            item.IsBusy = false;
            Search();
        }
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(HasNoSources));

        if (HasQuery && SearchResults.Count > 0)
        {
            StatusMessage = $"{SearchResults.Count} Treffer";
            ShowEmptyState = false;
            return;
        }

        StatusMessage = string.Empty;
        ShowEmptyState = true;
        if (HasNoSources)
        {
            EmptyStateTitle = "Noch keine Quellen";
            EmptyStateText = "Füge über das Zahnrad unten links einen Ordner oder ein Cloud-Konto hinzu.";
        }
        else if (HasQuery)
        {
            EmptyStateTitle = "Keine Treffer";
            EmptyStateText = $"Für „{SearchQuery.Trim()}“ wurde nichts gefunden.";
        }
        else
        {
            EmptyStateTitle = "Volltextsuche für deine Dateien";
            EmptyStateText = $"{Sources.Sum(s => s.FileCount)} Dateien im lokalen, verschlüsselten Index. Tippe oben einen Suchbegriff ein.";
        }
    }

    private static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
}
