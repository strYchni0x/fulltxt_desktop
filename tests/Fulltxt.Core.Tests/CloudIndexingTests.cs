using Fulltxt.Core.Cloud;
using Fulltxt.Core.Crypto;
using Fulltxt.Core.Data;
using Fulltxt.Core.Indexing;
using Fulltxt.Core.Models;
using Fulltxt.Core.Search;

namespace Fulltxt.Core.Tests;

/// <summary>End-to-End: Cloud-Konto (Fake-WebDAV) wird indexiert; Änderungen, Löschungen und Fehler werden korrekt behandelt,
/// ohne dass Dateien dauerhaft gespeichert werden.</summary>
public sealed class CloudIndexingTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "fulltxt-cloud-" + Guid.NewGuid());
    private readonly FakeWebDavServer server = new();
    private readonly IndexDatabase database;
    private readonly SourceRepository sources;
    private readonly FileIndexRepository files;
    private readonly SearchService search;
    private readonly CloudFolderIndexer indexer;
    private readonly FileSource source;

    public CloudIndexingTests()
    {
        Directory.CreateDirectory(tempRoot);
        database = new IndexDatabase(Path.Combine(tempRoot, "index.db"), IndexKeyStore.GetOrCreateKeyHex(Path.Combine(tempRoot, "index.key")));
        sources = new SourceRepository(database);
        files = new FileIndexRepository(database);
        search = new SearchService(database);
        indexer = new CloudFolderIndexer(sources, files, new ContentExtractorRegistry(), new IndexingOptions(), new CloudConnectorFactory());

        var id = sources.Add(new FileSource
        {
            Type = SourceType.Nextcloud,
            DisplayName = "Test-Cloud",
            RootPath = "/",
            ServerUrl = server.BaseUrl,
            Username = FakeWebDavServer.User,
            ProtectedCredential = SecretProtector.ProtectString(FakeWebDavServer.Password),
            CreatedUtc = DateTime.UtcNow,
        });
        source = sources.GetById(id)!;

        server.Files["/Berichte/Jahresbericht Übersicht.txt"] = new("Der Solarertrag stieg im Jahr 2026 deutlich.", "e1", 101);
        server.Files["/Notizen/ideen.md"] = new("Ideen zum Thema Kennzahlen und Solarertrag.", "e2", 102);
        server.Files["/Bilder/foto.jpg"] = new("kein Text", "e3", 103);
    }

    public void Dispose()
    {
        server.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Index_FindsCloudFiles_WithBrowserLink_AndFallsBackWhenInfinityRejected()
    {
        server.Infinity = InfinityBehavior.ServerError500; // wie Nextcloud bei Depth: infinity

        var summary = await indexer.IndexAsync(source);

        Assert.Equal(2, summary.Added); // .jpg wird nicht aufgenommen
        Assert.Equal(0, summary.Failed);

        var hits = search.Search("Solarertrag");
        Assert.Equal(2, hits.Count);
        var report = hits.Single(h => h.FileName.StartsWith("Jahresbericht"));
        Assert.Equal("/Berichte/Jahresbericht Übersicht.txt", report.RelativePath); // dekodiert, mit Umlaut und Leerzeichen
        Assert.Equal($"{server.BaseUrl}/index.php/f/101", report.WebUrl);
        Assert.NotNull(report.RemoteId);
        Assert.True(report.IsCloud);
    }

    [Fact]
    public async Task Index_UsesSingleInfinityRequest_WhenServerAllowsIt()
    {
        server.Infinity = InfinityBehavior.Allow;

        await indexer.IndexAsync(source);

        Assert.Equal(1, server.Requests.Count(r => r.StartsWith("PROPFIND") && r.EndsWith("depth=infinity")));
        Assert.DoesNotContain(server.Requests, r => r.EndsWith("depth=1"));
    }

    [Fact]
    public async Task SecondRun_DownloadsNothing_WhenETagsUnchanged()
    {
        await indexer.IndexAsync(source);
        var downloadsAfterFirstRun = server.GetCount;

        var summary = await indexer.IndexAsync(source);

        Assert.Equal(2, summary.Unchanged);
        Assert.Equal(0, summary.Added + summary.Updated);
        Assert.Equal(downloadsAfterFirstRun, server.GetCount); // Kernvorteil: kein erneuter Download
    }

    [Fact]
    public async Task ChangedFile_IsReindexed_AndDeletedFile_IsRemoved()
    {
        await indexer.IndexAsync(source);

        server.Files["/Notizen/ideen.md"] = new("Jetzt geht es um Windkraft statt Sonne.", "e2-neu", 102);
        server.Files.Remove("/Berichte/Jahresbericht Übersicht.txt");

        var summary = await indexer.IndexAsync(source);

        Assert.Equal(1, summary.Updated);
        Assert.Equal(1, summary.Deleted);
        Assert.Empty(search.Search("Solarertrag"));
        Assert.Single(search.Search("Windkraft"));
    }

    [Fact]
    public async Task FailedDownload_IsNotMarkedDone_AndRetriedNextRun()
    {
        server.FailGetFor.Add("/Notizen/ideen.md");

        var first = await indexer.IndexAsync(source);
        Assert.Equal(1, first.Failed);
        Assert.Single(search.Search("Solarertrag")); // nur die andere Datei

        server.FailGetFor.Clear();
        var second = await indexer.IndexAsync(source);

        Assert.Equal(0, second.Failed);
        Assert.Equal(2, search.Search("Solarertrag").Count);
    }

    [Fact]
    public async Task DownloadService_SavesSingleFile_WithoutOverwriting()
    {
        await indexer.IndexAsync(source);
        var hit = search.Search("Kennzahlen").Single();
        var service = new CloudFileService(sources, files, new CloudConnectorFactory());
        var target = Path.Combine(tempRoot, "downloads");

        var first = await service.DownloadAsync(hit.FileId, target);
        var second = await service.DownloadAsync(hit.FileId, target);

        Assert.Equal("ideen.md", Path.GetFileName(first));
        Assert.Equal("ideen (1).md", Path.GetFileName(second));
        Assert.Contains("Kennzahlen", await File.ReadAllTextAsync(first));
    }

    [Fact]
    public async Task WrongPassword_ThrowsAuthError()
    {
        using var connector = CloudConnectorFactory.CreateForNewPasswordAccount(
            SourceType.Nextcloud, server.BaseUrl, FakeWebDavServer.User, "falsch");

        var ex = await Assert.ThrowsAsync<CloudException>(() => connector.TestConnectionAsync());
        Assert.True(ex.IsAuthError);
    }

    [Fact]
    public async Task CorrectPassword_TestConnectionSucceeds()
    {
        using var connector = CloudConnectorFactory.CreateForNewPasswordAccount(
            SourceType.Nextcloud, server.BaseUrl, FakeWebDavServer.User, FakeWebDavServer.Password);

        await connector.TestConnectionAsync();
    }

    [Fact]
    public async Task MovedCloudFile_KeepsSingleEntry_ViaRemoteId()
    {
        // Direkt über das Repository: gleiche Remote-ID, neuer Pfad -> Update statt Dublette.
        var sourceId = source.Id;
        files.Upsert(new IndexedFile { SourceId = sourceId, RelativePath = "/alt/a.txt", FileName = "a.txt", SizeBytes = 1,
            ModifiedUtc = DateTime.UtcNow, RemoteId = "id-1", ETag = "v1" }, "Inhalt Solarertrag");
        files.Upsert(new IndexedFile { SourceId = sourceId, RelativePath = "/neu/b.txt", FileName = "b.txt", SizeBytes = 1,
            ModifiedUtc = DateTime.UtcNow, RemoteId = "id-1", ETag = "v2" }, "Inhalt Solarertrag");

        Assert.Equal(1, files.CountFiles(sourceId));
        var hit = search.Search("Solarertrag").Single();
        Assert.Equal("/neu/b.txt", hit.RelativePath);
        await Task.CompletedTask;
    }

    [Fact]
    public void DeleteByPathOrFolder_IsCaseInsensitive_AndRemovesFolderContents()
    {
        var sourceId = source.Id;
        foreach (var path in new[] { "/Ordner/Eins.txt", "/Ordner/Sub/Zwei.txt", "/Anderer/Drei.txt" })
        {
            files.Upsert(new IndexedFile { SourceId = sourceId, RelativePath = path, FileName = Path.GetFileName(path),
                SizeBytes = 1, ModifiedUtc = DateTime.UtcNow }, "Text Sonnenschein");
        }

        var deleted = files.DeleteByPathOrFolder(sourceId, ["/ordner"]); // Dropbox liefert path_lower

        Assert.Equal(2, deleted);
        Assert.Equal("/Anderer/Drei.txt", Assert.Single(search.Search("Sonnenschein")).RelativePath);
    }
}
