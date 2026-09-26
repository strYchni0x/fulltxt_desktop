using Fulltxt.Core.Crypto;
using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Models;
using Fulltxt.Core.Search;

namespace Fulltxt.Core.Tests;

public sealed class LocalFolderIndexingTests : IDisposable
{
    private readonly string tempRoot;
    private readonly string folderToIndex;
    private readonly string dbPath;
    private readonly string keyPath;

    public LocalFolderIndexingTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "fulltxt-tests-" + Guid.NewGuid());
        folderToIndex = Path.Combine(tempRoot, "docs");
        Directory.CreateDirectory(folderToIndex);
        dbPath = Path.Combine(tempRoot, "index.db");
        keyPath = Path.Combine(tempRoot, "index.key");
    }

    public void Dispose()
    {
        try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task IndexAndSearch_FindsWordInPlainTextFile()
    {
        File.WriteAllText(Path.Combine(folderToIndex, "notizen.txt"), "Der Fuchs springt über den faulen Hund.");
        File.WriteAllText(Path.Combine(folderToIndex, "anderes.txt"), "Etwas komplett anderes ohne Bezug.");

        var keyHex = IndexKeyStore.GetOrCreateKeyHex(keyPath);
        using var database = new IndexDatabase(dbPath, keyHex);
        var sourceRepository = new SourceRepository(database);
        var fileIndexRepository = new FileIndexRepository(database);
        var extractors = new ContentExtractorRegistry();
        var indexer = new LocalFolderIndexer(fileIndexRepository, extractors, new IndexingOptions());
        var search = new SearchService(database);

        var sourceId = sourceRepository.Add(new FileSource
        {
            Type = SourceType.LocalFolder,
            DisplayName = "Testordner",
            RootPath = folderToIndex,
            CreatedUtc = DateTime.UtcNow,
        });
        var source = sourceRepository.GetById(sourceId)!;

        var summary = await indexer.IndexAsync(source);

        Assert.Equal(2, summary.Added);
        Assert.Equal(0, summary.Skipped);

        var hits = search.Search("Fuchs");
        Assert.Single(hits);
        Assert.Equal("notizen.txt", hits[0].FileName);
    }

    [Fact]
    public async Task SecondRun_SkipsUnchangedFiles()
    {
        File.WriteAllText(Path.Combine(folderToIndex, "a.txt"), "Erste Datei mit Inhalt.");
        File.WriteAllText(Path.Combine(folderToIndex, "b.txt"), "Zweite Datei mit Inhalt.");

        var keyHex = IndexKeyStore.GetOrCreateKeyHex(keyPath);
        using var database = new IndexDatabase(dbPath, keyHex);
        var sourceRepository = new SourceRepository(database);
        var indexer = new LocalFolderIndexer(new FileIndexRepository(database), new ContentExtractorRegistry(), new IndexingOptions());
        var id = sourceRepository.Add(new FileSource
        {
            Type = SourceType.LocalFolder, DisplayName = "Delta", RootPath = folderToIndex, CreatedUtc = DateTime.UtcNow,
        });
        var source = sourceRepository.GetById(id)!;

        await indexer.IndexAsync(source);
        var second = await indexer.IndexAsync(source);

        // Regression: die Zeitstempel wurden früher in Ortszeit zurückgelesen und nie als "unverändert" erkannt.
        Assert.Equal(2, second.Unchanged);
        Assert.Equal(0, second.Added + second.Updated);
    }

    [Fact]
    public async Task Reindex_DetectsDeletedFile()
    {
        var filePath = Path.Combine(folderToIndex, "temp.txt");
        File.WriteAllText(filePath, "Vergängliche Inhalte für den Löschtest.");

        var keyHex = IndexKeyStore.GetOrCreateKeyHex(keyPath);
        using var database = new IndexDatabase(dbPath, keyHex);
        var sourceRepository = new SourceRepository(database);
        var fileIndexRepository = new FileIndexRepository(database);
        var indexer = new LocalFolderIndexer(fileIndexRepository, new ContentExtractorRegistry(), new IndexingOptions());

        var sourceId = sourceRepository.Add(new FileSource
        {
            Type = SourceType.LocalFolder,
            DisplayName = "Löschtest",
            RootPath = folderToIndex,
            CreatedUtc = DateTime.UtcNow,
        });
        var source = sourceRepository.GetById(sourceId)!;

        await indexer.IndexAsync(source);
        Assert.Equal(1, fileIndexRepository.CountFiles(source.Id));

        File.Delete(filePath);
        var summary = await indexer.IndexAsync(source);

        Assert.Equal(1, summary.Deleted);
        Assert.Equal(0, fileIndexRepository.CountFiles(source.Id));
    }

    [Fact]
    public void IndexKeyStore_PersistsAndRestoresSameKey()
    {
        var first = IndexKeyStore.GetOrCreateKeyHex(keyPath);
        var second = IndexKeyStore.GetOrCreateKeyHex(keyPath);
        Assert.Equal(first, second);
    }

    [Fact]
    public void IndexDatabase_WrongKeyCannotReadExistingData()
    {
        var correctKeyHex = IndexKeyStore.GetOrCreateKeyHex(keyPath);
        using (var database = new IndexDatabase(dbPath, correctKeyHex))
        {
            new SourceRepository(database).Add(new FileSource
            {
                Type = SourceType.LocalFolder,
                DisplayName = "geheim",
                RootPath = folderToIndex,
                CreatedUtc = DateTime.UtcNow,
            });
        }

        var wrongKeyHex = Convert.ToHexString(new byte[32]); // alle Nullbytes statt echtem Schlüssel
        Assert.ThrowsAny<Exception>(() =>
        {
            using var wrongDb = new IndexDatabase(dbPath, wrongKeyHex);
        });
    }

    [Fact]
    public async Task Summary_ClassifiesNotSearchableFilesByReason()
    {
        File.WriteAllText(Path.Combine(folderToIndex, "text.txt"), "Durchsuchbarer Inhalt.");
        File.WriteAllBytes(Path.Combine(folderToIndex, "foto.jpg"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(folderToIndex, "leer.txt"), "   ");
        File.WriteAllText(Path.Combine(folderToIndex, "gross.txt"), new string('x', 200));

        using var database = new IndexDatabase(dbPath, IndexKeyStore.GetOrCreateKeyHex(keyPath));
        var sourceRepository = new SourceRepository(database);
        var files = new FileIndexRepository(database);
        var indexer = new LocalFolderIndexer(files, new ContentExtractorRegistry(), new IndexingOptions { MaxFileSizeBytes = 100 });
        var id = sourceRepository.Add(new FileSource
        {
            Type = SourceType.LocalFolder, DisplayName = "Gruende", RootPath = folderToIndex, CreatedUtc = DateTime.UtcNow,
        });

        var summary = await indexer.IndexAsync(sourceRepository.GetById(id)!);

        Assert.Equal(4, summary.Added);
        Assert.Equal(1, summary.SkippedUnsupported); // foto.jpg
        Assert.Equal(1, summary.SkippedNoText);      // leer.txt
        Assert.Equal(1, summary.SkippedTooLarge);    // gross.txt
        Assert.Equal(3, summary.Skipped);
        Assert.Contains("3 nicht durchsuchbar", summary.Describe());
        Assert.Equal((4, 3), files.CountFilesDetailed(id));

        // Auch nach einem zweiten Lauf bleibt die Zahl aus der Datenbank korrekt.
        var second = await indexer.IndexAsync(sourceRepository.GetById(id)!);
        Assert.Equal(0, second.Skipped);
        Assert.Equal((4, 3), files.CountFilesDetailed(id));
    }

    [Fact]
    public async Task Search_RespectsResultLimit()
    {
        for (var i = 0; i < 12; i++) File.WriteAllText(Path.Combine(folderToIndex, $"treffer{i}.txt"), "Gemeinsames Stichwort im Text.");

        using var database = new IndexDatabase(dbPath, IndexKeyStore.GetOrCreateKeyHex(keyPath));
        var sourceRepository = new SourceRepository(database);
        var indexer = new LocalFolderIndexer(new FileIndexRepository(database), new ContentExtractorRegistry(), new IndexingOptions());
        var id = sourceRepository.Add(new FileSource
        {
            Type = SourceType.LocalFolder, DisplayName = "Limit", RootPath = folderToIndex, CreatedUtc = DateTime.UtcNow,
        });
        await indexer.IndexAsync(sourceRepository.GetById(id)!);
        var search = new SearchService(database);

        Assert.Equal(5, search.Search("Stichwort", 5).Count);
        Assert.Equal(12, search.Search("Stichwort", 500).Count);
        Assert.Equal(12, search.Search("Stichwort").Count); // Standardlimit 100
    }

    [Fact]
    public async Task Cancellation_StopsIndexing_AndKeepsAlreadyIndexedFiles()
    {
        for (var i = 0; i < 5; i++) File.WriteAllText(Path.Combine(folderToIndex, $"datei{i}.txt"), $"Inhalt {i}");

        using var database = new IndexDatabase(dbPath, IndexKeyStore.GetOrCreateKeyHex(keyPath));
        var sourceRepository = new SourceRepository(database);
        var files = new FileIndexRepository(database);
        var indexer = new LocalFolderIndexer(files, new ContentExtractorRegistry(), new IndexingOptions());
        var id = sourceRepository.Add(new FileSource
        {
            Type = SourceType.LocalFolder, DisplayName = "Abbruch", RootPath = folderToIndex, CreatedUtc = DateTime.UtcNow,
        });
        var source = sourceRepository.GetById(id)!;

        using var cts = new CancellationTokenSource();
        var reported = 0;
        var progress = new SyncProgress(_ => { if (++reported == 2) cts.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => indexer.IndexAsync(source, progress, cts.Token));

        var (total, _) = files.CountFilesDetailed(id);
        Assert.InRange(total, 1, 4);

        // Der nächste Lauf setzt fort und indexiert den Rest.
        var resumed = await indexer.IndexAsync(source);
        Assert.Equal(5, files.CountFilesDetailed(id).Total);
        Assert.Equal(5, resumed.Added + resumed.Updated + resumed.Unchanged);
    }

    /// <summary>Progress&lt;T&gt; meldet asynchron über den SynchronizationContext - für deterministische Tests direkt aufrufen.</summary>
    private sealed class SyncProgress(Action<string> onReport) : IProgress<string>
    {
        public void Report(string value) => onReport(value);
    }
}
