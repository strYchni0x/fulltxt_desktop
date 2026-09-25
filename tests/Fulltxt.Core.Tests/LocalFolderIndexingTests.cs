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
}
