using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fulltxt.App.Views;
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

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];
    public ObservableCollection<SearchHit> SearchResults { get; } = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Bereit";

    public MainViewModel(SourceRepository sourceRepository, FileIndexRepository fileIndexRepository,
        SearchService searchService, IndexingService indexingService)
    {
        this.sourceRepository = sourceRepository;
        this.fileIndexRepository = fileIndexRepository;
        this.searchService = searchService;
        this.indexingService = indexingService;

        foreach (var source in sourceRepository.GetAll())
        {
            Sources.Add(new SourceItemViewModel(source, fileIndexRepository.CountFiles(source.Id)));
        }
    }

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

        var id = sourceRepository.Add(source);
        var saved = sourceRepository.GetById(id)!;
        var vm = new SourceItemViewModel(saved, 0);
        Sources.Add(vm);
        await IndexSourceAsync(vm);
    }

    [RelayCommand]
    private async Task AddNextcloudAsync()
    {
        var dialog = new AddNextcloudWindow { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.CreatedSource is null) return;

        var id = sourceRepository.Add(dialog.CreatedSource);
        var saved = sourceRepository.GetById(id)!;
        var vm = new SourceItemViewModel(saved, 0);
        Sources.Add(vm);
        await IndexSourceAsync(vm);
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
    }

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            StatusMessage = "Bereit";
            return;
        }

        try
        {
            foreach (var hit in searchService.Search(SearchQuery))
            {
                SearchResults.Add(hit);
            }
            StatusMessage = $"{SearchResults.Count} Treffer";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fehler bei der Suche: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenResult(SearchHit hit)
    {
        var source = Sources.FirstOrDefault(s => s.Source.DisplayName == hit.SourceDisplayName)?.Source;
        if (source is null) return;

        if (source.Type == SourceType.LocalFolder)
        {
            var fullPath = Path.Combine(source.RootPath, hit.RelativePath);
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
        }
        else if (source.ServerUrl is not null)
        {
            // Öffnet die Nextcloud-Weboberfläche; ein Deep-Link direkt auf die Datei ist ein Folgeschritt.
            Process.Start(new ProcessStartInfo(source.ServerUrl) { UseShellExecute = true });
        }
    }

    partial void OnSearchQueryChanged(string value) => Search();

    private async Task IndexSourceAsync(SourceItemViewModel item)
    {
        item.IsBusy = true;
        item.StatusText = "Indexiere …";
        var progress = new Progress<string>(relativePath => item.StatusText = $"Indexiere: {relativePath}");

        try
        {
            var summary = await Task.Run(() => indexingService.IndexSourceAsync(item.Source, progress));
            item.FileCount = fileIndexRepository.CountFiles(item.Source.Id);
            item.StatusText = $"{summary.Added} neu, {summary.Updated} aktualisiert, " +
                               $"{summary.Deleted} entfernt, {summary.Skipped} übersprungen";
        }
        catch (Exception ex)
        {
            item.StatusText = $"Fehler: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
